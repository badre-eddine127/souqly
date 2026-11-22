using Souqly.Catalog.Domain.Products;
using Souqly.Messaging;

namespace Souqly.Catalog.Application.Products;

/// <summary>
/// Événements du cycle de vie d'un produit (topic souqly.catalog.product-events.v1, clé = identifiant du produit).
/// Ils transportent l'état complet du produit : un consommateur (Search) tient son modèle de lecture à jour
/// sans rappeler Catalog. Les noms suivent le dossier de projet : ProductCreated, ProductUpdated, ProductArchived.
/// </summary>
public sealed record ProductCreated(ProductSnapshot Product);

public sealed record ProductUpdated(ProductSnapshot Product);

public sealed record ProductArchived(ProductSnapshot Product);

/// <summary>État publié d'un produit. Status : DRAFT, ACTIVE ou ARCHIVED.</summary>
public sealed record ProductSnapshot(
    long ProductId,
    string VendorId,
    long CategoryId,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string Status,
    DateTimeOffset UpdatedAt)
{
    public static ProductSnapshot From(Product p) => new(
        p.Id, p.VendorId, p.CategoryId, p.Sku, p.Name, p.Description, p.Price, "MAD",
        p.Status.ToString().ToUpperInvariant(), p.UpdatedAt);
}

internal static class ProductEventPublishing
{
    public static void Created(this IEventPublisher events, Product p) =>
        events.Publish(Topics.CatalogEvents, p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), new ProductCreated(ProductSnapshot.From(p)));

    public static void Updated(this IEventPublisher events, Product p) =>
        events.Publish(Topics.CatalogEvents, p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), new ProductUpdated(ProductSnapshot.From(p)));

    public static void Archived(this IEventPublisher events, Product p) =>
        events.Publish(Topics.CatalogEvents, p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), new ProductArchived(ProductSnapshot.From(p)));
}
