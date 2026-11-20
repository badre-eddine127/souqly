using System.Net.Http.Json;
using System.Text.Json;
using Acceptance.Tests.Support;

namespace Acceptance.Tests.Steps;

/// <summary>Étapes du stock (US-10, US-11), de l'expédition et de la livraison (US-13, US-14), et réponses HTTP.</summary>
[Binding]
public sealed class StockAndShippingSteps(World world)
{
    private const string Vendor = @"(?:le|la) vendeu(?:r|se) ""([^""]+)""";

    private string? _lastVendor;
    private PageView<VendorOrderView>? _vendorOrders;

    // ---------- stock ----------

    [Given($@"^{Vendor} a déclaré (\d+) unités de ""([^""]+)""$")]
    public async Task GivenDeclared(string vendor, int units, string product)
    {
        await WhenDeclare(vendor, units, product);
        world.Last!.EnsureSuccessStatusCode();
    }

    [When($@"^{Vendor} déclare (\d+) unités de ""([^""]+)""$")]
    public async Task WhenDeclare(string vendor, int units, string product)
    {
        _lastVendor = vendor;
        await world.RememberAsync(await Inventory(vendor).PutAsJsonAsync($"/api/v1/stock-items/{world.Product(product)}", new { onHand = units }));
    }

    [Given(@"^le stock de ""([^""]+)"" est (\d+)$")]
    public async Task GivenStockIs(string product, int units)
    {
        var owner = await OwnerAsync(product);
        (await world.Platform.Inventory.As(owner, "VENDOR").PutAsJsonAsync($"/api/v1/stock-items/{world.Product(product)}", new { onHand = units }))
            .EnsureSuccessStatusCode();
    }

    [When($@"^{Vendor} ajuste le stock de ""([^""]+)"" de (-?\d+) pour le motif ""([^""]+)""$")]
    public async Task WhenAdjust(string vendor, string product, int delta, string reason)
    {
        _lastVendor = vendor;
        await world.RememberAsync(await Inventory(vendor).PostAsJsonAsync($"/api/v1/stock-items/{world.Product(product)}/adjustments", new { delta, reason }));
    }

    [Then(@"^le stock disponible de ""([^""]+)"" vaut (?:de nouveau )?(\d+)$")]
    public async Task ThenAvailable(string product, int available) =>
        await World.EventuallyAsync(() => StockAsync(product), s => s.Available == available && s.Reserved == 0);

    [Then(@"^le stock de ""([^""]+)"" vaut (\d+) en main, (\d+) réservé et (\d+) disponible$")]
    public async Task ThenStock(string product, int onHand, int reserved, int available) =>
        await World.EventuallyAsync(() => StockAsync(product), s => s.OnHand == onHand && s.Reserved == reserved && s.Available == available);

    // ---------- expédition et livraison ----------

    [Given($@"^{Vendor} a expédié sa part avec ""([^""]+)"" et le suivi ""([^""]+)""$")]
    public async Task GivenShipped(string vendor, string carrier, string tracking)
    {
        await WhenShip(vendor, carrier, tracking);
        world.Last!.EnsureSuccessStatusCode();
    }

    [When($@"^{Vendor} expédie sa part avec ""([^""]+)"" et le suivi ""([^""]+)""$")]
    public async Task WhenShip(string vendor, string carrier, string tracking) =>
        await world.RememberAsync(await VendorOrdering(vendor).PostAsJsonAsync($"/api/v1/vendor/orders/{world.OrderId}/shipment", new { carrier, trackingNumber = tracking }));

    [When($@"^{Vendor} confirme la livraison$")]
    public async Task WhenDelivered(string vendor) =>
        await world.RememberAsync(await VendorOrdering(vendor).PostAsync($"/api/v1/vendor/orders/{world.OrderId}/delivery", null));

    [When($@"^{Vendor} consulte ses commandes à préparer$")]
    public async Task WhenVendorLists(string vendor) =>
        _vendorOrders = await VendorOrdering(vendor).GetFromJsonAsync<PageView<VendorOrderView>>("/api/v1/vendor/orders?status=CONFIRMED", World.Json);

    [Then(@"^il voit la commande avec seulement ""([^""]+)""$")]
    public void ThenVendorSees(string product)
    {
        var order = _vendorOrders!.Content.Single(o => o.OrderId == world.OrderId);
        order.Lines.Select(l => l.ProductName).ShouldBe([product]);
    }

    [Then(@"^""([^""]+)"" voit le colis ""([^""]+)"" de ""([^""]+)"" au statut ""([^""]+)""$")]
    public async Task ThenParcel(string customer, string tracking, string carrier, string status)
    {
        var order = await world.Platform.Ordering.As(world.User(customer), "CUSTOMER").GetFromJsonAsync<OrderView>($"/api/v1/orders/{world.OrderId}", World.Json);
        order!.Shipments.ShouldContain(s => s.TrackingNumber == tracking && s.Carrier == carrier && s.Status == status);
    }

    // ---------- réponses HTTP ----------

    [Then(@"^la réponse a le statut (\d+)$")]
    public void ThenStatusCode(int code) => ((int)world.Last!.StatusCode).ShouldBe(code, world.LastBody);

    [Then(@"^la réponse est un ProblemDetail$")]
    public void ThenProblem()
    {
        world.Last!.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(world.LastBody!);
        json.RootElement.TryGetProperty("title", out _).ShouldBeTrue();
        json.RootElement.TryGetProperty("status", out _).ShouldBeTrue();
    }

    // ---------- outils ----------

    private async Task<StockView> StockAsync(string product) =>
        (await world.Platform.Inventory.As(await OwnerAsync(product), "VENDOR")
            .GetFromJsonAsync<StockView>($"/api/v1/stock-items/{world.Product(product)}/details", World.Json))!;

    private async Task<string> OwnerAsync(string product) =>
        (await world.Platform.Catalog.Anonymous().GetFromJsonAsync<ProductView>($"/api/v1/products/{world.Product(product)}", World.Json))!.VendorId;

    private HttpClient Inventory(string vendor) => world.Platform.Inventory.As(world.User(vendor), "VENDOR");

    private HttpClient VendorOrdering(string vendor) => world.Platform.Ordering.As(world.User(vendor), "VENDOR");
}
