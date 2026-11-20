using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Souqly.Catalog.Application.Products;
using Souqly.Chassis.Paging;

namespace Catalog.ApiTests;

[Trait("Category", "Integration")]
public class ProductsApiTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static ProductRequest Rug(string sku, string amount = "1200.00", long category = 2, bool publish = true) =>
        new(category, sku, $"Tapis {sku}", "Laine naturelle, tissé à la main", new MoneyDto(amount, "MAD"), publish);

    private async Task<ProductResponse> CreateAsync(HttpClient vendor, ProductRequest request)
    {
        var response = await vendor.PostAsJsonAsync("/api/v1/products", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>(Json))!;
    }

    [Fact]
    public async Task US03_un_vendeur_cree_un_produit_201_avec_Location()
    {
        var vendor = factory.ClientAs("vendor-us03", "VENDOR");

        var response = await vendor.PostAsJsonAsync("/api/v1/products", Rug("US03-A"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldEndWith($"/api/v1/products/{(await response.Content.ReadFromJsonAsync<ProductResponse>(Json))!.Id}");
    }

    [Fact]
    public async Task US02_la_fiche_publique_expose_le_prix_en_chaine_MAD()
    {
        var created = await CreateAsync(factory.ClientAs("vendor-us02", "VENDOR"), Rug("US02-A", "349.90"));

        using var anonymous = factory.CreateClient();
        var body = await anonymous.GetStringAsync($"/api/v1/products/{created.Id}");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("price").GetProperty("amount").GetString().ShouldBe("349.90");
        doc.RootElement.GetProperty("price").GetProperty("currency").GetString().ShouldBe("MAD");
    }

    [Fact]
    public async Task US01_liste_publique_paginee_filtree_et_triee_par_prix()
    {
        var vendor = factory.ClientAs("vendor-us01", "VENDOR");
        await CreateAsync(vendor, Rug("US01-C", "300.00", category: 3));
        await CreateAsync(vendor, Rug("US01-A", "100.00", category: 3));
        await CreateAsync(vendor, Rug("US01-B", "200.00", category: 3));
        await CreateAsync(vendor, Rug("US01-DRAFT", "50.00", category: 3, publish: false));

        using var anonymous = factory.CreateClient();
        var page = await anonymous.GetFromJsonAsync<PageResponse<ProductResponse>>(
            "/api/v1/products?category=3&sort=price,asc&page=0&size=2", Json);

        page!.TotalElements.ShouldBe(3);          // le brouillon n'est pas visible
        page.TotalPages.ShouldBe(2);
        page.Content.Select(p => p.Price.Amount).ShouldBe(["100.00", "200.00"]);
    }

    [Fact]
    public async Task US01_une_categorie_parente_inclut_ses_sous_categories()
    {
        var vendor = factory.ClientAs("vendor-parent", "VENDOR");
        await CreateAsync(vendor, Rug("PARENT-1", category: 4));   // Maroquinerie, enfant d'Artisanat (1)

        using var anonymous = factory.CreateClient();
        var page = await anonymous.GetFromJsonAsync<PageResponse<ProductResponse>>("/api/v1/products?category=1&size=100", Json);

        page!.Content.ShouldContain(p => p.Sku == "PARENT-1");
    }

    [Fact]
    public async Task La_taille_de_page_est_plafonnee_a_100()
    {
        using var anonymous = factory.CreateClient();
        var page = await anonymous.GetFromJsonAsync<PageResponse<ProductResponse>>("/api/v1/products?size=5000", Json);
        page!.Size.ShouldBe(100);
    }

    [Fact]
    public async Task Sans_authentification_la_creation_renvoie_401()
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/v1/products", Rug("ANON"));
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_client_ne_peut_pas_creer_de_produit_403()
    {
        var customer = factory.ClientAs("customer-1", "CUSTOMER");
        var response = await customer.PostAsJsonAsync("/api/v1/products", Rug("CUST"));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RG07_un_vendeur_ne_peut_pas_modifier_le_produit_d_un_autre_404()
    {
        var owner = factory.ClientAs("vendor-owner", "VENDOR");
        var intruder = factory.ClientAs("vendor-intruder", "VENDOR");
        var created = await CreateAsync(owner, Rug("OWNED-1"));

        var update = await intruder.PutAsJsonAsync($"/api/v1/products/{created.Id}", Rug("OWNED-1", "1.00"));
        var delete = await intruder.DeleteAsync($"/api/v1/products/{created.Id}");

        update.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.CreateClient().GetFromJsonAsync<ProductResponse>($"/api/v1/products/{created.Id}", Json))!
            .Price.Amount.ShouldBe("1200.00");
    }

    [Fact]
    public async Task US03_archiver_retire_le_produit_du_catalogue_public()
    {
        var vendor = factory.ClientAs("vendor-archive", "VENDOR");
        var created = await CreateAsync(vendor, Rug("ARCH-1"));

        (await vendor.DeleteAsync($"/api/v1/products/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/products/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var update = await vendor.PutAsJsonAsync($"/api/v1/products/{created.Id}", Rug("ARCH-1"));
        update.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Une_requete_invalide_renvoie_400_problem_json_avec_traceId()
    {
        var vendor = factory.ClientAs("vendor-invalid", "VENDOR");
        var response = await vendor.PostAsJsonAsync("/api/v1/products", Rug("BAD") with { Price = new MoneyDto("12.345", "EUR") });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errors").TryGetProperty("Price.Amount", out _).ShouldBeTrue();
        doc.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Une_categorie_inconnue_renvoie_422()
    {
        var vendor = factory.ClientAs("vendor-cat", "VENDOR");
        var response = await vendor.PostAsJsonAsync("/api/v1/products", Rug("CAT-404", category: 999));
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Un_SKU_en_double_chez_le_meme_vendeur_renvoie_409()
    {
        var vendor = factory.ClientAs("vendor-dup", "VENDOR");
        await CreateAsync(vendor, Rug("DUP-1"));
        (await vendor.PostAsJsonAsync("/api/v1/products", Rug("dup-1"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task L_endpoint_interne_des_prix_est_refuse_aux_utilisateurs()
    {
        var customer = factory.ClientAs("customer-2", "CUSTOMER");
        (await customer.GetAsync("/internal/v1/products/prices?ids=1")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RG02_un_client_technique_obtient_l_instantane_de_prix()
    {
        var created = await CreateAsync(factory.ClientAs("vendor-prices", "VENDOR"), Rug("PRICE-1", "75.50"));
        var ordering = factory.ClientAs("ordering-service", "SERVICE");

        var prices = await ordering.GetFromJsonAsync<List<PriceSnapshot>>($"/internal/v1/products/prices?ids={created.Id},999999", Json);

        prices!.ShouldHaveSingleItem().Price.Amount.ShouldBe("75.50");
        prices[0].Purchasable.ShouldBeTrue();
    }

    [Fact]
    public async Task Les_categories_de_reference_sont_disponibles()
    {
        using var anonymous = factory.CreateClient();
        var categories = await anonymous.GetFromJsonAsync<List<CategoryResponse>>("/api/v1/categories", Json);
        categories!.ShouldContain(c => c.Slug == "tapis" && c.ParentId == 1);
    }

    [Fact]
    public async Task Les_sondes_de_sante_repondent()
    {
        using var client = factory.CreateClient();
        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetStringAsync("/health/ready")).ShouldBe("Healthy");
    }
}
