using Microsoft.EntityFrameworkCore;
using Souqly.Catalog.Domain.Vendors;

namespace Souqly.Catalog.Infrastructure.Persistence;

internal sealed class VendorApplicationRepository(CatalogDbContext db) : IVendorApplicationRepository
{
    public Task<VendorApplication?> FindAsync(long id, CancellationToken ct) =>
        db.VendorApplications.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<VendorApplication?> FindByUserAsync(string userId, CancellationToken ct) =>
        db.VendorApplications.FirstOrDefaultAsync(a => a.UserId == userId, ct);

    public async Task<(IReadOnlyList<VendorApplication> Items, long Total)> ListAsync(
        VendorApplicationStatus? status, int skip, int take, CancellationToken ct)
    {
        var query = db.VendorApplications.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(a => a.Status == s);
        }

        var total = await query.LongCountAsync(ct);
        // Les plus anciennes d'abord : c'est une file d'attente.
        var items = await query.OrderBy(a => a.SubmittedAt).ThenBy(a => a.Id).Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<VendorApplication>> ListApprovedAsync(CancellationToken ct) =>
        await db.VendorApplications.AsNoTracking()
            .Where(a => a.Status == VendorApplicationStatus.Approved)
            .OrderBy(a => a.ShopName)
            .ToListAsync(ct);

    public void Add(VendorApplication application) => db.VendorApplications.Add(application);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
