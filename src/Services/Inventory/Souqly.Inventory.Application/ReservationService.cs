using Souqly.Inventory.Domain;

namespace Souqly.Inventory.Application;

/// <summary>
/// Réservations de stock pour la saga de commande (US-11, RG-01, RG-03).
/// Tout ou rien : si une ligne n'est pas disponible, aucune n'est réservée.
/// Idempotent par commande : rejouer ReserveStock pour la même commande ne réserve pas deux fois.
/// </summary>
public sealed class ReservationService(IStockStore store, TimeProvider clock)
{
    public async Task<ReservationResult> ReserveAsync(ReserveStockRequest request, CancellationToken ct)
    {
        var already = await store.ReservationsForOrderAsync(request.OrderId, ct);
        if (already.Count > 0)
        {
            return new ReservationResult(request.OrderId, ReservationResult.Reserved, null);
        }

        // Regroupe les lignes d'un même produit et trie par identifiant : deux commandes concurrentes
        // verrouillent les lignes dans le même ordre, donc pas d'interblocage.
        var lines = request.Lines
            .GroupBy(l => l.ProductId)
            .Select(g => new ReserveLine(g.Key, g.Sum(l => l.Quantity)))
            .OrderBy(l => l.ProductId)
            .ToList();

        long? failed = null;
        var now = clock.GetUtcNow();
        bool reserved;
        try
        {
            reserved = await store.InTransactionAsync(async () =>
            {
                foreach (var line in lines)
                {
                    if (!await store.TryReserveAsync(line.ProductId, line.Quantity, ct))
                    {
                        failed = line.ProductId;
                        return false;   // la transaction est annulée : rien n'est réservé
                    }

                    store.AddReservation(Reservation.Active(request.OrderId, line.ProductId, line.Quantity, now));
                }

                await store.SaveChangesAsync(ct);
                return true;
            }, ct);
        }
        catch (DuplicateReservationException)
        {
            // Même commande traitée en parallèle (message Kafka rejoué) : l'autre traitement a gagné.
            return new ReservationResult(request.OrderId, ReservationResult.Reserved, null);
        }

        return reserved
            ? new ReservationResult(request.OrderId, ReservationResult.Reserved, null)
            : new ReservationResult(request.OrderId, ReservationResult.OutOfStock, failed);
    }

    /// <summary>Compensation : paiement refusé ou commande annulée. Sans effet si déjà libérée.</summary>
    public Task ReleaseAsync(Guid orderId, CancellationToken ct) =>
        CloseAsync(orderId, async r =>
        {
            await store.ReleaseAsync(r.ProductId, r.Quantity, ct);
            r.MarkReleased();
        }, ct);

    /// <summary>Commande confirmée : les unités réservées sortent du stock physique.</summary>
    public Task CommitAsync(Guid orderId, CancellationToken ct) =>
        CloseAsync(orderId, async r =>
        {
            await store.CommitAsync(r.ProductId, r.Quantity, ct);
            r.MarkCommitted();
        }, ct);

    /// <summary>RG-03 : rend le stock des réservations échues (appelé toutes les 30 s). Renvoie le nombre expiré.</summary>
    public async Task<int> ExpireDueAsync(int batchSize, CancellationToken ct)
    {
        var due = await store.DueReservationsAsync(clock.GetUtcNow(), batchSize, ct);
        if (due.Count == 0)
        {
            return 0;
        }

        await store.InTransactionAsync(async () =>
        {
            foreach (var reservation in due)
            {
                await store.ReleaseAsync(reservation.ProductId, reservation.Quantity, ct);
                reservation.MarkExpired();
            }

            await store.SaveChangesAsync(ct);
            return true;
        }, ct);
        return due.Count;
    }

    private async Task CloseAsync(Guid orderId, Func<Reservation, Task> close, CancellationToken ct)
    {
        var active = (await store.ReservationsForOrderAsync(orderId, ct)).Where(r => r.IsActive).ToList();
        if (active.Count == 0)
        {
            return;   // idempotent : message rejoué ou réservation déjà expirée
        }

        await store.InTransactionAsync(async () =>
        {
            foreach (var reservation in active.OrderBy(r => r.ProductId))
            {
                await close(reservation);
            }

            await store.SaveChangesAsync(ct);
            return true;
        }, ct);
    }
}

/// <summary>Levée par le stockage quand une réservation existe déjà pour (commande, produit).</summary>
public sealed class DuplicateReservationException(Exception inner) : Exception("Réservation déjà enregistrée pour cette commande.", inner);
