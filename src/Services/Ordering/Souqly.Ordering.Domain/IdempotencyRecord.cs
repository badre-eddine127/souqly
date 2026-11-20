namespace Souqly.Ordering.Domain;

/// <summary>
/// US-09 : mémorise la clé Idempotency-Key d'une création de commande. Un double clic ou un nouvel essai réseau
/// renvoie la même commande au lieu d'en créer une seconde. La même clé avec un autre contenu est refusée.
/// </summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
        CustomerId = RequestHash = string.Empty;
    }

    public IdempotencyRecord(string customerId, Guid key, string requestHash, Guid orderId, DateTimeOffset now)
    {
        CustomerId = customerId;
        Key = key;
        RequestHash = requestHash;
        OrderId = orderId;
        CreatedAt = now;
    }

    public string CustomerId { get; private set; }
    public Guid Key { get; private set; }
    public string RequestHash { get; private set; }
    public Guid OrderId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Port de persistance des commandes.</summary>
public interface IOrderRepository
{
    Task<Order?> FindAsync(Guid id, CancellationToken ct);

    Task<IdempotencyRecord?> FindIdempotencyAsync(string customerId, Guid key, CancellationToken ct);

    Task<(IReadOnlyList<Order> Items, long Total)> ListForCustomerAsync(string customerId, int skip, int take, CancellationToken ct);

    Task<(IReadOnlyList<Order> Items, long Total)> ListForVendorAsync(string vendorId, OrderStatus? status, int skip, int take, CancellationToken ct);

    void Add(Order order, IdempotencyRecord idempotency);

    /// <summary>Enregistre ; lève <see cref="DuplicateIdempotencyKeyException"/> si la même clé a été enregistrée en parallèle.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}

public sealed class DuplicateIdempotencyKeyException(Exception inner) : Exception("Clé d'idempotence déjà utilisée.", inner);
