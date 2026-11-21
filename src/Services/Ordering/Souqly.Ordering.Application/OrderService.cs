using System.Security.Cryptography;
using System.Text;
using Souqly.Chassis.Errors;
using Souqly.Chassis.Paging;
using Souqly.Chassis.Security;
using Souqly.Messaging;
using Souqly.Ordering.Domain;

namespace Souqly.Ordering.Application;

/// <summary>Cas d'usage de la commande (US-06 création, US-07 suivi, US-08 annulation, US-09 idempotence).</summary>
public sealed class OrderService(IOrderRepository orders, ICatalogPort catalog, IEventPublisher events, TimeProvider clock)
{
    /// <summary>
    /// Crée une commande en attente. Étapes :
    /// 1. même clé d'idempotence déjà vue → on renvoie la commande existante (aucun doublon) ;
    /// 2. instantané des prix lu dans le catalogue, HORS transaction (un appel réseau ne tient pas de verrou en base) ;
    /// 3. la commande et la clé sont enregistrées ensemble (un seul SaveChanges : tout ou rien).
    /// L'événement OrderPlaced est écrit dans l'outbox par ce même SaveChanges : il démarre la saga
    /// (réservation du stock, puis paiement) seulement si la commande est bien enregistrée.
    /// </summary>
    public async Task<PlaceOrderResult> PlaceAsync(CurrentUser customer, Guid idempotencyKey, PlaceOrderRequest request, CancellationToken ct)
    {
        var hash = Hash(request);
        var replay = await TryReplayAsync(customer, idempotencyKey, hash, ct);
        if (replay is not null)
        {
            return replay;
        }

        var wanted = request.Lines.ToList();
        if (wanted.GroupBy(l => l.ProductId).Any(g => g.Count() > 1))
        {
            throw new BusinessRuleException("duplicate-product", "Un même produit ne peut apparaître qu'une fois par commande.");
        }

        Dictionary<long, CatalogPrice> prices;
        try
        {
            prices = (await catalog.GetPricesAsync(wanted.Select(l => l.ProductId).ToList(), ct)).ToDictionary(p => p.ProductId);
        }
        catch (DependencyUnavailableException e)
        {
            OrderMetrics.RecordFailed(e.Dependency.ToLowerInvariant() + "_unavailable");   // compté par l'alerte US-20
            throw;
        }

        var unavailable = wanted.Where(l => !prices.TryGetValue(l.ProductId, out var p) || !p.Purchasable).Select(l => l.ProductId).ToList();
        if (unavailable.Count > 0)
        {
            throw new BusinessRuleException("product-unavailable", $"Produit(s) indisponible(s) à la vente : {string.Join(", ", unavailable)}.");
        }

        var now = clock.GetUtcNow();
        Order order;
        try
        {
            order = Order.Place(customer.Id,
                wanted.Select(l =>
                {
                    var p = prices[l.ProductId];
                    return new OrderLine(p.ProductId, p.VendorId, p.Name, p.Price, l.Quantity);
                }).ToList(),
                now);
        }
        catch (OrderRuleException e)
        {
            throw new BusinessRuleException(e.Code, e.Message);
        }

        orders.Add(order, new IdempotencyRecord(customer.Id, idempotencyKey, hash, order.Id, now));
        events.Publish(Topics.OrderingEvents, order.Id.ToString(), OrderEvents.Placed(order));
        try
        {
            await orders.SaveChangesAsync(ct);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // Double clic arrivé en même temps : l'autre requête a gagné, on renvoie sa commande.
            return await TryReplayAsync(customer, idempotencyKey, hash, ct)
                   ?? throw new ConflictException("Requête concurrente avec la même clé d'idempotence.");
        }

        OrderMetrics.RecordPlaced();
        return new PlaceOrderResult(OrderResponse.From(order), Replayed: false);
    }

    /// <summary>US-07 : suivi d'une commande. Celle d'un autre client renvoie 404 (RG-07).</summary>
    public async Task<OrderResponse> GetAsync(Guid id, CurrentUser customer, CancellationToken ct) =>
        OrderResponse.From(await FindOwnedAsync(id, customer, ct));

