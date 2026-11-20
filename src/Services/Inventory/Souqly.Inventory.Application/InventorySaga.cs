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
/// Participation d'Inventory à la saga :
/// OrderPlaced → réservation tout ou rien (ADR-007) puis réponse ; OrderConfirmed → sortie du stock ;
/// OrderRejected → libération ; OrderCancelled → libération ou remise en stock.
/// Chaque opération est idempotente, en plus de l'inbox (double sécurité).
/// </summary>
public sealed class InventorySaga(ReservationService reservations, IEventPublisher events) :
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
        }
        else
        {
            events.Publish(Topics.InventoryEvents, key, new StockReservationFailed(message.OrderId, result.FailedProductId ?? 0));
        }
    }

    public Task HandleAsync(OrderConfirmed message, MessageContext context, CancellationToken ct) =>
        reservations.CommitAsync(message.OrderId, ct);

    public Task HandleAsync(OrderRejected message, MessageContext context, CancellationToken ct) =>
        reservations.ReleaseAsync(message.OrderId, ct);

    public Task HandleAsync(OrderCancelled message, MessageContext context, CancellationToken ct) =>
        reservations.CancelAsync(message.OrderId, ct);
}
