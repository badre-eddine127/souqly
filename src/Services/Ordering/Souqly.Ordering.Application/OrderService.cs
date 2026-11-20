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

        var prices = (await catalog.GetPricesAsync(wanted.Select(l => l.ProductId).ToList(), ct)).ToDictionary(p => p.ProductId);
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
        return OrderResponse.From(order);
    }

    /// <summary>RG-05 : le vendeur voit les commandes qui contiennent ses produits, et seulement ses lignes.</summary>
    public async Task<PageResponse<VendorOrderResponse>> ListForVendorAsync(CurrentUser vendor, OrderStatus? status, int? page, int? size, CancellationToken ct)
    {
        var request = PageRequest.Of(page, size);
        var (items, total) = await orders.ListForVendorAsync(vendor.Id, status, request.Skip, request.Size, ct);
        var views = items.Select(o =>
        {
            var lines = o.LinesOf(vendor.Id).OrderBy(l => l.ProductId).ToList();
            return new VendorOrderResponse(o.Id, OrderResponse.StatusName(o.Status), lines.Select(OrderLineResponse.From).ToList(),
                MoneyDto.Of(lines.Sum(l => l.LineTotal)), o.CreatedAt);
        }).ToList();
        return PageResponse<VendorOrderResponse>.From(views, request, total);
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
