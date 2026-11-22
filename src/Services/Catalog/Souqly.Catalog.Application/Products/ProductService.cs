using Souqly.Catalog.Domain.Products;
using Souqly.Chassis.Errors;
using Souqly.Chassis.Paging;
using Souqly.Chassis.Security;
using Souqly.Messaging;

namespace Souqly.Catalog.Application.Products;

/// <summary>Cas d'usage du catalogue (US-01, US-02, US-03).</summary>
public sealed class ProductService(IProductRepository products, IEventPublisher events, TimeProvider clock)
{
    /// <summary>US-01 : catalogue public paginé, filtré par catégorie, prix et texte, trié.</summary>
    public async Task<PageResponse<ProductResponse>> SearchAsync(ProductQuery query, CancellationToken ct)
    {
        var page = PageRequest.Of(query.Page, query.Size);
        var search = new ProductSearch(query.Category, query.MinPrice, query.MaxPrice, query.Q, ParseSort(query.Sort),
            page.Skip, page.Size);
        var (items, total) = await products.SearchAsync(search, ct);
        return PageResponse<ProductResponse>.From(items.Select(ProductResponse.From).ToList(), page, total);
    }

    /// <summary>Espace vendeur : tous ses produits, y compris brouillons et archivés.</summary>
    public async Task<PageResponse<ProductResponse>> ListForVendorAsync(CurrentUser vendor, int? pageNumber, int? size, CancellationToken ct)
    {
        var page = PageRequest.Of(pageNumber, size);
        var search = new ProductSearch(null, null, null, null, ProductSort.Newest, page.Skip, page.Size,
            VendorId: vendor.Id, IncludeNonPublic: true);
        var (items, total) = await products.SearchAsync(search, ct);
        return PageResponse<ProductResponse>.From(items.Select(ProductResponse.From).ToList(), page, total);
    }

    /// <summary>US-02 : fiche produit. Un brouillon n'est visible que de son vendeur (404 pour les autres).</summary>
    public async Task<ProductResponse> GetAsync(long id, CurrentUser? caller, CancellationToken ct)
    {
        var product = await products.FindAsync(id, ct);
        if (product is null || (!product.IsPubliclyVisible && (caller is null || !product.IsOwnedBy(caller.Id))))
        {
            throw new NotFoundException("Produit", id);
        }

        return ProductResponse.From(product);
    }

    /// <summary>US-03 : création par un vendeur.</summary>
    public async Task<ProductResponse> CreateAsync(ProductRequest request, CurrentUser vendor, CancellationToken ct)
    {
        await EnsureCategoryExistsAsync(request.CategoryId, ct);
        if (await products.SkuExistsForVendorAsync(vendor.Id, request.Sku.Trim().ToUpperInvariant(), null, ct))
        {
            throw new ConflictException($"Le SKU {request.Sku} existe déjà dans votre catalogue.");
        }

        var now = clock.GetUtcNow();
        var product = Translate(() => Product.Create(vendor.Id, request.CategoryId, request.Sku, request.Name,
            request.Description ?? string.Empty, request.Price.ToDecimal(), now));
        if (request.Publish)
        {
            product.Publish(now);
        }

        // L'identifiant n'existe qu'après l'insertion : produit puis événement, dans une même transaction (outbox).
        await products.InTransactionAsync(async () =>
        {
            products.Add(product);
            await products.SaveChangesAsync(ct);
            events.Created(product);
            await products.SaveChangesAsync(ct);
        }, ct);
        return ProductResponse.From(product);
    }

    /// <summary>US-03 : modification par le vendeur propriétaire uniquement (RG-07).</summary>
    public async Task<ProductResponse> UpdateAsync(long id, ProductRequest request, CurrentUser vendor, CancellationToken ct)
    {
        var product = await FindOwnedAsync(id, vendor, ct);
        await EnsureCategoryExistsAsync(request.CategoryId, ct);
        if (await products.SkuExistsForVendorAsync(vendor.Id, request.Sku.Trim().ToUpperInvariant(), id, ct))
        {
            throw new ConflictException($"Le SKU {request.Sku} existe déjà dans votre catalogue.");
        }

        var now = clock.GetUtcNow();
        Translate(() =>
        {
            product.Update(request.CategoryId, request.Sku, request.Name, request.Description ?? string.Empty,
                request.Price.ToDecimal(), now);
            if (request.Publish)
            {
                product.Publish(now);
            }

            return product;
        });
        events.Updated(product);   // enregistré dans l'outbox par le même SaveChanges que le produit
        await products.SaveChangesAsync(ct);
        return ProductResponse.From(product);
    }

    /// <summary>US-03 : archivage (suppression logique) par le vendeur propriétaire.</summary>
    public async Task ArchiveAsync(long id, CurrentUser vendor, CancellationToken ct)
    {
        var product = await FindOwnedAsync(id, vendor, ct);
        product.Archive(clock.GetUtcNow());
        events.Archived(product);
        await products.SaveChangesAsync(ct);
    }

    /// <summary>Endpoint interne pour Ordering : prix figés au moment de la commande (RG-02).</summary>
    public async Task<IReadOnlyList<PriceSnapshot>> PriceSnapshotAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        if (ids.Count is 0 or > 20)
        {
            throw new BusinessRuleException("invalid-ids", "Entre 1 et 20 identifiants de produit sont attendus.");
        }

        var found = await products.FindManyAsync(ids, ct);
        return found.Select(p => new PriceSnapshot(p.Id, p.VendorId, p.Name, MoneyDto.Of(p.Price), p.IsPubliclyVisible))
            .ToList();
    }

    public async Task<IReadOnlyList<CategoryResponse>> ListCategoriesAsync(CancellationToken ct) =>
        (await products.ListCategoriesAsync(ct)).Select(c => new CategoryResponse(c.Id, c.Slug, c.Name, c.ParentId)).ToList();

    private async Task<Product> FindOwnedAsync(long id, CurrentUser vendor, CancellationToken ct)
    {
        var product = await products.FindAsync(id, ct);
        // Contrôle d'accès objet : un autre vendeur reçoit 404, pas 403, pour ne pas révéler l'existence du produit.
        if (product is null || !product.IsOwnedBy(vendor.Id))
        {
            throw new NotFoundException("Produit", id);
        }

        return product;
    }

    private async Task EnsureCategoryExistsAsync(long categoryId, CancellationToken ct)
    {
        if (!await products.CategoryExistsAsync(categoryId, ct))
        {
            throw new BusinessRuleException("unknown-category", $"La catégorie {categoryId} n'existe pas.");
        }
    }

    private static T Translate<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (ProductRuleException e)
        {
            throw new BusinessRuleException(e.Code, e.Message);
        }
        catch (ProductStateException e)
        {
            throw new ConflictException(e.Message);
        }
    }

    internal static ProductSort ParseSort(string? sort) => sort?.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
    {
        null or "" or "createdat,desc" => ProductSort.Newest,
        "price" or "price,asc" => ProductSort.PriceAsc,
        "price,desc" => ProductSort.PriceDesc,
        "name" or "name,asc" => ProductSort.NameAsc,
        _ => throw new BusinessRuleException("invalid-sort", "Tri accepté : price,asc · price,desc · name,asc · createdAt,desc."),
    };
}
