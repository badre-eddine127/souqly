namespace Souqly.Inventory.Domain;

/// <summary>Port de persistance du stock (implémenté avec EF Core).</summary>
public interface IStockStore
{
    Task<StockItem?> FindAsync(long productId, CancellationToken ct);

    void Add(StockItem item);

    /// <summary>
    /// US-17 : stocks d'un vendeur dont le disponible est inférieur ou égal au seuil (ruptures et stocks bas).
    /// Sans seuil imposé, celui de chaque produit (US-12).
    /// </summary>
    Task<IReadOnlyList<StockItem>> ListLowForVendorAsync(string vendorId, int? threshold, CancellationToken ct);

    /// <summary>État actuel en base (non suivi), par exemple juste après une réservation atomique.</summary>
    Task<IReadOnlyList<StockItem>> ListAsync(IReadOnlyCollection<long> productIds, CancellationToken ct);

    /// <summary>
    /// Réserve <paramref name="quantity"/> unités si et seulement si elles sont disponibles,
    /// en une seule requête atomique (UPDATE … WHERE on_hand - reserved >= qty). Renvoie false sinon.
    /// </summary>
    Task<bool> TryReserveAsync(long productId, int quantity, CancellationToken ct);

    /// <summary>Rend des unités réservées (paiement refusé, expiration, annulation).</summary>
    Task ReleaseAsync(long productId, int quantity, CancellationToken ct);

    /// <summary>Consomme une réservation : la marchandise sort du stock physique (commande confirmée).</summary>
    Task CommitAsync(long productId, int quantity, CancellationToken ct);

    /// <summary>Remet en stock des unités déjà sorties (commande confirmée puis annulée avant expédition).</summary>
    Task RestockAsync(long productId, int quantity, CancellationToken ct);

    Task<IReadOnlyList<Reservation>> ReservationsForOrderAsync(Guid orderId, CancellationToken ct);

    Task<IReadOnlyList<Reservation>> DueReservationsAsync(DateTimeOffset now, int limit, CancellationToken ct);

    void AddReservation(Reservation reservation);

    /// <summary>Exécute le travail dans une transaction ; tout est annulé si <paramref name="work"/> renvoie false ou échoue.</summary>
    Task<bool> InTransactionAsync(Func<Task<bool>> work, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
