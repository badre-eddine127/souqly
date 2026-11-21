using Microsoft.EntityFrameworkCore;
using Npgsql;
using Souqly.Chassis.Errors;
using Souqly.Inventory.Application;
using Souqly.Inventory.Domain;

namespace Souqly.Inventory.Infrastructure.Persistence;

internal sealed class StockStore(InventoryDbContext db) : IStockStore
{
    public Task<StockItem?> FindAsync(long productId, CancellationToken ct) =>
        db.StockItems.FirstOrDefaultAsync(s => s.ProductId == productId, ct);

    public void Add(StockItem item) => db.StockItems.Add(item);

    public async Task<IReadOnlyList<StockItem>> ListLowForVendorAsync(string vendorId, int threshold, CancellationToken ct) =>
        await db.StockItems.AsNoTracking()
            .Where(s => s.VendorId == vendorId && s.OnHand - s.Reserved <= threshold)
            .OrderBy(s => s.OnHand - s.Reserved).ThenBy(s => s.ProductId)
            .Take(100)
            .ToListAsync(ct);

    /// <summary>
    /// ADR-007 : la condition est dans le WHERE, PostgreSQL l'évalue et écrit de façon atomique sous verrou de ligne.
    /// 500 requêtes concurrentes ne peuvent pas toutes réussir : seules celles qui trouvent du disponible modifient une ligne.
    /// </summary>
    public async Task<bool> TryReserveAsync(long productId, int quantity, CancellationToken ct)
    {
        var updated = await db.StockItems
            .Where(s => s.ProductId == productId && s.OnHand - s.Reserved >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, x => x.Reserved + quantity), ct);
        return updated == 1;
    }

    public async Task ReleaseAsync(long productId, int quantity, CancellationToken ct) =>
        await db.StockItems
            .Where(s => s.ProductId == productId && s.Reserved >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, x => x.Reserved - quantity), ct);

    public async Task CommitAsync(long productId, int quantity, CancellationToken ct) =>
        await db.StockItems
            .Where(s => s.ProductId == productId && s.Reserved >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Reserved, x => x.Reserved - quantity)
                .SetProperty(x => x.OnHand, x => x.OnHand - quantity), ct);

    public async Task<IReadOnlyList<Reservation>> ReservationsForOrderAsync(Guid orderId, CancellationToken ct) =>
        await db.Reservations.Where(r => r.OrderId == orderId).ToListAsync(ct);

    public async Task<IReadOnlyList<Reservation>> DueReservationsAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        await db.Reservations
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= now)
            .OrderBy(r => r.ExpiresAt)
            .Take(limit)
            .ToListAsync(ct);

    public void AddReservation(Reservation reservation) => db.Reservations.Add(reservation);

    public async Task RestockAsync(long productId, int quantity, CancellationToken ct) =>
        await db.StockItems
            .Where(s => s.ProductId == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.OnHand, x => x.OnHand + quantity), ct);

    public async Task<bool> InTransactionAsync(Func<Task<bool>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is { } outer)
        {
            return await InSavepointAsync(outer, work, ct);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (!await work())
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return false;
            }

            await transaction.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            throw new DuplicateReservationException(e);
        }
    }

    /// <summary>
    /// Appelé depuis un consommateur Kafka : la transaction de l'inbox est déjà ouverte. Un point de sauvegarde
    /// permet d'annuler uniquement la réservation (tout ou rien) sans perdre la trace « message traité ».
    /// </summary>
    private async Task<bool> InSavepointAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction outer, Func<Task<bool>> work, CancellationToken ct)
    {
        const string savepoint = "inventory_work";
        await outer.CreateSavepointAsync(savepoint, ct);
        try
        {
            if (!await work())
            {
                await outer.RollbackToSavepointAsync(savepoint, ct);
                db.ChangeTracker.Clear();
                return false;
            }

            await outer.ReleaseSavepointAsync(savepoint, ct);
            return true;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await outer.RollbackToSavepointAsync(savepoint, ct);
            db.ChangeTracker.Clear();
            throw new DuplicateReservationException(e);
        }
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Le stock a été modifié entre-temps ; rechargez-le et réessayez.");
        }
    }
}
