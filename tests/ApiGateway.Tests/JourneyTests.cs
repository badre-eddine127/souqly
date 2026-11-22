using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Souqly.ApiGateway;
using Souqly.Testing;

namespace ApiGateway.Tests;

/// <summary>Parcours réels écrits en Gherkin (page admin) : chaque action métier avec OK ou KO.</summary>
public class JourneyTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient ClientAs(string user, params string[] roles) => factory.CreateClient().WithToken(TestJwt.Token(user, roles));

    private async Task<JourneyPage> JourneysAsync(string user) =>
        (await ClientAs("journey-admin", "ADMIN").GetFromJsonAsync<JourneyPage>($"/api/v1/admin/journeys?user={user}", Json))!;

    [Fact]
    public async Task Les_actions_d_un_utilisateur_forment_un_scenario_Gherkin_avec_OK_et_KO()
    {
        var vendor = ClientAs("journey-fatima", "VENDOR");
        await vendor.PostAsync("/api/v1/products", null);
        await vendor.PostAsync("/api/v1/vendors/42/approval", JsonContent.Create(new { approved = false, reason = "test" }));

        var scenario = (await JourneysAsync("journey-fatima")).Content.ShouldHaveSingleItem();

        scenario.Ok.ShouldBeFalse();
        scenario.Passed.ShouldBe(1);
        scenario.Failed.ShouldBe(1);
        scenario.Lines.Select(l => $"{l.Keyword} {l.Text}").ShouldBe([
            "Étant donné que le vendeur \"journey-fatima\" est connecté",
            "Quand le vendeur \"journey-fatima\" crée un produit",
            "Alors la réponse est OK (200)",
            "Et le vendeur \"journey-fatima\" refuse la demande de boutique n° 42",
            "Alors la réponse est KO (403 · accès refusé)",
        ]);
    }

    [Fact]
    public async Task Le_corps_lu_pour_la_phrase_arrive_intact_au_service()
    {
        var body = """{"categoryId":2,"name":"Tapis","quantity":3}""";
        var response = await ClientAs("journey-body", "VENDOR")
            .PutAsync("/api/v1/products/7", new StringContent(body, Encoding.UTF8, "application/json"));

        (await response.Content.ReadAsStringAsync()).ShouldBe(body);
        (await JourneysAsync("journey-body")).Content.ShouldHaveSingleItem()
            .Lines.ShouldContain(l => l.Text == "le vendeur \"journey-body\" modifie {produit:7}");
    }

    [Fact]
    public async Task Les_lectures_ordinaires_et_les_visiteurs_anonymes_ne_sont_pas_journalises()
    {
        await ClientAs("journey-reader", "CUSTOMER").GetAsync("/api/v1/products");
        await factory.CreateClient().PostAsync("/api/v1/products", null);

        (await JourneysAsync("journey-reader")).Content.ShouldBeEmpty();
    }

    [Fact]
    public async Task Seul_l_administrateur_voit_les_parcours()
    {
        (await ClientAs("journey-vendor", "VENDOR").GetAsync("/api/v1/admin/journeys")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void Une_pause_de_plus_de_30_minutes_commence_un_nouveau_scenario()
    {
        var t0 = DateTimeOffset.UtcNow;
        JourneyStep Step(int minutes, int status) => new(t0.AddMinutes(minutes), "u1", "sara", "le client \"sara\"", "vide son panier", "DELETE", "/api/v1/carts/me", status, status < 400);

        var scenarios = JourneySetup.Group([Step(50, 204), Step(5, 409), Step(0, 204)], TimeSpan.FromMinutes(30)).ToList();

        scenarios.Count.ShouldBe(2);
        scenarios[0].Lines.Count.ShouldBe(3);   // le plus récent : une seule action
        scenarios[1].Ok.ShouldBeFalse();        // le premier contient un 409
    }
}
