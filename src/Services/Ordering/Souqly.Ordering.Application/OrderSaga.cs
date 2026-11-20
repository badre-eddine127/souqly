using Microsoft.Extensions.Logging;
using Souqly.Messaging;
using Souqly.Ordering.Domain;

namespace Souqly.Ordering.Application;

/// <summary>
/// Saga de commande, orchestrée par Ordering (ADR-005). Chaque réponse fait avancer la machine à états de la
/// commande et publie l'étape suivante dans l'outbox, dans la même transaction que l'inbox :
///
///   PENDING ──StockReserved──▶ STOCK_RESERVED ──PaymentSucceeded──▶ CONFIRMED
///      │                             │
///      └─StockReservationFailed─▶ REJECTED ◀──PaymentFailed──┘   (compensation : Inventory libère le stock)
///
/// Une réponse qui arrive pour une commande déjà annulée est ignorée : l'annulation a déjà déclenché ses compensations
/// et, Kafka gardant l'ordre par commande, Inventory et Payment les appliqueront après leur propre étape.
/// </summary>
public sealed partial class OrderSaga(IOrderRepository orders, IEventPublisher events, TimeProvider clock, ILogger<OrderSaga> logger) :
    IMessageHandler<StockReserved>,
    IMessageHandler<StockReservationFailed>,
    IMessageHandler<PaymentSucceeded>,
    IMessageHandler<PaymentFailed>
{
    public Task HandleAsync(StockReserved message, MessageContext context, CancellationToken ct) =>
        AdvanceAsync(message.OrderId, OrderStatus.Pending, order =>
        {
            order.MarkStockReserved(clock.GetUtcNow());
            events.Publish(Topics.OrderingEvents, order.Id.ToString(), new PaymentRequested(order.Id, order.CustomerId, order.Total, Order.Currency));
        }, ct);

    public Task HandleAsync(StockReservationFailed message, MessageContext context, CancellationToken ct) =>
        AdvanceAsync(message.OrderId, OrderStatus.Pending, order => Reject(order, "OUT_OF_STOCK"), ct);

    public Task HandleAsync(PaymentSucceeded message, MessageContext context, CancellationToken ct) =>
        AdvanceAsync(message.OrderId, OrderStatus.StockReserved, order =>
        {
            order.Confirm(message.PaymentId, clock.GetUtcNow());
            events.Publish(Topics.OrderingEvents, order.Id.ToString(),
                new OrderConfirmed(order.Id, order.CustomerId, message.PaymentId, OrderEvents.Lines(order), order.Total, Order.Currency));
        }, ct);

    public Task HandleAsync(PaymentFailed message, MessageContext context, CancellationToken ct) =>
        AdvanceAsync(message.OrderId, OrderStatus.StockReserved, order => Reject(order, "PAYMENT_DECLINED"), ct);

    private void Reject(Order order, string reason)
    {
        order.Reject(reason, clock.GetUtcNow());
        events.Publish(Topics.OrderingEvents, order.Id.ToString(), new OrderRejected(order.Id, order.CustomerId, reason));
    }

    private async Task AdvanceAsync(Guid orderId, OrderStatus expected, Action<Order> step, CancellationToken ct)
    {
        var order = await orders.FindAsync(orderId, ct);
        if (order is null)
        {
            LogUnknownOrder(logger, orderId);
            return;
        }

        if (order.Status != expected)
        {
            // Réponse tardive (commande annulée entre-temps) : rien à faire, voir le commentaire de la classe.
            LogIgnored(logger, orderId, order.Status);
            return;
        }

        step(order);
        await orders.SaveChangesAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Réponse de saga pour une commande inconnue {OrderId}")]
    private static partial void LogUnknownOrder(ILogger logger, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Réponse de saga ignorée : la commande {OrderId} est déjà {Status}")]
    private static partial void LogIgnored(ILogger logger, Guid orderId, OrderStatus status);
}
