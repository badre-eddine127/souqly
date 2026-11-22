using System.Net.Http.Json;
using Acceptance.Tests.Support;

namespace Acceptance.Tests.Steps;

/// <summary>Étapes du catalogue (US-01, US-02, US-03) et de mise en place des produits.</summary>
[Binding]
public sealed class CatalogSteps(World world)
{
    private const string Vendor = @"(?:le|la) vendeu(?:r|se) ""([^""]+)""";
    private const string Money = @"(-?[\d   ]+,\d{2}) MAD";

    private readonly Dictionary<string, (long Category, string Sku, bool Published)> _details = [];
    private PageView<ProductView>? _page;

    [Given($@"^{Vendor} publie le produit ""([^""]+)"" à {Money} dans la catégorie ""([^""]+)""$")]
    public async Task GivenPublishedProduct(string vendor, string name, string price, string category) =>
        await CreateProductAsync(vendor, name, World.Money(price), category, publish: true, expectSuccess: true);

    [Given($@"^{Vendor} a un brouillon ""([^""]+)"" à {Money} dans la catégorie ""([^""]+)""$")]
    public async Task GivenDraft(string vendor, string name, string price, string category) =>
        await CreateProductAsync(vendor, name, World.Money(price), category, publish: false, expectSuccess: true);

    [Given($@"^{Vendor} publie les produits suivants :$")]
    public async Task GivenProducts(string vendor, DataTable table)
    {
        foreach (var row in table.Rows)
        {
            await CreateProductAsync(vendor, row["nom"], World.Money(row["prix"]), row["catégorie"], publish: true, expectSuccess: true);
        }
    }

    /// <summary>Produit publié dans le vrai Catalog et stock déclaré dans Inventory : prêt à être commandé.</summary>
    [Given($@"^{Vendor} propose le produit ""([^""]+)"" à {Money} avec (\d+) unités en stock$")]
    public async Task GivenProductInStock(string vendor, string name, string price, int stock)
    {
        await CreateProductAsync(vendor, name, World.Money(price), "artisanat", publish: true, expectSuccess: true);
        var response = await world.Platform.Inventory.As(world.User(vendor), "VENDOR")
            .PutAsJsonAsync($"/api/v1/stock-items/{world.Product(name)}", new { onHand = stock });
        response.EnsureSuccessStatusCode();
    }

    [When($@"^{Vendor} crée le produit ""([^""]+)"" à {Money} dans la catégorie ""([^""]+)""$")]
    public async Task WhenVendorCreates(string vendor, string name, string price, string category) =>
        await CreateProductAsync(vendor, name, World.Money(price), category, publish: true, expectSuccess: false);

    [When(@"^(?:le|la) client(?:e)? ""([^""]+)"" essaie de créer le produit ""([^""]+)"" à " + Money + @" dans la catégorie ""([^""]+)""$")]
    public async Task WhenCustomerCreates(string customer, string name, string price, string category) =>
        await world.RememberAsync(await world.Platform.Catalog.As(world.User(customer), "CUSTOMER")
            .PostAsJsonAsync("/api/v1/products", Body(name, World.Money(price), World.Category(category), "SKU-" + Guid.NewGuid().ToString("N")[..8], true)));

    [When($@"^{Vendor} change le prix de ""([^""]+)"" à {Money}$")]
    public async Task WhenPriceChanges(string vendor, string name, string price)
    {
        var (category, sku, published) = _details[name];
        await world.RememberAsync(await world.Platform.Catalog.As(world.User(vendor), "VENDOR")
            .PutAsJsonAsync($"/api/v1/products/{world.Product(name)}", Body(name, World.Money(price), category, sku, published)));
    }

    [When($@"^{Vendor} archive le produit ""([^""]+)""$")]
    public async Task WhenArchive(string vendor, string name) =>
        await world.RememberAsync(await world.Platform.Catalog.As(world.User(vendor), "VENDOR").DeleteAsync($"/api/v1/products/{world.Product(name)}"));

    [When(@"^un visiteur parcourt la catégorie ""([^""]+)"" triée par ""([^""]+)"", (\d+) produits par page$")]
    public async Task WhenBrowseFirst(string category, string sort, int size) => await WhenBrowse(1, category, sort, size);

