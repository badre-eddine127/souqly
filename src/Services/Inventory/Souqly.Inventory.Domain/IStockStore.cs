namespace Souqly.Inventory.Domain;

/// <summary>Port de persistance du stock (implémenté avec EF Core).</summary>
public interface IStockStore
{
    Task<StockItem?> FindAsync(long productId, CancellationToken ct);

    void Add(StockItem item);

    /// <summary>
    /// Réserve <paramref name="quantity"/> unités si et seulement si elles sont disponibles,
    /// en une seule requête atomique (UPDATE … WHERE on_hand - reserved >= qty). Renvoie false sinon.
    /// </summary>
    Task<bool> TryReserveAsync(long productId, int quantity, CancellationToken ct);

    /// <summary>Rend des unités réservées (paiement refusé, expiration, annulation).</summary>
    Task ReleaseAsync(long productId, int quantity, CancellationToken ct);

    /// <summary>Consomme une réservation : la marchandise sort du stock physique (commande confirmée).</summary>
    Task CommitAsync(long productId, int quantity, CancellationToken ct);

    Task<IReadOnlyList<Reservation>> ReservationsForOrderAsync(Guid orderId, CancellationToken ct);

    Task<IReadOnlyList<Reservation>> DueReservationsAsync(DateTimeOffset now, int limit, CancellationToken ct);

    void AddReservation(Reservation reservation);

    /// <summary>Exécute le travail dans une transaction ; tout est annulé si <paramref name="work"/> renvoie false ou échoue.</summary>
    Task<bool> InTransactionAsync(Func<Task<bool>> work, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
