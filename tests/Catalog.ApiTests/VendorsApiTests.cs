using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Souqly.Catalog.Application.Vendors;
using Souqly.Chassis.Paging;

namespace Catalog.ApiTests;

/// <summary>US-16 de bout en bout dans le service : demande, file de l'administrateur, décision, boutiques publiques.</summary>
[Trait("Category", "Integration")]
public class VendorsApiTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<VendorApplicationResponse> SubmitAsync(string userId, string shop = "Atelier Zellige")
    {
        var response = await factory.ClientAs(userId, "CUSTOMER")
            .PostAsJsonAsync("/api/v1/vendors", new VendorApplicationRequest(shop, "Fès", "Zellige fait main"));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<VendorApplicationResponse>(Json))!;
    }

    [Fact]
    public async Task US16_un_client_demande_une_boutique_et_suit_sa_demande()
    {
        var created = await SubmitAsync("cust-us16-a");

        created.Status.ShouldBe("PENDING");
        var mine = await factory.ClientAs("cust-us16-a", "CUSTOMER")
            .GetFromJsonAsync<VendorApplicationResponse>("/api/v1/vendors/me", Json);
        mine!.Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task US16_une_seconde_demande_en_attente_est_un_conflit_409()
    {
        await SubmitAsync("cust-us16-b");

        var again = await factory.ClientAs("cust-us16-b", "CUSTOMER")
            .PostAsJsonAsync("/api/v1/vendors", new VendorApplicationRequest("Autre", "Fès", null));

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task US16_seul_un_administrateur_voit_la_file_et_decide()
    {
        var created = await SubmitAsync("cust-us16-c");
        var customer = factory.ClientAs("cust-us16-c", "CUSTOMER");

        (await customer.GetAsync("/api/v1/vendors?status=PENDING")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await customer.PostAsJsonAsync($"/api/v1/vendors/{created.Id}/approval", new VendorDecisionRequest(true, null)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = factory.ClientAs("admin-us16", "ADMIN");
        var queue = await admin.GetFromJsonAsync<PageResponse<VendorApplicationResponse>>("/api/v1/vendors?status=PENDING&size=100", Json);
        queue!.Content.ShouldContain(a => a.Id == created.Id);
    }

    [Fact]
    public async Task US16_validee_le_role_VENDOR_est_attribue_et_la_boutique_devient_publique()
    {
        var created = await SubmitAsync("cust-us16-d", "Coopérative Tifawin");
        var admin = factory.ClientAs("admin-us16", "ADMIN");

        var response = await admin.PostAsJsonAsync($"/api/v1/vendors/{created.Id}/approval", new VendorDecisionRequest(true, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<VendorApplicationResponse>(Json))!.Status.ShouldBe("APPROVED");
        factory.Roles.Granted.ShouldContain("cust-us16-d");

        using var anonymous = factory.CreateClient();
        var shops = await anonymous.GetFromJsonAsync<List<VendorShopResponse>>("/api/v1/vendors/shops", Json);
        shops!.ShouldContain(s => s.VendorId == "cust-us16-d" && s.ShopName == "Coopérative Tifawin");

        (await admin.PostAsJsonAsync($"/api/v1/vendors/{created.Id}/approval", new VendorDecisionRequest(false, "Erreur")))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task US16_un_refus_sans_motif_est_400_et_un_refus_motive_permet_de_resoumettre()
    {
        var created = await SubmitAsync("cust-us16-e");
        var admin = factory.ClientAs("admin-us16", "ADMIN");

        (await admin.PostAsJsonAsync($"/api/v1/vendors/{created.Id}/approval", new VendorDecisionRequest(false, null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var rejected = await admin.PostAsJsonAsync($"/api/v1/vendors/{created.Id}/approval",
            new VendorDecisionRequest(false, "Merci de décrire vos produits"));
        (await rejected.Content.ReadFromJsonAsync<VendorApplicationResponse>(Json))!.Status.ShouldBe("REJECTED");
        factory.Roles.Granted.ShouldNotContain("cust-us16-e");

        var resubmitted = await factory.ClientAs("cust-us16-e", "CUSTOMER")
            .PostAsJsonAsync("/api/v1/vendors", new VendorApplicationRequest("Atelier Zellige", "Fès", "Zellige émaillé, carreaux et fontaines"));
        resubmitted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await resubmitted.Content.ReadFromJsonAsync<VendorApplicationResponse>(Json))!.Status.ShouldBe("PENDING");
    }

    [Fact]
    public async Task US16_sans_demande_me_renvoie_404_et_anonyme_401()
    {
        (await factory.ClientAs("cust-us16-none", "CUSTOMER").GetAsync("/api/v1/vendors/me")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/vendors", new VendorApplicationRequest("X", "Y", null)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
