using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souqly.Inventory.Application;
using Souqly.Inventory.Domain;
using Souqly.Inventory.Infrastructure.Persistence;

namespace Inventory.ApiTests;

[Trait("Category", "Integration")]
public class ReservationTests(InventoryApiFactory factory) : IClassFixture<InventoryApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static long _nextProduct = 5_000;

    private HttpClient Ordering => factory.ClientAs("ordering-service", "SERVICE");

    private async Task<long> StockAsync(int onHand)
    {
        var productId = Interlocked.Increment(ref _nextProduct);
        var response = await factory.ClientAs("vendor-r", "VENDOR").PutAsJsonAsync($"/api/v1/stock-items/{productId}", new SetStockRequest(onHand));
        response.EnsureSuccessStatusCode();
        return productId;
    }

    private async Task<StockItem> ReadStockAsync(long productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().StockItems.AsNoTracking().SingleAsync(s => s.ProductId == productId);
    }

    private async Task<HttpResponseMessage> ReserveAsync(Guid orderId, params ReserveLine[] lines) =>
        await Ordering.PostAsJsonAsync("/internal/v1/reservations", new ReserveStockRequest(orderId, lines));

    [Fact]
    public async Task US11_reserver_bloque_le_stock_sans_le_sortir_du_physique()
    {
        var productId = await StockAsync(3);

        var response = await ReserveAsync(Guid.NewGuid(), new ReserveLine(productId, 1));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stock = await ReadStockAsync(productId);
        (stock.OnHand, stock.Reserved, stock.Available).ShouldBe((3, 1, 2));
    }

    [Fact]
    public async Task Stock_insuffisant_409_OUT_OF_STOCK_et_rien_n_est_reserve_tout_ou_rien()
    {
        var available = await StockAsync(5);
        var empty = await StockAsync(0);

        var response = await ReserveAsync(Guid.NewGuid(), new ReserveLine(available, 2), new ReserveLine(empty, 1));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var result = await response.Content.ReadFromJsonAsync<ReservationResult>(Json);
        result!.Status.ShouldBe("OUT_OF_STOCK");
        result.FailedProductId.ShouldBe(empty);
        (await ReadStockAsync(available)).Reserved.ShouldBe(0);   // la première ligne a été annulée
    }

    [Fact]
    public async Task US09_rejouer_la_meme_commande_ne_reserve_pas_deux_fois()
    {
        var productId = await StockAsync(10);
        var orderId = Guid.NewGuid();

        await ReserveAsync(orderId, new ReserveLine(productId, 3));
        var replay = await ReserveAsync(orderId, new ReserveLine(productId, 3));

        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadStockAsync(productId)).Reserved.ShouldBe(3);
    }

    [Fact]
    public async Task Compensation_paiement_refuse_le_stock_disponible_revient_a_3()
    {
        var productId = await StockAsync(3);
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, new ReserveLine(productId, 1));

        (await Ordering.PostAsync($"/internal/v1/reservations/{orderId}/release", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Ordering.PostAsync($"/internal/v1/reservations/{orderId}/release", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);   // rejouable

        var stock = await ReadStockAsync(productId);
        (stock.OnHand, stock.Reserved).ShouldBe((3, 0));
    }

    [Fact]
    public async Task Commande_confirmee_le_stock_physique_passe_de_3_a_2()
    {
        var productId = await StockAsync(3);
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, new ReserveLine(productId, 1));

        await Ordering.PostAsync($"/internal/v1/reservations/{orderId}/commit", null);
        await Ordering.PostAsync($"/internal/v1/reservations/{orderId}/commit", null);   // rejoué : sans effet

        var stock = await ReadStockAsync(productId);
        (stock.OnHand, stock.Reserved, stock.Available).ShouldBe((2, 0, 2));
    }

    [Fact]
    public async Task RG03_une_reservation_de_plus_de_15_minutes_expire_et_rend_le_stock()
    {
        var productId = await StockAsync(4);
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, new ReserveLine(productId, 4));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await db.Reservations.Where(r => r.OrderId == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));

        var expired = await scope.ServiceProvider.GetRequiredService<ReservationService>().ExpireDueAsync(200, CancellationToken.None);

        expired.ShouldBeGreaterThanOrEqualTo(1);
        (await ReadStockAsync(productId)).Reserved.ShouldBe(0);
        (await db.Reservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId)).Status.ShouldBe(ReservationStatus.Expired);
    }
}
