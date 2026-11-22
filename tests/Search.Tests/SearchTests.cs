using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Souqly.Messaging;
using Souqly.Search.Api.Application;
using Souqly.Search.Api.Domain;
using static Search.Tests.SearchFactory;

namespace Search.Tests;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Tapis berbère, Azilal !", "tapis berbere azilal")]
    [InlineData("Huile d'argan  BIO", "huile d argan bio")]
    [InlineData("Bœuf — Fès", "boeuf fes")]
    [InlineData("   ", "")]
    public void La_normalisation_retire_accents_casse_et_ponctuation(string input, string expected) =>
        TextNormalizer.Normalize(input).ShouldBe(expected);
}

/// <summary>US-04 : recherche tolérante aux fautes, filtres, facettes, et index tenu à jour par les événements de Catalog.</summary>
[Trait("Category", "Integration")]
public class SearchTests(SearchFactory factory) : IClassFixture<SearchFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static long _next = 1000;
    private readonly long _base = Interlocked.Add(ref _next, 100);

    // Un mot rare par classe de test pour isoler les jeux de données dans la même base.
    public async Task InitializeAsync()
    {
        await factory.DeliverAsync(_base + 1, new ProductCreated(Product(_base + 1, "Tapis berbère Beni Ouarain", 2890m, category: 2, description: "Laine nouée main, Atlas")));
        await factory.DeliverAsync(_base + 2, new ProductCreated(Product(_base + 2, "Tapis kilim Azilal", 1450m, category: 2)));
        await factory.DeliverAsync(_base + 3, new ProductCreated(Product(_base + 3, "Tajine de Safi", 260m, category: 3, description: "Terre cuite émaillée")));
        await factory.DeliverAsync(_base + 4, new ProductCreated(Product(_base + 4, "Huile d'argan bio", 180m, category: 6)));
        await factory.DeliverAsync(_base + 5, new ProductCreated(Product(_base + 5, "Pouf en cuir brodé", 650m, category: 4, status: "DRAFT")));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<SearchResponse> SearchAsync(string query)
    {
        var response = await factory.CreateClient().GetAsync($"/api/v1/search?{query}&size=100");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<SearchResponse>(Json))!;
        // On ne garde que les produits de ce test (les autres tests partagent la base).
        var mine = result.Content.Where(h => h.ProductId > _base && h.ProductId <= _base + 100).ToList();
        return result with { Content = mine };
    }

    [Fact]
    public async Task Une_recherche_avec_des_fautes_trouve_le_bon_produit_en_premier()
    {
        var result = await SearchAsync("q=tapi%20berbre");

        result.Content[0].ProductId.ShouldBe(_base + 1);
        result.Content.ShouldNotContain(h => h.ProductId == _base + 3);   // « tajine » n'est pas un « tapis »
    }

    [Fact]
    public async Task Les_accents_et_majuscules_n_ont_pas_d_importance()
    {
        (await SearchAsync("q=ARGANE")).Content.ShouldContain(h => h.ProductId == _base + 4);
        (await SearchAsync("q=terre%20cuite%20emaillee")).Content.ShouldContain(h => h.ProductId == _base + 3);
    }

    [Fact]
    public async Task Les_filtres_prix_et_categorie_se_combinent_et_les_facettes_comptent_par_categorie()
    {
        var result = await SearchAsync("q=tapis&minPrice=1000&maxPrice=2000&category=2");

        result.Content.Select(h => h.ProductId).ShouldBe([_base + 2]);
        result.Content[0].Price.Amount.ShouldBe("1450.00");

        var facets = await SearchAsync("q=tapis&category=3");
        facets.Facets.Categories.ShouldContain(f => f.CategoryId == 2 && f.Count >= 2);   // les facettes ignorent le filtre de catégorie
    }

    [Fact]
    public async Task Un_brouillon_n_est_pas_trouvable_et_un_produit_archive_disparait()
    {
        (await SearchAsync("q=pouf%20cuir")).Content.ShouldBeEmpty();

        await factory.DeliverAsync(_base + 3, new ProductArchived(Product(_base + 3, "Tajine de Safi", 260m, category: 3, status: "ARCHIVED")));

        (await SearchAsync("q=tajine")).Content.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_modification_est_visible_et_un_evenement_en_retard_ne_l_ecrase_pas()
    {
        var now = DateTimeOffset.UtcNow;
        await factory.DeliverAsync(_base + 2, new ProductUpdated(Product(_base + 2, "Kilim Azilal tissé main", 1290m, updatedAt: now.AddMinutes(1))));
        await factory.DeliverAsync(_base + 2, new ProductUpdated(Product(_base + 2, "Ancien nom", 9999m, updatedAt: now.AddMinutes(-5))));

        var hit = (await SearchAsync("q=kilim")).Content.ShouldHaveSingleItem();
        hit.Name.ShouldBe("Kilim Azilal tissé main");
        hit.Price.Amount.ShouldBe("1290.00");
    }

    [Fact]
    public async Task Un_meme_evenement_recu_deux_fois_n_est_traite_qu_une_fois()
    {
        var id = Guid.NewGuid();
        var message = new ProductCreated(Product(_base + 9, "Lanterne en laiton ciselé", 540m, category: 1));

        (await factory.DeliverAsync(_base + 9, message, id)).ShouldBe(DispatchOutcome.Handled);
        (await factory.DeliverAsync(_base + 9, message, id)).ShouldBe(DispatchOutcome.Duplicate);
    }

    [Fact]
    public async Task Une_fourchette_de_prix_inversee_est_refusee()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/search?q=tapis&minPrice=500&maxPrice=100");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
