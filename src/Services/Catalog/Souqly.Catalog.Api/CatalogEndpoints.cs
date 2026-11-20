using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Souqly.Catalog.Application.Products;
using Souqly.Chassis;
using Souqly.Chassis.Errors;
using Souqly.Chassis.Paging;
using Souqly.Chassis.Security;
using Souqly.Chassis.Validation;

namespace Souqly.Catalog.Api;

/// <summary>Endpoints REST du catalogue. Minces : ils délèguent à <see cref="ProductService"/>.</summary>
internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/v1/products").WithTags("Produits");

        products.MapGet("/", SearchAsync)
            .WithSummary("Lister le catalogue public (paginé, filtrable, triable)")
            .AllowAnonymous();

        products.MapGet("/{id:long}", GetAsync)
            .WithName("GetProduct")
            .WithSummary("Fiche produit")
            .AllowAnonymous();

        products.MapPost("/", CreateAsync)
            .WithSummary("Créer un produit (vendeur)")
            .RequireAuthorization(Policies.Vendor)
            .Validate<ProductRequest>();

        products.MapPut("/{id:long}", UpdateAsync)
            .WithSummary("Modifier un de ses produits (vendeur propriétaire)")
            .RequireAuthorization(Policies.Vendor)
            .Validate<ProductRequest>();

        products.MapDelete("/{id:long}", ArchiveAsync)
            .WithSummary("Archiver un de ses produits (vendeur propriétaire)")
            .RequireAuthorization(Policies.Vendor);

        app.MapGet("/api/v1/vendor/products", ListMineAsync)
            .WithTags("Espace vendeur")
            .WithSummary("Mes produits, y compris brouillons et archivés")
            .RequireAuthorization(Policies.Vendor);

        app.MapGet("/api/v1/categories", async (ProductService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListCategoriesAsync(ct)))
            .WithTags("Catégories")
            .WithSummary("Arbre des catégories")
            .AllowAnonymous();

        // Interne : jamais routé par la passerelle, réservé aux clients techniques (Ordering).
        app.MapGet("/internal/v1/products/prices", PricesAsync)
            .WithTags("Interne")
            .WithSummary("Instantané de prix pour la commande (RG-02)")
            .RequireAuthorization(Policies.Internal);

        return app;
    }

    private static async Task<Ok<PageResponse<ProductResponse>>> SearchAsync(
        [AsParameters] ProductQuery query, ProductService service, CancellationToken ct) =>
        TypedResults.Ok(await service.SearchAsync(query, ct));

    private static async Task<Ok<ProductResponse>> GetAsync(long id, HttpContext http, ProductService service, CancellationToken ct)
    {
        var caller = http.User.Identity?.IsAuthenticated == true ? http.CurrentUser() : null;
        return TypedResults.Ok(await service.GetAsync(id, caller, ct));
    }

    private static async Task<CreatedAtRoute<ProductResponse>> CreateAsync(
        ProductRequest request, HttpContext http, ProductService service, CancellationToken ct)
    {
        var created = await service.CreateAsync(request, http.CurrentUser(), ct);
        return TypedResults.CreatedAtRoute(created, "GetProduct", new { id = created.Id });
    }

    private static async Task<Ok<ProductResponse>> UpdateAsync(
        long id, ProductRequest request, HttpContext http, ProductService service, CancellationToken ct) =>
        TypedResults.Ok(await service.UpdateAsync(id, request, http.CurrentUser(), ct));

    private static async Task<NoContent> ArchiveAsync(long id, HttpContext http, ProductService service, CancellationToken ct)
    {
        await service.ArchiveAsync(id, http.CurrentUser(), ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<PageResponse<ProductResponse>>> ListMineAsync(
        int? page, int? size, HttpContext http, ProductService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListForVendorAsync(http.CurrentUser(), page, size, ct));

    private static async Task<Ok<IReadOnlyList<PriceSnapshot>>> PricesAsync(
        [FromQuery] string ids, ProductService service, CancellationToken ct)
    {
        var parsed = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var id) ? id : throw new BusinessRuleException("invalid-ids", $"Identifiant invalide : {s}"))
            .Distinct()
            .ToList();
        return TypedResults.Ok(await service.PriceSnapshotAsync(parsed, ct));
    }
}
