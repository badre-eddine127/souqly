using System.Net.Http.Json;
using Acceptance.Tests.Support;

namespace Acceptance.Tests.Steps;

/// <summary>
/// US-04 : recherche en texte libre, par la passerelle comme le site web. Le service Search est alimenté par les
/// événements de Catalog : l'index est cohérent « à terme », les vérifications attendent donc quelques instants.
/// Les autres scénarios partagent l'index : on ne regarde que les produits créés par ce scénario.
/// </summary>
[Binding]
public sealed class SearchSteps(World world, CatalogSteps catalog)
{
    private const string Vendor = @"(?:le|la) vendeu(?:r|se) ""([^""]+)""";
    private const string Money = @"(-?[\d   ]+,\d{2}) MAD";

    private string _query = string.Empty;
    private List<SearchHitView> _hits = [];
    private SearchView? _last;

    [Given($@"^{Vendor} a publié ""([^""]+)"" à {Money} dans la catégorie ""([^""]+)""$")]
    public Task GivenPublished(string vendor, string name, string price, string category) =>
        catalog.CreateProductAsync(vendor, name, World.Money(price), category, publish: true, expectSuccess: true);

    [When(@"^un visiteur recherche ""([^""]*)""$")]
    public void WhenSearch(string text) => _query = $"q={Uri.EscapeDataString(text)}";

    [When($@"^un visiteur recherche ""([^""]*)"" entre {Money} et {Money}$")]
    public void WhenSearchPrice(string text, string min, string max) =>
        _query = $"q={Uri.EscapeDataString(text)}&minPrice={World.Amount(World.Money(min))}&maxPrice={World.Amount(World.Money(max))}";

    [When(@"^un visiteur recherche ""([^""]*)"" dans la catégorie ""([^""]+)""$")]
    public void WhenSearchCategory(string text, string category) =>
        _query = $"q={Uri.EscapeDataString(text)}&category={World.Category(category)}";

    [Then(@"^le premier résultat est ""([^""]+)""$")]
    public async Task ThenFirst(string name) =>
        await EventuallyAsync(() => _hits.FirstOrDefault()?.ProductId == world.Product(name), $"« {name} » en tête");

    [Then(@"^les résultats sont, dans l'ordre : (.+)$")]
    public async Task ThenExactly(string list)
    {
        var expected = Names(list).Select(world.Product).ToList();
        await EventuallyAsync(() => _hits.Select(h => h.ProductId).SequenceEqual(expected), $"exactement {list}");
    }

    [Then(@"^""([^""]+)"" fait partie des résultats$")]
    public async Task ThenContains(string name) =>
        await EventuallyAsync(() => _hits.Any(h => h.ProductId == world.Product(name)), $"« {name} » trouvé");

    [Then(@"^""([^""]+)"" ne fait pas partie des résultats$")]
    public async Task ThenNotContains(string name) =>
        await EventuallyAsync(() => _hits.All(h => h.ProductId != world.Product(name)), $"« {name} » absent");

    [Then(@"^le résultat ""([^""]+)"" coûte " + Money + "$")]
    public async Task ThenPrice(string name, string price) =>
        await EventuallyAsync(
            () => _hits.Any(h => h.ProductId == world.Product(name) && h.Price.Amount == World.Amount(World.Money(price))),
            $"« {name} » à {price} MAD");

    [Then(@"^la facette ""([^""]+)"" compte (\d+) résultats? de ce scénario$")]
    public async Task ThenFacet(string category, int count) =>
        await EventuallyAsync(() => _last!.Facets.Categories.Any(f => f.CategoryId == World.Category(category) && f.Count >= count),
            $"facette {category} ≥ {count}");

    private static IEnumerable<string> Names(string list) =>
        list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim('"'));

    /// <summary>Relance la recherche jusqu'à ce que la condition soit vraie (5 s au plus) : l'index suit Catalog de près, pas instantanément.</summary>
    private async Task EventuallyAsync(Func<bool> condition, string what)
    {
        var mine = world.Products.Values.ToHashSet();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            _last = await world.Platform.Gateway.Anonymous().GetFromJsonAsync<SearchView>($"/api/v1/search?{_query}&size=100", World.Json);
            _hits = _last!.Content.Where(h => mine.Contains(h.ProductId)).ToList();
            if (condition())
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new ShouldAssertException($"Recherche « {_query} » : attendu {what}, obtenu {string.Join(", ", _hits.Select(h => h.Name))}");
            }

            await Task.Delay(100);
        }
    }
}