    public async Task<OrderPage> ListMineAsync(CurrentUser customer, int? page, int? size, CancellationToken ct)
    {
        var request = PageRequest.Of(page, size);
        var (items, total) = await orders.ListForCustomerAsync(customer.Id, request.Skip, request.Size, ct);
        var result = PageResponse<OrderResponse>.From(items.Select(OrderResponse.From).ToList(), request, total);
        return new OrderPage(result.Content, result.Page, result.Size, result.TotalElements, result.TotalPages);
    }

    /// <summary>US-08 : annulation par le client, possible jusqu'à l'expédition (RG-06).</summary>
    public async Task<OrderResponse> CancelAsync(Guid id, CurrentUser customer, CancellationToken ct)
    {
        var order = await FindOwnedAsync(id, customer, ct);
        try
        {
            order.Cancel("CUSTOMER_REQUEST", clock.GetUtcNow());
        }
        catch (IllegalOrderTransitionException e)
        {
            throw new ConflictException(e.Message);
        }

        // Compensations : Inventory rend le stock, Payment rembourse si le paiement était passé.
        events.Publish(Topics.OrderingEvents, order.Id.ToString(), new OrderCancelled(order.Id, order.CustomerId, "CUSTOMER_REQUEST"));
        await orders.SaveChangesAsync(ct);
        OrderMetrics.RecordOutcome(order, clock.GetUtcNow());
        return OrderResponse.From(order);
    }

    /// <summary>RG-05 : le vendeur voit les commandes qui contiennent ses produits, et seulement ses lignes.</summary>
    public async Task<PageResponse<VendorOrderResponse>> ListForVendorAsync(CurrentUser vendor, OrderStatus? status, int? page, int? size, CancellationToken ct)
    {
        var request = PageRequest.Of(page, size);
        var (items, total) = await orders.ListForVendorAsync(vendor.Id, status, request.Skip, request.Size, ct);
        var views = items.Select(o => ToVendorView(o, vendor.Id)).ToList();
        return PageResponse<VendorOrderResponse>.From(views, request, total);
    }

