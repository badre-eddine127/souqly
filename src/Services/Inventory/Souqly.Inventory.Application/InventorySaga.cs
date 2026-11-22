using Souqly.Inventory.Domain;
using Souqly.Messaging;

namespace Souqly.Inventory.Application;

// Événements lus sur souqly.ordering.order-events.v1 (copies locales du contrat, ADR-009).

public sealed record OrderLineEvent(long ProductId, string VendorId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record OrderPlaced(Guid OrderId, string CustomerId, IReadOnlyList<OrderLineEvent> Lines, decimal Total, string Currency);

public sealed record OrderConfirmed(Guid OrderId, string CustomerId, Guid PaymentId, IReadOnlyList<OrderLineEvent> Lines, decimal Total, string Currency);

public sealed record OrderRejected(Guid OrderId, string CustomerId, string Reason);

public sealed record OrderCancelled(Guid OrderId, string CustomerId, string Reason);

// Événements publiés sur souqly.inventory.stock-events.v1.

public sealed record StockReserved(Guid OrderId);

public sealed record StockReservationFailed(Guid OrderId, long ProductId);

/// <summary>
/// US-12 : la disponibilité d'un produit vient de passer au seuil d'alerte du vendeur ou en dessous (0 = rupture).
/// Clé : identifiant du produit. ProductName n'est connu que si l'alerte vient d'une commande.
/// </summary>
public sealed record LowStock(long ProductId, string VendorId, string? ProductName, int Available, int Threshold);

/// <summary>
/// Participation d'Inventory à la saga :
/// OrderPlaced → réservation tout ou rien (ADR-007) puis réponse ; OrderConfirmed → sortie du stock ;
/// OrderRejected → libération ; OrderCancelled → libération ou remise en stock.
/// Chaque opération est idempotente, en plus de l'inbox (double sécurité).
/// </summary>
public sealed class InventorySaga(ReservationService reservations, IStockStore store, IEventPublisher events) :
    IMessageHandler<OrderPlaced>,
    IMessageHandler<OrderConfirmed>,
    IMessageHandler<OrderRejected>,
    IMessageHandler<OrderCancelled>
{
    public async Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
    {
        var result = await reservations.ReserveAsync(
            new ReserveStockRequest(message.OrderId, message.Lines.Select(l => new ReserveLine(l.ProductId, l.Quantity)).ToList()), ct);

        var key = message.OrderId.ToString();
        if (result.Succeeded)
        {
            events.Publish(Topics.InventoryEvents, key, new StockReserved(message.OrderId));
            await AlertLowStockAsync(message, ct);
        }
        else
        {
            events.Publish(Topics.InventoryEvents, key, new StockReservationFailed(message.OrderId, result.FailedProductId ?? 0));
        }
    }

    /// <summary>
    /// US-12 : après la réservation, alerte pour chaque produit qui vient de franchir son seuil. L'état est relu en base
    /// (la réservation est un UPDATE atomique) ; « avant » = après + quantité réservée par cette commande.
    /// </summary>
    private async Task AlertLowStockAsync(OrderPlaced message, CancellationToken ct)
    {
        var lines = message.Lines.GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => (Quantity: g.Sum(l => l.Quantity), Name: g.First().ProductName));
        foreach (var item in await store.ListAsync(lines.Keys, ct))
        {
            var (quantity, name) = lines[item.ProductId];
            if (item.CrossedLowStock(item.Available + quantity))
            {
                events.Publish(Topics.InventoryEvents, item.ProductId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new LowStock(item.ProductId, item.VendorId, name, item.Available, item.LowStockThreshold));
            }
        }
    }

    public Task HandleAsync(OrderConfirmed message, MessageContext context, CancellationToken ct) =>
        reservations.CommitAsync(message.OrderId, ct);

    public Task HandleAsync(OrderRejected message, MessageContext context, CancellationToken ct) =>
        reservations.ReleaseAsync(message.OrderId, ct);

    public Task HandleAsync(OrderCancelled message, MessageContext context, CancellationToken ct) =>
        reservations.CancelAsync(message.OrderId, ct);
}
