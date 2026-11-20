using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Souqly.Inventory.Application;

namespace Inventory.ApiTests;

[Trait("Category", "Integration")]
public class StockApiTests(InventoryApiFactory factory) : IClassFixture<InventoryApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static long _nextProduct = 1_000;

    private static long NewProductId() => Interlocked.Increment(ref _nextProduct);

    [Fact]
    public async Task US10_le_vendeur_declare_son_stock_et_le_public_voit_la_disponibilite()
    {
        var productId = NewProductId();
        var vendor = factory.ClientAs("vendor-a", "VENDOR");

        var put = await vendor.PutAsJsonAsync($"/api/v1/stock-items/{productId}", new SetStockRequest(3));
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var anonymous = factory.CreateClient();
        var availability = await anonymous.GetFromJsonAsync<AvailabilityResponse>($"/api/v1/stock-items/{productId}", Json);
        availability.ShouldBe(new AvailabilityResponse(productId, 3, true));
    }

    [Fact]
    public async Task Un_produit_sans_stock_declare_est_indisponible()
    {
        using var anonymous = factory.CreateClient();
        var availability = await anonymous.GetFromJsonAsync<AvailabilityResponse>($"/api/v1/stock-items/{NewProductId()}", Json);
        availability!.InStock.ShouldBeFalse();
    }

    [Fact]
    public async Task US10_ajustement_avec_motif()
    {
        var productId = NewProductId();
        var vendor = factory.ClientAs("vendor-b", "VENDOR");
        await vendor.PutAsJsonAsync($"/api/v1/stock-items/{productId}", new SetStockRequest(10));

        var response = await vendor.PostAsJsonAsync($"/api/v1/stock-items/{productId}/adjustments", new AdjustStockRequest(-4, "Casse au déballage"));

        (await response.Content.ReadFromJsonAsync<StockItemResponse>(Json))!.OnHand.ShouldBe(6);
    }

    [Fact]
    public async Task Un_ajustement_qui_rendrait_le_stock_negatif_renvoie_422()
    {
        var productId = NewProductId();
        var vendor = factory.ClientAs("vendor-c", "VENDOR");
        await vendor.PutAsJsonAsync($"/api/v1/stock-items/{productId}", new SetStockRequest(1));

        var response = await vendor.PostAsJsonAsync($"/api/v1/stock-items/{productId}/adjustments", new AdjustStockRequest(-2, "Erreur"));
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task RG07_un_autre_vendeur_ne_peut_pas_modifier_ce_stock_404()
    {
        var productId = NewProductId();
        await factory.ClientAs("vendor-owner", "VENDOR").PutAsJsonAsync($"/api/v1/stock-items/{productId}", new SetStockRequest(5));

        var intruder = factory.ClientAs("vendor-intruder", "VENDOR");
        (await intruder.PutAsJsonAsync($"/api/v1/stock-items/{productId}", new SetStockRequest(0))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.GetAsync($"/api/v1/stock-items/{productId}/details")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Un_client_ne_peut_pas_modifier_le_stock_403_et_un_anonyme_401()
    {
        var productId = NewProductId();
        (await factory.ClientAs("sara", "CUSTOMER").PutAsJsonAsync($"/api/v1/stock-items/{productId}", new SetStockRequest(5)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClient().PutAsJsonAsync($"/api/v1/stock-items/{productId}", new SetStockRequest(5)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Les_reservations_sont_refusees_aux_utilisateurs()
    {
        var request = new ReserveStockRequest(Guid.NewGuid(), [new ReserveLine(1, 1)]);
        (await factory.ClientAs("vendor-a", "VENDOR").PostAsJsonAsync("/internal/v1/reservations", request))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Le_vendeur_ne_peut_pas_descendre_sous_le_stock_reserve()
    {
        var productId = NewProductId();
        var vendor = factory.ClientAs("vendor-d", "VENDOR");
        await vendor.PutAsJsonAsync($"/api/v1/stock-items/{productId}", new SetStockRequest(5));
        var ordering = factory.ClientAs("ordering", "SERVICE");
        await ordering.PostAsJsonAsync("/internal/v1/reservations", new ReserveStockRequest(Guid.NewGuid(), [new ReserveLine(productId, 4)]));

        var response = await vendor.PutAsJsonAsync($"/api/v1/stock-items/{productId}", new SetStockRequest(3));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
