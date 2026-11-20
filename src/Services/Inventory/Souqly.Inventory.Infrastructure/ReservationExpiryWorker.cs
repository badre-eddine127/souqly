using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Souqly.Inventory.Application;
using Souqly.Inventory.Infrastructure.Persistence;

namespace Souqly.Inventory.Infrastructure;

/// <summary>
/// RG-03 : toutes les 30 s, rend le stock des réservations de plus de 15 minutes.
/// Un verrou consultatif PostgreSQL garantit qu'une seule réplique du service travaille à la fois.
/// </summary>
internal sealed class ReservationExpiryWorker(IServiceScopeFactory scopes, ILogger<ReservationExpiryWorker> logger) : BackgroundService
{
    private const long LockKey = 0x50_51_4C_59_45_58_50; // "SOUQLY-EXP"
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Échec du passage d'expiration des réservations ; nouvel essai dans {Period}", Period);
            }
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var locked = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({LockKey}) AS \"Value\"").SingleAsync(ct);
            if (!locked)
            {
                return 0;   // une autre réplique s'en charge
            }

            try
            {
                var expired = await scope.ServiceProvider.GetRequiredService<ReservationService>().ExpireDueAsync(200, ct);
                if (expired > 0)
                {
                    logger.LogInformation("{Count} réservation(s) expirée(s) : stock rendu", expired);
                }

                return expired;
            }
            finally
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({LockKey})", ct);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
