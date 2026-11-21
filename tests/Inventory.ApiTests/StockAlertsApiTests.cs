using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Souqly.Inventory.Application;

namespace Inventory.ApiTests;

/// <summary>US-17 : ruptures et stocks bas du vendeur, pour son tableau de bord.</summary>
[Trait("Category", "Integration")]
public class StockAlertsApiTests(InventoryApiFactory factory) : IClassFixture<InventoryApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task US17_le_vendeur_voit_ses_ruptures_et_stocks_bas_les_plus_urgents_d_abord()
    {
        var vendor = factory.ClientAs("vendor-alerts", "VENDOR");
        await vendor.PutAsJsonAsync("/api/v1/stock-items/91001", new SetStockRequest(0));
        await vendor.PutAsJsonAsync("/api/v1/stock-items/91002", new SetStockRequest(2));
        await vendor.PutAsJsonAsync("/api/v1/stock-items/91003", new SetStockRequest(50));
        await factory.ClientAs("vendor-other", "VENDOR").PutAsJsonAsync("/api/v1/stock-items/91004", new SetStockRequest(0));

        var alerts = await vendor.GetFromJsonAsync<List<StockItemResponse>>("/api/v1/vendor/stock-alerts", Json);

        alerts!.Select(a => a.ProductId).ShouldBe([91001L, 91002L]);
        alerts![0].Available.ShouldBe(0);

        var strict = await vendor.GetFromJsonAsync<List<StockItemResponse>>("/api/v1/vendor/stock-alerts?threshold=0", Json);
        strict!.ShouldHaveSingleItem().ProductId.ShouldBe(91001L);
    }

    [Fact]
    public async Task US17_un_client_n_a_pas_acces_aux_alertes_de_stock()
    {
        (await factory.ClientAs("client-alerts", "CUSTOMER").GetAsync("/api/v1/vendor/stock-alerts"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
