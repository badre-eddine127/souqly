using System.Net.Http.Json;
using Acceptance.Tests.Support;

namespace Acceptance.Tests.Steps;

/// <summary>US-12 : le vendeur est alerté quand un produit passe sous son seuil de stock (événement LowStock → notification).</summary>
[Binding]
public sealed class StockAlertSteps(World world)
{
    private const string Vendor = @"(?:le|la) vendeu(?:r|se) ""([^""]+)""";

    private HttpClient Inventory(string vendor) => world.Platform.Inventory.As(world.User(vendor), "VENDOR");

    [Given($@"^{Vendor} a fixé l'alerte de stock de ""([^""]+)"" à (\d+)$")]
    public async Task GivenThreshold(string vendor, string product, int threshold)
    {
        var id = world.Product(product);
        var stock = (await Inventory(vendor).GetFromJsonAsync<StockView>($"/api/v1/stock-items/{id}/details", World.Json))!;
        (await Inventory(vendor).PutAsJsonAsync($"/api/v1/stock-items/{id}", new { onHand = stock.OnHand, lowStockThreshold = threshold }))
            .EnsureSuccessStatusCode();
    }

    [Then(@"^""([^""]+)"" est alertée? que ""([^""]+)"" est en stock bas avec (\d+) disponibles?$")]
    public async Task ThenLowStock(string vendor, string product, int available) =>
        await World.EventuallyAsync(() => AlertsAsync(vendor, product),
            alerts => alerts.Any(a => a.Kind == "LOW_STOCK" && a.Body.Contains($"Plus que {available} unité", StringComparison.Ordinal)),
            timeoutSeconds: 10);

    [Then(@"^""([^""]+)"" est alertée? de la rupture de ""([^""]+)""$")]
    public async Task ThenOutOfStock(string vendor, string product) =>
        await World.EventuallyAsync(() => AlertsAsync(vendor, product), alerts => alerts.Any(a => a.Kind == "OUT_OF_STOCK"), timeoutSeconds: 10);

    [Then(@"^l'alerte de ""([^""]+)"" nomme le produit ""([^""]+)""$")]
    public async Task ThenNamed(string vendor, string product) =>
        (await AlertsAsync(vendor, product)).ShouldContain(a => a.Body.Contains($"« {product} »", StringComparison.Ordinal));

    [Then(@"^""([^""]+)"" a reçu exactement (\d+) alertes? de stock pour ""([^""]+)""$")]
    public async Task ThenCount(string vendor, int count, string product)
    {
        await Task.Delay(1000);   // laisse passer d'éventuels messages en retard avant de compter
        (await AlertsAsync(vendor, product)).Count.ShouldBe(count);
    }

    private async Task<IReadOnlyList<NotificationView>> AlertsAsync(string vendor, string product)
    {
        var page = await world.Platform.Notification.As(world.User(vendor), "VENDOR")
            .GetFromJsonAsync<PageView<NotificationView>>("/api/v1/notifications?size=100", World.Json);
        var id = world.Product(product);
        return page!.Content.Where(n => n.ProductId == id && n.Kind is "LOW_STOCK" or "OUT_OF_STOCK").ToList();
    }
}
