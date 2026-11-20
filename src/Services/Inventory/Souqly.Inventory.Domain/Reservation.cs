namespace Souqly.Inventory.Domain;

/// <summary>Réservation de stock pour une ligne de commande, valable 15 minutes (RG-03).</summary>
public sealed class Reservation
{
    public static readonly TimeSpan Hold = TimeSpan.FromMinutes(15);

    private Reservation()
    {
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public long ProductId { get; private set; }
    public int Quantity { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public static Reservation Active(Guid orderId, long productId, int quantity, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        OrderId = orderId,
        ProductId = productId,
        Quantity = quantity,
        Status = ReservationStatus.Active,
        CreatedAt = now,
        ExpiresAt = now + Hold,
    };

    public bool IsActive => Status == ReservationStatus.Active;

    public void MarkReleased() => Close(ReservationStatus.Released);

    public void MarkCommitted() => Close(ReservationStatus.Committed);

    public void MarkExpired() => Close(ReservationStatus.Expired);

    private void Close(ReservationStatus next)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException($"La réservation {Id} est déjà {Status}.");
        }

        Status = next;
    }
}

public enum ReservationStatus
{
    Active,
    Committed,
    Released,
    Expired,
}