    [When(@"^un visiteur parcourt la page (\d+) de la catégorie ""([^""]+)"" triée par ""([^""]+)"", (\d+) produits par page$")]
    public async Task WhenBrowse(int page, string category, string sort, int size)
    {
        var apiSort = sort switch
        {
            "prix croissant" => "price,asc",
            "prix décroissant" => "price,desc",
            "nom" => "name,asc",
            _ => throw new ArgumentException(sort),
        };
        var number = page - 1;
        _page = await world.Platform.Catalog.Anonymous().GetFromJsonAsync<PageView<ProductView>>(
            $"/api/v1/products?category={World.Category(category)}&sort={apiSort}&size={size}&page={number}&q={world.Token}", World.Json);
    }

    [Then(@"^la page affiche dans l'ordre ""([^""]+)""$")]
    public void ThenPageOrder(string names) =>
        _page!.Content.Select(p => p.Name).ShouldBe(names.Split(", "));

    [Then(@"^la page ne contient pas ""([^""]+)""$")]
    public void ThenPageExcludes(string name) => _page!.Content.ShouldNotContain(p => p.Name == name);

    [Then(@"^le catalogue annonce (\d+) produits sur (\d+) pages$")]
    public void ThenTotals(int total, int pages)
    {
        _page!.TotalElements.ShouldBe(total);
        _page.TotalPages.ShouldBe(pages);
    }

    [When(@"^un visiteur ouvre la fiche du produit ""([^""]+)""$")]
    public async Task WhenVisitorOpens(string name) =>
        await world.RememberAsync(await world.Platform.Catalog.Anonymous().GetAsync($"/api/v1/products/{world.Product(name)}"));

    [When($@"^{Vendor} ouvre la fiche du produit ""([^""]+)""$")]
    public async Task WhenVendorOpens(string vendor, string name) =>
        await world.RememberAsync(await world.Platform.Catalog.As(world.User(vendor), "VENDOR").GetAsync($"/api/v1/products/{world.Product(name)}"));

    [When(@"^un visiteur ouvre la fiche d'un produit qui n'existe pas$")]
    public async Task WhenMissing() =>
        await world.RememberAsync(await world.Platform.Catalog.Anonymous().GetAsync("/api/v1/products/987654321"));

    [Then(@"^la fiche indique le prix ""([^""]+)"" MAD et le vendeur ""([^""]+)""$")]
    public void ThenSheet(string amount, string vendor)
    {
        var product = System.Text.Json.JsonSerializer.Deserialize<ProductView>(world.LastBody!, World.Json)!;
        product.Price.ShouldBe(new MoneyView(amount, "MAD"));
        product.VendorId.ShouldBe(world.User(vendor));
    }

    [Then(@"^un visiteur trouve ""([^""]+)"" à " + Money + " dans le catalogue$")]
    public async Task ThenVisitorFinds(string name, string price)
    {
        var page = await SearchAsync();
        page.Content.ShouldContain(p => p.Name == name && p.Price.Amount == World.Amount(World.Money(price)));
    }

    [Then(@"^un visiteur ne trouve plus ""([^""]+)"" dans le catalogue$")]
    public async Task ThenVisitorMisses(string name)
    {
        (await SearchAsync()).Content.ShouldNotContain(p => p.Name == name);
        (await world.Platform.Catalog.Anonymous().GetAsync($"/api/v1/products/{world.Product(name)}")).StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

    private async Task<PageView<ProductView>> SearchAsync() =>
        (await world.Platform.Catalog.Anonymous().GetFromJsonAsync<PageView<ProductView>>($"/api/v1/products?q={world.Token}&size=100", World.Json))!;

    internal async Task CreateProductAsync(string vendor, string name, decimal price, string category, bool publish, bool expectSuccess)
    {
        var sku = "SKU-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var response = await world.Platform.Catalog.As(world.User(vendor), "VENDOR")
            .PostAsJsonAsync("/api/v1/products", Body(name, price, World.Category(category), sku, publish));
        await world.RememberAsync(response);
        if (expectSuccess)
        {
            response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, world.LastBody);
        }

        if (response.IsSuccessStatusCode)
        {
            var created = System.Text.Json.JsonSerializer.Deserialize<ProductView>(world.LastBody!, World.Json)!;
            world.Products[name] = created.Id;
            _details[name] = (World.Category(category), sku, publish);
        }
    }

    private object Body(string name, decimal price, long category, string sku, bool publish) => new
    {
        categoryId = category,
        sku,
        name,
        description = $"Produit de recette {world.Token}",
        price = new { amount = World.Amount(price), currency = "MAD" },
        publish,
    };
}
