namespace Souqly.Payment.Api.Domain;

/// <summary>
/// Paiement d'une commande (simulé : aucun prestataire réel, aucune donnée de carte). Un seul paiement par commande.
/// </summary>
public sealed class Payment
{
    private Payment()
    {
        CustomerId = string.Empty;
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string CustomerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static Payment Captured(Guid orderId, string customerId, decimal amount, string currency, DateTimeOffset now) =>
        Create(orderId, customerId, amount, currency, PaymentStatus.Captured, null, now);

    public static Payment Declined(Guid orderId, string customerId, decimal amount, string currency, string reason, DateTimeOffset now) =>
        Create(orderId, customerId, amount, currency, PaymentStatus.Declined, reason, now);

    /// <summary>Remboursement intégral d'un paiement encaissé (commande annulée).</summary>
    public bool Refund(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Captured)
        {
            return false;   // refusé ou déjà remboursé : rien à rembourser
        }

        Status = PaymentStatus.Refunded;
        UpdatedAt = now;
        return true;
    }

    private static Payment Create(Guid orderId, string customerId, decimal amount, string currency, PaymentStatus status, string? reason, DateTimeOffset now)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Le montant doit être positif.");
        }

        return new Payment
        {
            Id = Guid.CreateVersion7(now),
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Currency = currency,
            Status = status,
            FailureReason = reason,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}

public enum PaymentStatus
{
    Captured,
    Declined,
    Refunded,
}
