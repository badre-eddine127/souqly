using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souqly.Inventory.Application;
using Souqly.Inventory.Domain;
using Souqly.Inventory.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Inventory.ApiTests;

/// <summary>
/// RG-01 : le stock disponible n'est jamais négatif, même sous 500 achats concurrents (vente flash).
/// Exigence non fonctionnelle : 0 survente sur 500 achats concurrents pour 100 unités.
/// </summary>
[Trait("Category", "Integration")]
public class FlashSaleTests(InventoryApiFactory factory, ITestOutputHelper output) : IClassFixture<InventoryApiFactory>
{
    private const long ProductId = 777;
    private const int Units = 100;
    private const int Buyers = 500;

    [Fact]
    public async Task RG01_vente_flash_500_achats_concurrents_pour_100_unites_zero_survente()
    {
        await using (var setup = factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<InventoryDbContext>();
            db.StockItems.Add(StockItem.Declare(ProductId, "vendor-flash", Units, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var stopwatch = Stopwatch.StartNew();
        using var gate = new ManualResetEventSlim(false);
        var buyers = Enumerable.Range(0, Buyers).Select(_ => Task.Run(async () =>
        {
            gate.Wait();   // tous les acheteurs partent en même temps
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ReservationService>();
            return await service.ReserveAsync(
                new ReserveStockRequest(Guid.NewGuid(), [new ReserveLine(ProductId, 1)]), CancellationToken.None);
        })).ToList();
        gate.Set();
        var results = await Task.WhenAll(buyers);
        stopwatch.Stop();

        await using var check = factory.Services.CreateAsyncScope();
        var verify = check.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var stock = await verify.StockItems.AsNoTracking().SingleAsync(s => s.ProductId == ProductId);
        var reservations = await verify.Reservations.CountAsync(r => r.ProductId == ProductId);

        var won = results.Count(r => r.Succeeded);
        var lost = results.Count(r => r.Status == ReservationResult.OutOfStock);
        output.WriteLine($"Vente flash : {Buyers} acheteurs, {Units} unités → {won} réservées, {lost} refusées (OUT_OF_STOCK), " +
                         $"stock réservé = {stock.Reserved}/{stock.OnHand}, {reservations} lignes de réservation, {stopwatch.ElapsedMilliseconds} ms");

        won.ShouldBe(Units);
        lost.ShouldBe(Buyers - Units);
        stock.Reserved.ShouldBe(Units);
        stock.Available.ShouldBe(0);
        reservations.ShouldBe(Units);
    }
}
