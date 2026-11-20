using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Souqly.Ordering.Application;
using Souqly.Ordering.Infrastructure.Persistence;

namespace Ordering.ApiTests;

/// <summary>US-13 / US-14 : expédition par vendeur et suivi de livraison par le client.</summary>
[Trait("Category", "Integration")]
[Collection("ordering")]
public class ShippingApiTests(OrderingApiFactory factory) : IClassFixture<OrderingApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Customer(string id) => factory.ClientAs(id, "CUSTOMER");

    private HttpClient Vendor(string id) => factory.ClientAs(id, "VENDOR");

    /// <summary>Commande Tapis (Fatima) + Huile d'argan (Karim), amenée à CONFIRMED comme le ferait la saga.</summary>
    private async Task<Guid> ConfirmedOrderAsync(string customer)
    {
        factory.Catalog.Mode = "ok";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new PlaceOrderRequest([new OrderLineRequest(1, 1), new OrderLineRequest(2, 1)])),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var order = await (await Customer(customer).SendAsync(request)).Content.ReadFromJsonAsync<OrderResponse>(Json);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var entity = await db.Orders.FindAsync(order!.Id);
        entity!.MarkStockReserved(DateTimeOffset.UtcNow);
        entity.Confirm(Guid.NewGuid(), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private Task<HttpResponseMessage> ShipAsync(string vendor, Guid orderId, string carrier = "AMANA", string tracking = "AM123456789MA") =>
        Vendor(vendor).PostAsJsonAsync($"/api/v1/vendor/orders/{orderId}/shipment", new ShipRequest(carrier, tracking));

    [Fact]
    public async Task US13_le_vendeur_expedie_son_colis_et_le_client_voit_le_numero_de_suivi_US14()
    {
        var id = await ConfirmedOrderAsync("ship-1");

        var shipped = await ShipAsync("vendor-fatima", id);
        shipped.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = (await shipped.Content.ReadFromJsonAsync<VendorOrderResponse>(Json))!;
        view.Status.ShouldBe("CONFIRMED");   // Karim n'a pas encore expédié
        view.Shipment!.TrackingNumber.ShouldBe("AM123456789MA");

        var order = await Customer("ship-1").GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{id}", Json);
        order!.Shipments.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            s => s.Carrier.ShouldBe("AMANA"),
            s => s.Status.ShouldBe("IN_TRANSIT"));
    }

    [Fact]
    public async Task Quand_tous_les_colis_sont_partis_SHIPPED_puis_livres_DELIVERED()
    {
        var id = await ConfirmedOrderAsync("ship-2");
        await ShipAsync("vendor-fatima", id);
        var last = await (await ShipAsync("vendor-karim", id, "CTM", "CTM-998877")).Content.ReadFromJsonAsync<VendorOrderResponse>(Json);
        last!.Status.ShouldBe("SHIPPED");

        await Vendor("vendor-fatima").PostAsync($"/api/v1/vendor/orders/{id}/delivery", null);
        (await Vendor("vendor-fatima").PostAsync($"/api/v1/vendor/orders/{id}/delivery", null)).StatusCode.ShouldBe(HttpStatusCode.OK);   // idempotent
        var done = await (await Vendor("vendor-karim").PostAsync($"/api/v1/vendor/orders/{id}/delivery", null)).Content.ReadFromJsonAsync<VendorOrderResponse>(Json);
        done!.Status.ShouldBe("DELIVERED");

        var order = await Customer("ship-2").GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{id}", Json);
        order!.Shipments.ShouldAllBe(s => s.Status == "DELIVERED");
    }

    [Fact]
    public async Task RG06_le_client_ne_peut_plus_annuler_des_qu_un_colis_est_parti()
    {
        var id = await ConfirmedOrderAsync("ship-3");
        await ShipAsync("vendor-fatima", id);
        (await Customer("ship-3").PostAsync($"/api/v1/orders/{id}/cancellation", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Un_autre_vendeur_ne_voit_pas_la_commande_404_et_un_client_ne_peut_pas_expedier_403()
    {
        var id = await ConfirmedOrderAsync("ship-4");
        (await ShipAsync("vendor-intrus", id)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Customer("ship-4").PostAsJsonAsync($"/api/v1/vendor/orders/{id}/shipment", new ShipRequest("AMANA", "AM123456789MA")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Transporteur_inconnu_ou_suivi_invalide_400_double_expedition_409()
    {
        var id = await ConfirmedOrderAsync("ship-5");
        (await ShipAsync("vendor-fatima", id, carrier: "POSTE")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ShipAsync("vendor-fatima", id, tracking: "abc")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ShipAsync("vendor-fatima", id)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ShipAsync("vendor-fatima", id)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Une_commande_non_payee_ne_peut_pas_etre_expediee_409()
    {
        factory.Catalog.Mode = "ok";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new PlaceOrderRequest([new OrderLineRequest(1, 1)])),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var pending = await (await Customer("ship-6").SendAsync(request)).Content.ReadFromJsonAsync<OrderResponse>(Json);

        (await ShipAsync("vendor-fatima", pending!.Id)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
