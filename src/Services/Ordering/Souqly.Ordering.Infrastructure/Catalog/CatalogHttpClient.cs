using System.Net.Http.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Souqly.Chassis.Errors;
using Souqly.Ordering.Application;

namespace Souqly.Ordering.Infrastructure.Catalog;

/// <summary>
/// Seul appel synchrone du parcours de commande : GET /internal/v1/products/prices du catalogue (RG-02).
/// Protégé par un jeton technique (client credentials) et par Polly : délai, un nouvel essai, disjoncteur.
/// Si le catalogue ne répond pas, on renvoie 503 en moins de 3 s ; on n'invente jamais un prix.
/// </summary>
internal sealed class CatalogHttpClient(HttpClient http) : ICatalogPort
{
    private const int RetryAfterSeconds = 10;

    public async Task<IReadOnlyList<CatalogPrice>> GetPricesAsync(IReadOnlyCollection<long> productIds, CancellationToken ct)
    {
        try
        {
            var snapshots = await http.GetFromJsonAsync<List<PriceSnapshotDto>>(
                $"/internal/v1/products/prices?ids={string.Join(',', productIds)}", ct) ?? [];
            return snapshots.Select(s => new CatalogPrice(s.ProductId, s.VendorId, s.Name, s.Price.ToDecimal(), s.Purchasable)).ToList();
        }
        catch (Exception e) when (e is HttpRequestException or BrokenCircuitException or TimeoutRejectedException or TaskCanceledException
                                     && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("Catalog", RetryAfterSeconds, e);
        }
    }

    /// <summary>Contrat de réponse du catalogue (copie locale : pas de DTO partagé entre services, ADR-009).</summary>
    private sealed record PriceSnapshotDto(long ProductId, string VendorId, string Name, MoneyJson Price, bool Purchasable);

    private sealed record MoneyJson(string Amount, string Currency)
    {
        public decimal ToDecimal() => decimal.Parse(Amount, System.Globalization.CultureInfo.InvariantCulture);
    }
}
