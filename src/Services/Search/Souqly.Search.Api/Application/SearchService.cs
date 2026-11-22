using Microsoft.EntityFrameworkCore;
using Souqly.Chassis.Errors;
using Souqly.Search.Api.Domain;
using Souqly.Search.Api.Infrastructure;

namespace Souqly.Search.Api.Application;

/// <summary>Paramètres de GET /api/v1/search.</summary>
public sealed record SearchRequest(string? Q, long? Category, decimal? MinPrice, decimal? MaxPrice, int? Page, int? Size);

public sealed record SearchHitResponse(long ProductId, string VendorId, long CategoryId, string Sku, string Name, string Description, MoneyView Price, double Score);

public sealed record MoneyView(string Amount, string Currency);

public sealed record CategoryFacet(long CategoryId, int Count);

public sealed record PriceFacet(string? Min, string? Max);

public sealed record SearchFacets(IReadOnlyList<CategoryFacet> Categories, PriceFacet Price);

public sealed record SearchResponse(
    string Query,
    IReadOnlyList<SearchHitResponse> Content,
    int Page,
    int Size,
    long TotalElements,
    int TotalPages,
    SearchFacets Facets);

/// <summary>
/// US-04 : recherche en texte libre tolérante aux fautes (similarité de trigrammes sur les mots, pg_trgm),
/// filtres catégorie et prix, facettes par catégorie et fourchette de prix.
/// Les facettes ignorent le filtre de catégorie : on voit combien de résultats chaque catégorie apporterait.
/// </summary>
public sealed class SearchService(SearchDbContext db)
{
    /// <summary>Similarité minimale (0 à 1). 0,45 accepte « tapi berbre » → « tapis berbère » mais refuse « tapis » → « tajine ».</summary>
    public const double MinScore = 0.45;

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        if (request.MinPrice is < 0 || request.MaxPrice is < 0 || (request.MinPrice is { } min && request.MaxPrice is { } max && min > max))
        {
            throw new BusinessRuleException("invalid-price-range", "Fourchette de prix invalide.");
        }

        var page = Math.Max(0, request.Page ?? 0);
        var size = Math.Clamp(request.Size ?? 20, 1, 100);
        var q = TextNormalizer.Normalize(request.Q);
        if (q.Length > 100)
        {
            q = q[..100];
        }

        // Requête paramétrée (aucune concaténation de texte utilisateur). Colonnes en snake_case : convention de nommage d'EF.
        var matches = db.Database.SqlQuery<Row>($"""
            SELECT product_id, vendor_id, category_id, sku, name, description, price, currency,
                   CASE WHEN {q} = '' THEN 1.0 ELSE GREATEST(word_similarity({q}, search_text), CASE WHEN lower(sku) = {q} THEN 1.0 ELSE 0 END) END::float8 AS score
            FROM search_documents
            WHERE {q} = '' OR word_similarity({q}, search_text) >= {MinScore} OR lower(sku) = {q}
            """);

        var priced = matches.Where(r => (request.MinPrice == null || r.Price >= request.MinPrice) && (request.MaxPrice == null || r.Price <= request.MaxPrice));

        var categories = (await priced.GroupBy(r => r.CategoryId)
                .Select(g => new { CategoryId = g.Key, Count = g.Count() })
                .OrderByDescending(f => f.Count).ThenBy(f => f.CategoryId)
                .ToListAsync(ct))
            .Select(f => new CategoryFacet(f.CategoryId, f.Count))
            .ToList();

        var filtered = request.Category is { } category ? priced.Where(r => r.CategoryId == category) : priced;
        var total = await filtered.LongCountAsync(ct);
        var bounds = await filtered.GroupBy(_ => 1).Select(g => new { Min = g.Min(r => r.Price), Max = g.Max(r => r.Price) }).FirstOrDefaultAsync(ct);
        var rows = await filtered
            .OrderByDescending(r => r.Score).ThenBy(r => r.Name).ThenBy(r => r.ProductId)
            .Skip(page * size).Take(size)
            .ToListAsync(ct);

        return new SearchResponse(
            request.Q ?? string.Empty,
            rows.Select(r => new SearchHitResponse(r.ProductId, r.VendorId, r.CategoryId, r.Sku, r.Name, r.Description,
                Money(r.Price, r.Currency), Math.Round(r.Score, 3))).ToList(),
            page,
            size,
            total,
            (int)Math.Ceiling(total / (double)size),
            new SearchFacets(categories, new PriceFacet(bounds is null ? null : Format(bounds.Min), bounds is null ? null : Format(bounds.Max))));
    }

    private static MoneyView Money(decimal amount, string currency) => new(Format(amount), currency);

    private static string Format(decimal amount) => amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    internal sealed class Row
    {
        public long ProductId { get; init; }
        public string VendorId { get; init; } = string.Empty;
        public long CategoryId { get; init; }
        public string Sku { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public string Currency { get; init; } = string.Empty;
        public double Score { get; init; }
    }
}
