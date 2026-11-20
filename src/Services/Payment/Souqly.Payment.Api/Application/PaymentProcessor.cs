using Microsoft.EntityFrameworkCore;
using Souqly.Messaging;
using Souqly.Payment.Api.Infrastructure;

namespace Souqly.Payment.Api.Application;

// Événements lus sur souqly.ordering.order-events.v1 (copies locales du contrat, ADR-009).
public sealed record PaymentRequested(Guid OrderId, string CustomerId, decimal Amount, string Currency);

public sealed record OrderCancelled(Guid OrderId, string CustomerId, string Reason);

// Événements publiés sur souqly.payment.payment-events.v1.
public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId, decimal Amount);

public sealed record PaymentFailed(Guid OrderId, string Reason);

public sealed record PaymentRefunded(Guid OrderId, string CustomerId, Guid PaymentId, decimal Amount, string Currency);

/// <summary>Règles du prestataire simulé.</summary>
public sealed class PaymentOptions
{
    /// <summary>Plafond de la carte simulée : au-delà, le paiement est refusé (sert à démontrer la compensation).</summary>
    public decimal DeclineAbove { get; set; } = 10_000m;
}

/// <summary>
/// Étape « paiement » de la saga. Idempotent par commande : si le paiement existe déjà (message relivré
/// après une panne), on republie le même résultat au lieu de débiter deux fois.
/// </summary>
public sealed class PaymentProcessor(PaymentDbContext db, IEventPublisher events, PaymentOptions options, TimeProvider clock) :
    IMessageHandler<PaymentRequested>,
    IMessageHandler<OrderCancelled>
{
    public async Task HandleAsync(PaymentRequested message, MessageContext context, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == message.OrderId, ct);
        if (payment is null)
        {
            var now = clock.GetUtcNow();
            payment = message.Amount > options.DeclineAbove
                ? Domain.Payment.Declined(message.OrderId, message.CustomerId, message.Amount, message.Currency, "LIMIT_EXCEEDED", now)
                : Domain.Payment.Captured(message.OrderId, message.CustomerId, message.Amount, message.Currency, now);
            db.Payments.Add(payment);
        }

        var key = message.OrderId.ToString();
        if (payment.Status == Domain.PaymentStatus.Declined)
        {
            events.Publish(Topics.PaymentEvents, key, new PaymentFailed(payment.OrderId, payment.FailureReason ?? "DECLINED"));
        }
        else
        {
            events.Publish(Topics.PaymentEvents, key, new PaymentSucceeded(payment.OrderId, payment.Id, payment.Amount));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Compensation : la commande est annulée ; si elle avait été payée, on rembourse.</summary>
    public async Task HandleAsync(OrderCancelled message, MessageContext context, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == message.OrderId, ct);
        if (payment is null || !payment.Refund(clock.GetUtcNow()))
        {
            return;
        }

        events.Publish(Topics.PaymentEvents, message.OrderId.ToString(),
            new PaymentRefunded(payment.OrderId, payment.CustomerId, payment.Id, payment.Amount, payment.Currency));
        await db.SaveChangesAsync(ct);
    }
}
