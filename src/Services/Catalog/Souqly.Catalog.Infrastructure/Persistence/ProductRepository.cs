using Microsoft.EntityFrameworkCore;
using Souqly.Catalog.Domain.Categories;
using Souqly.Catalog.Domain.Products;
using Souqly.Chassis.Errors;

namespace Souqly.Catalog.Infrastructure.Persistence;

internal sealed class ProductRepository(CatalogDbContext db) : IProductRepository
{
    public Task<Product?> FindAsync(long id, CancellationToken ct) =>
        db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<(IReadOnlyList<Product> Items, long Total)> SearchAsync(ProductSearch search, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking();

        query = search.IncludeNonPublic
            ? query
            : query.Where(p => p.Status == ProductStatus.Active);

        if (search.VendorId is not null)
        {
            query = query.Where(p => p.VendorId == search.VendorId);
        }

        if (search.CategoryId is { } categoryId)
        {
            // Une catégorie parente inclut ses sous-catégories directes.
            query = query.Where(p => p.CategoryId == categoryId
                                     || db.Categories.Any(c => c.Id == p.CategoryId && c.ParentId == categoryId));
        }

        if (search.MinPrice is { } min)
        {
            query = query.Where(p => p.Price >= min);
        }

        if (search.MaxPrice is { } max)
        {
            query = query.Where(p => p.Price <= max);
        }

        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            // Recherche SQL simple (le niveau Pro la remplace par OpenSearch). Paramétrée : pas d'injection.
            var pattern = $"%{search.Text.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.Name, pattern) || EF.Functions.ILike(p.Description, pattern));
        }

        var total = await query.LongCountAsync(ct);

        // Tri toujours complété par l'identifiant : pagination stable.
        query = search.Sort switch
        {
            ProductSort.PriceAsc => query.OrderBy(p => p.Price).ThenBy(p => p.Id),
            ProductSort.PriceDesc => query.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
            ProductSort.NameAsc => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
        };

        var items = await query.Skip(search.Skip).Take(search.Take).ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<Product>> FindManyAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);

    public Task<bool> SkuExistsForVendorAsync(string vendorId, string sku, long? exceptProductId, CancellationToken ct) =>
        db.Products.AnyAsync(p => p.VendorId == vendorId && p.Sku == sku && p.Id != (exceptProductId ?? 0), ct);

    public Task<bool> CategoryExistsAsync(long categoryId, CancellationToken ct) =>
        db.Categories.AnyAsync(c => c.Id == categoryId, ct);

    public async Task<IReadOnlyList<Category>> ListCategoriesAsync(CancellationToken ct) =>
        await db.Categories.AsNoTracking().OrderBy(c => c.ParentId ?? c.Id).ThenBy(c => c.Id).ToListAsync(ct);

    public void Add(Product product) => db.Products.Add(product);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Le produit a été modifié entre-temps ; rechargez-le et réessayez.");
        }
    }

    public async Task InTransactionAsync(Func<Task> work, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await work();
            await transaction.CommitAsync(ct);
        });
    }
}
