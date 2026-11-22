using Microsoft.EntityFrameworkCore;
using Souqly.Search.Api.Application;

namespace Souqly.Search.Api.Infrastructure;

/// <summary>
/// Réconciliation au démarrage : on relit une fois la liste publique de Catalog (produits en vente), page par page.
/// Elle remplit un index vide (nouvelle installation, produits créés avant que Catalog ne publie ses événements)
/// et rattrape un événement perdu. Une version plus ancienne n'écrase jamais l'index ; un produit absent de la liste
/// et non modifié depuis le début de la relecture est retiré. Ensuite, seuls les événements tiennent l'index à jour.
/// Un échec n'empêche pas le service de répondre (quelques nouveaux essais, puis au prochain démarrage).
/// </summary>
internal sealed partial class CatalogBackfill(IServiceScopeFactory scopes, IHttpClientFactory http, ILogger<CatalogBackfill> logger) : BackgroundService
{
    public const string Client = "catalog";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= 10 && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await RunAsync(stoppingToken);
                return;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException && !stoppingToken.IsCancellationRequested)
            {
                LogRetry(logger, e, attempt);
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), stoppingToken);
            }
        }
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var client = http.CreateClient(Client);
        var seen = new HashSet<long>();
        for (var page = 0; ; page++)
        {
            var result = await client.GetFromJsonAsync<CatalogPage>($"api/v1/products?Page={page}&Size=100", ct)
                         ?? throw new InvalidOperationException("Réponse vide de Catalog.");
            await using var scope = scopes.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetRequiredService<CatalogEventHandlers>();
            foreach (var p in result.Content)
            {
                await handlers.ApplyAsync(new ProductSnapshot(p.Id, p.VendorId, p.CategoryId, p.Sku, p.Name, p.Description ?? string.Empty,
                    decimal.Parse(p.Price.Amount, System.Globalization.CultureInfo.InvariantCulture), p.Price.Currency, p.Status, p.UpdatedAt ?? DateTimeOffset.MinValue), ct);
            }

            await scope.ServiceProvider.GetRequiredService<SearchDbContext>().SaveChangesAsync(ct);
            seen.UnionWith(result.Content.Select(p => p.Id));
            if (page + 1 >= result.TotalPages)
            {
                break;
            }
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SearchDbContext>();
            var gone = await db.Documents.Where(d => !seen.Contains(d.ProductId) && d.UpdatedAt < started).ToListAsync(ct);
            db.Documents.RemoveRange(gone);
            await db.SaveChangesAsync(ct);
        }

        LogDone(logger, seen.Count);
    }

    private sealed record CatalogPage(List<CatalogProduct> Content, int TotalPages);

    private sealed record CatalogProduct(long Id, string VendorId, long CategoryId, string Sku, string Name, string? Description,
        CatalogMoney Price, string Status, DateTimeOffset? UpdatedAt);

    private sealed record CatalogMoney(string Amount, string Currency);

    [LoggerMessage(Level = LogLevel.Information, Message = "Index de recherche réconcilié avec Catalog : {Count} produits en vente")]
    private static partial void LogDone(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Réconciliation de l'index avec Catalog impossible (essai {Attempt}) : nouvel essai")]
    private static partial void LogRetry(ILogger logger, Exception e, int attempt);
}
