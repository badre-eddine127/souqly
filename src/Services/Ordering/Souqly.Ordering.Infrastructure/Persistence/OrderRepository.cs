using Microsoft.EntityFrameworkCore;
using Npgsql;
using Souqly.Chassis.Errors;
using Souqly.Ordering.Domain;

namespace Souqly.Ordering.Infrastructure.Persistence;

internal sealed class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    public Task<Order?> FindAsync(Guid id, CancellationToken ct) =>
        db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<IdempotencyRecord?> FindIdempotencyAsync(string customerId, Guid key, CancellationToken ct) =>
        db.IdempotencyKeys.AsNoTracking().FirstOrDefaultAsync(i => i.CustomerId == customerId && i.Key == key, ct);

    public async Task<(IReadOnlyList<Order> Items, long Total)> ListForCustomerAsync(string customerId, int skip, int take, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId);
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }

    public async Task<(IReadOnlyList<Order> Items, long Total)> ListForVendorAsync(string vendorId, OrderStatus? status, int skip, int take, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking().Where(o => o.Lines.Any(l => l.VendorId == vendorId));
        if (status is { } s)
        {
            query = query.Where(o => o.Status == s);
        }

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }

    public void Add(Order order, IdempotencyRecord idempotency)
    {
        db.Orders.Add(order);
        db.IdempotencyKeys.Add(idempotency);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, TableName: "idempotency_keys" })
        {
            db.ChangeTracker.Clear();
            throw new DuplicateIdempotencyKeyException(e);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("La commande a été modifiée entre-temps ; rechargez-la et réessayez.");
        }
    }
}