    /// <summary>
    /// US-17 : ventes du jour (commandes payées : CONFIRMED, SHIPPED ou DELIVERED, sur ses seules lignes) et commandes
    /// à préparer (CONFIRMED sans colis expédié par ce vendeur). Le « jour » est celui du Maroc.
    /// </summary>
    public async Task<VendorDashboardResponse> DashboardAsync(CurrentUser vendor, CancellationToken ct)
    {
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), MoroccoTime.Zone);
        var startOfDay = new DateTimeOffset(now.Date, now.Offset);
        var recent = await orders.ListForVendorDashboardAsync(vendor.Id, startOfDay, ct);

        var today = recent.Where(o => o.CreatedAt >= startOfDay).ToList();
        var paidToday = today.Where(o => o.Status is OrderStatus.Confirmed or OrderStatus.Shipped or OrderStatus.Delivered).ToList();
        var paidLines = paidToday.SelectMany(o => o.LinesOf(vendor.Id)).ToList();
        var toPrepare = recent
            .Where(o => o.Status == OrderStatus.Confirmed && o.Shipments.All(sh => sh.VendorId != vendor.Id))
            .OrderBy(o => o.CreatedAt)
            .ToList();

        return new VendorDashboardResponse(
            DateOnly.FromDateTime(now.Date),
            paidToday.Count,
            paidLines.Sum(l => l.Quantity),
            MoneyDto.Of(paidLines.Sum(l => l.LineTotal)),
            today.Count(o => o.Status is OrderStatus.Pending or OrderStatus.StockReserved),
            toPrepare.Count,
            toPrepare.Take(5).Select(o => ToVendorView(o, vendor.Id)).ToList());
    }

    /// <summary>US-13 : le vendeur expédie son colis ; la commande passe à SHIPPED quand tous les colis sont partis.</summary>
    public async Task<VendorOrderResponse> ShipAsync(Guid id, CurrentUser vendor, ShipRequest request, CancellationToken ct)
    {
        var order = await FindForVendorAsync(id, vendor, ct);
        var now = clock.GetUtcNow();
        bool allShipped;
        try
        {
            allShipped = order.ShipFor(vendor.Id, request.Carrier, request.TrackingNumber, now);
        }
        catch (IllegalOrderTransitionException e)
        {
            throw new ConflictException(e.Message);
        }
        catch (OrderRuleException e) when (e.Code == "already-shipped")
        {
            throw new ConflictException(e.Message);
        }
        catch (OrderRuleException e)
        {
            throw new BusinessRuleException(e.Code, e.Message);
        }

        events.Publish(Topics.OrderingEvents, order.Id.ToString(),
            new OrderShipped(order.Id, order.CustomerId, vendor.Id, request.Carrier, request.TrackingNumber, allShipped));
        await orders.SaveChangesAsync(ct);
        return ToVendorView(order, vendor.Id);
    }

    /// <summary>Livraison confirmée (par le vendeur, en attendant le webhook du transporteur).</summary>
    public async Task<VendorOrderResponse> DeliverAsync(Guid id, CurrentUser vendor, CancellationToken ct)
    {
        var order = await FindForVendorAsync(id, vendor, ct);
        bool allDelivered;
        try
        {
            if (order.Shipments.Any(sh => sh.VendorId == vendor.Id && sh.DeliveredAt is not null))
            {
                return ToVendorView(order, vendor.Id);   // déjà livré : réponse identique (idempotent)
            }

            allDelivered = order.MarkDelivered(vendor.Id, clock.GetUtcNow());
        }
        catch (OrderRuleException e)
        {
            throw new BusinessRuleException(e.Code, e.Message);
        }

        events.Publish(Topics.OrderingEvents, order.Id.ToString(), new OrderDelivered(order.Id, order.CustomerId, vendor.Id, allDelivered));
        await orders.SaveChangesAsync(ct);
        return ToVendorView(order, vendor.Id);
    }

    private async Task<Order> FindForVendorAsync(Guid id, CurrentUser vendor, CancellationToken ct)
    {
        var order = await orders.FindAsync(id, ct);
        if (order is null || !order.LinesOf(vendor.Id).Any())
        {
            throw new NotFoundException("Commande", id);   // pas de fuite d'existence (RG-05/07)
        }

        return order;
    }

    private static VendorOrderResponse ToVendorView(Order o, string vendorId)
    {
        var lines = o.LinesOf(vendorId).OrderBy(l => l.ProductId).ToList();
        var shipment = o.Shipments.FirstOrDefault(sh => sh.VendorId == vendorId);
        return new VendorOrderResponse(o.Id, OrderResponse.StatusName(o.Status), lines.Select(OrderLineResponse.From).ToList(),
            MoneyDto.Of(lines.Sum(l => l.LineTotal)), o.CreatedAt, shipment is null ? null : ShipmentResponse.From(shipment));
    }

    private async Task<PlaceOrderResult?> TryReplayAsync(CurrentUser customer, Guid key, string hash, CancellationToken ct)
    {
        var previous = await orders.FindIdempotencyAsync(customer.Id, key, ct);
        if (previous is null)
        {
            return null;
        }

        if (!string.Equals(previous.RequestHash, hash, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("idempotency-key-reused",
                "Cette clé Idempotency-Key a déjà servi pour une autre commande : générez une nouvelle clé.");
        }

        var order = await orders.FindAsync(previous.OrderId, ct) ?? throw new NotFoundException("Commande", previous.OrderId);
        return new PlaceOrderResult(OrderResponse.From(order), Replayed: true);
    }

    private async Task<Order> FindOwnedAsync(Guid id, CurrentUser customer, CancellationToken ct)
    {
        var order = await orders.FindAsync(id, ct);
        if (order is null || !order.IsOwnedBy(customer.Id))
        {
            throw new NotFoundException("Commande", id);
        }

        return order;
    }

    /// <summary>Empreinte du contenu de la requête (lignes triées) pour détecter une clé réutilisée avec un autre panier.</summary>
    private static string Hash(PlaceOrderRequest request)
    {
        var canonical = string.Join(';', request.Lines.OrderBy(l => l.ProductId).ThenBy(l => l.Quantity).Select(l => $"{l.ProductId}x{l.Quantity}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

/// <summary>Fuseau du Maroc (UTC+1, UTC+0 pendant le ramadan), avec repli sur UTC+1 si la base des fuseaux est absente.</summary>
internal static class MoroccoTime
{
    public static readonly TimeZoneInfo Zone = Find();

    private static TimeZoneInfo Find()
    {
        foreach (var id in new[] { "Africa/Casablanca", "Morocco Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Morocco", TimeSpan.FromHours(1), "Maroc", "Maroc");
    }
}
