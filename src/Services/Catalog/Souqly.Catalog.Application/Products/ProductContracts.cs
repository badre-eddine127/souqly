using System.Globalization;
using Souqly.Catalog.Domain.Products;

namespace Souqly.Catalog.Application.Products;

/// <summary>Montant tel qu'il circule dans l'API : chaîne décimale + devise (jamais un nombre flottant).</summary>
public sealed record MoneyDto(string Amount, string Currency)
{
    public const string Mad = "MAD";

    public static MoneyDto Of(decimal amount) => new(amount.ToString("0.00", CultureInfo.InvariantCulture), Mad);

    public decimal ToDecimal() => decimal.Parse(Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}

/// <summary>Corps de POST /api/v1/products et PUT /api/v1/products/{id}.</summary>
public sealed record ProductRequest(
    long CategoryId,
    string Sku,
    string Name,
    string? Description,
    MoneyDto Price,
    bool Publish = false);

public sealed record ProductResponse(
    long Id,
    string VendorId,
    long CategoryId,
    string Sku,
    string Name,
    string Description,
    MoneyDto Price,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ProductResponse From(Product p) => new(
        p.Id, p.VendorId, p.CategoryId, p.Sku, p.Name, p.Description, MoneyDto.Of(p.Price),
        p.Status.ToString().ToUpperInvariant(), p.CreatedAt, p.UpdatedAt);
}

/// <summary>Instantané de prix pour Ordering (RG-02) : prix et disponibilité commerciale au moment de la commande.</summary>
public sealed record PriceSnapshot(long ProductId, string VendorId, string Name, MoneyDto Price, bool Purchasable);

public sealed record CategoryResponse(long Id, string Slug, string Name, long? ParentId);

/// <summary>Paramètres de GET /api/v1/products.</summary>
public sealed record ProductQuery(
    long? Category,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? Q,
    string? Sort,
    int? Page,
    int? Size);
