using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Souqly.Ordering.Application;
using Souqly.Testing;

namespace Ordering.ApiTests;

[Trait("Category", "Integration")]
[Collection("ordering")]
public class OrdersApiTests(OrderingApiFactory factory) : IClassFixture<OrderingApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Customer(string id = "sara") => factory.ClientAs(id, "CUSTOMER");

    private static async Task<HttpResponseMessage> PlaceAsync(HttpClient client, Guid key, params (long Product, int Qty)[] lines)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new PlaceOrderRequest(lines.Select(l => new OrderLineRequest(l.Product, l.Qty)).ToList())),
        };
        request.Headers.Add("Idempotency-Key", key.ToString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task US06_une_commande_est_acceptee_en_PENDING_avec_202_et_Location()
    {
        factory.Catalog.Mode = "ok";
        var response = await PlaceAsync(Customer("us06"), Guid.NewGuid(), (1, 1), (2, 2));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponse>(Json))!;
        response.Headers.Location!.ToString().ShouldEndWith($"/api/v1/orders/{order.Id}");
        order.Status.ShouldBe("PENDING");
        order.Total.ShouldBe(new MoneyDto("1579.80", "MAD"));   // 1200.00 + 2 × 189.90, prix lus dans le catalogue
    }

    [Fact]
    public async Task RG02_le_prix_vient_du_catalogue_avec_un_jeton_technique()
    {
        factory.Catalog.Mode = "ok";
        await PlaceAsync(Customer("rg02"), Guid.NewGuid(), (1, 1));
        factory.Catalog.LastAuthorization.ShouldBe("Bearer service-token-123");
    }

    [Fact]
    public async Task US09_un_double_clic_avec_la_meme_cle_ne_cree_qu_une_commande()
    {
        factory.Catalog.Mode = "ok";
        var client = Customer("us09");
        var key = Guid.NewGuid();

        var first = await PlaceAsync(client, key, (1, 1));
        var second = await PlaceAsync(client, key, (1, 1));

        var a = (await first.Content.ReadFromJsonAsync<OrderResponse>(Json))!;
        var b = (await second.Content.ReadFromJsonAsync<OrderResponse>(Json))!;
        b.Id.ShouldBe(a.Id);
        second.Headers.GetValues("Idempotent-Replayed").ShouldContain("true");
        var mine = await client.GetFromJsonAsync<OrderPage>("/api/v1/orders", Json);
        mine!.TotalElements.ShouldBe(1);
    }

    [Fact]
    public async Task US09_dix_envois_simultanes_avec_la_meme_cle_donnent_une_seule_commande()
    {
        factory.Catalog.Mode = "ok";
        var client = Customer("us09-race");
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PlaceAsync(client, key, (2, 1))));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Accepted);
        var ids = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<OrderResponse>(Json))!.Id));
        ids.Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task La_meme_cle_avec_un_autre_panier_est_refusee_422()
    {
        factory.Catalog.Mode = "ok";
        var client = Customer("reuse");
        var key = Guid.NewGuid();
        await PlaceAsync(client, key, (1, 1));

        var response = await PlaceAsync(client, key, (2, 3));
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Sans_Idempotency_Key_la_commande_est_refusee_400()
    {
        var response = await Customer().PostAsJsonAsync("/api/v1/orders", new PlaceOrderRequest([new OrderLineRequest(1, 1)]));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Un_produit_inconnu_ou_non_publie_est_refuse_422()
    {
        factory.Catalog.Mode = "ok";
        (await PlaceAsync(Customer(), Guid.NewGuid(), (3, 1))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await PlaceAsync(Customer(), Guid.NewGuid(), (999, 1))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task RG04_onze_unites_sont_refusees_400()
    {
        (await PlaceAsync(Customer(), Guid.NewGuid(), (1, 11))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RG07_un_client_ne_voit_pas_la_commande_d_un_autre_404()
    {
        factory.Catalog.Mode = "ok";
        var created = await (await PlaceAsync(Customer("owner"), Guid.NewGuid(), (1, 1))).Content.ReadFromJsonAsync<OrderResponse>(Json);

        (await Customer("intruder").GetAsync($"/api/v1/orders/{created!.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Customer("intruder").PostAsync($"/api/v1/orders/{created.Id}/cancellation", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Customer("owner").GetAsync($"/api/v1/orders/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task US08_le_client_annule_sa_commande_PENDING_puis_ne_peut_plus_l_annuler()
    {
        factory.Catalog.Mode = "ok";
        var client = Customer("us08");
        var created = await (await PlaceAsync(client, Guid.NewGuid(), (1, 1))).Content.ReadFromJsonAsync<OrderResponse>(Json);

        var cancel = await client.PostAsync($"/api/v1/orders/{created!.Id}/cancellation", null);
        (await cancel.Content.ReadFromJsonAsync<OrderResponse>(Json))!.Status.ShouldBe("CANCELLED");
        (await client.PostAsync($"/api/v1/orders/{created.Id}/cancellation", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RG05_le_vendeur_ne_voit_que_ses_lignes()
    {
        factory.Catalog.Mode = "ok";
        await PlaceAsync(Customer("rg05"), Guid.NewGuid(), (1, 1), (2, 1));

        var karim = await factory.ClientAs("vendor-karim", "VENDOR")
            .GetFromJsonAsync<Souqly.Chassis.Paging.PageResponse<VendorOrderResponse>>("/api/v1/vendor/orders?status=PENDING", Json);

        karim!.Content.ShouldNotBeEmpty();
        karim.Content.SelectMany(o => o.Lines).ShouldAllBe(l => l.VendorId == "vendor-karim");
    }

    [Fact]
    public async Task Un_vendeur_ne_peut_pas_passer_commande_403_un_anonyme_401_un_jeton_falsifie_401()
    {
        (await PlaceAsync(factory.ClientAs("vendor-x", "VENDOR"), Guid.NewGuid(), (1, 1))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PlaceAsync(factory.CreateClient(), Guid.NewGuid(), (1, 1))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PlaceAsync(factory.CreateClient().WithToken(TestJwt.ForgedToken("sara", "CUSTOMER")), Guid.NewGuid(), (1, 1)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Catalogue_en_panne_reponse_503_en_moins_de_3_secondes_avec_Retry_After()
    {
        factory.Catalog.Mode = "down";
        var stopwatch = Stopwatch.StartNew();
        var response = await PlaceAsync(Customer("down"), Guid.NewGuid(), (1, 1));
        stopwatch.Stop();
        factory.Catalog.Mode = "ok";

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull();
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Catalogue_trop_lent_reponse_503_en_moins_de_3_secondes()
    {
        factory.Catalog.Mode = "slow";
        var stopwatch = Stopwatch.StartNew();
        var response = await PlaceAsync(Customer("slow"), Guid.NewGuid(), (1, 1));
        stopwatch.Stop();
        factory.Catalog.Mode = "ok";

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
    }
}
