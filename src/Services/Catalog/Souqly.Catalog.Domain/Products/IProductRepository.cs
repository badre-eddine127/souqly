using Souqly.Catalog.Domain.Categories;

namespace Souqly.Catalog.Domain.Products;

/// <summary>Port de persistance du catalogue (implémenté avec EF Core dans Infrastructure).</summary>
public interface IProductRepository
{
    Task<Product?> FindAsync(long id, CancellationToken ct);

    Task<(IReadOnlyList<Product> Items, long Total)> SearchAsync(ProductSearch search, CancellationToken ct);

    Task<IReadOnlyList<Product>> FindManyAsync(IReadOnlyCollection<long> ids, CancellationToken ct);

    Task<bool> SkuExistsForVendorAsync(string vendorId, string sku, long? exceptProductId, CancellationToken ct);

    Task<bool> CategoryExistsAsync(long categoryId, CancellationToken ct);

    Task<IReadOnlyList<Category>> ListCategoriesAsync(CancellationToken ct);

    void Add(Product product);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Critères de recherche du catalogue public ou de l'espace vendeur.</summary>
public sealed record ProductSearch(
    long? CategoryId,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? Text,
    ProductSort Sort,
    int Skip,
    int Take,
    string? VendorId = null,
    bool IncludeNonPublic = false);

public enum ProductSort
{
    Newest,
    PriceAsc,
    PriceDesc,
    NameAsc,
}
