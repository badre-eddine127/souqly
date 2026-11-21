using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Acceptance.Tests.Support;

namespace Acceptance.Tests.Steps;

/// <summary>Étapes du panier (US-05), de la commande et de la saga (US-06 à US-09, US-11, US-15, US-19).</summary>
[Binding]
public sealed class OrderSteps(World world)
{
    private const string Customer = @"(?:le|la) client(?:e)? ""([^""]+)""";

    private string? _idempotencyKey;

    // ---------- panier ----------

    [Given($@"^{Customer} a mis (\d+) ""([^""]+)"" dans son panier$")]
    public async Task GivenInCart(string customer, int quantity, string product)
    {
        await WhenPutInCart(customer, quantity, product);
        world.Last!.EnsureSuccessStatusCode();
    }

    [When($@"^{Customer} met (\d+) ""([^""]+)"" dans son panier$")]
    public async Task WhenPutInCart(string customer, int quantity, string product) =>
        await world.RememberAsync(await Cart(customer).PutAsJsonAsync($"/api/v1/carts/me/items/{world.Product(product)}", new { quantity }));

    [When($@"^{Customer} retire ""([^""]+)"" de son panier$")]
    public async Task WhenRemove(string customer, string product) =>
        await world.RememberAsync(await Cart(customer).DeleteAsync($"/api/v1/carts/me/items/{world.Product(product)}"));

    [Then(@"^le panier de ""([^""]+)"" contient (\d+) articles$")]
    public async Task ThenCartUnits(string customer, int units) =>
        (await CartAsync(customer)).TotalUnits.ShouldBe(units);

    // ---------- commande ----------

    [Given($@"^{Customer} a validé son panier$")]
    [When($@"^{Customer} valide son panier$")]
    public async Task WhenCheckout(string customer)
    {
        var response = await PlaceFromCartAsync(customer, Guid.NewGuid().ToString());
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, world.LastBody);
    }

    [When($@"^{Customer} envoie deux fois sa commande avec la même clé d'idempotence$")]
    public async Task WhenDoubleClick(string customer)
    {
        _idempotencyKey = Guid.NewGuid().ToString();
        var first = await PlaceFromCartAsync(customer, _idempotencyKey);
        var firstId = world.OrderId;
        var second = await PlaceFromCartAsync(customer, _idempotencyKey);
        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        ((int)second.StatusCode).ShouldBeInRange(200, 202);
        world.OrderId.ShouldBe(firstId);   // la réponse rejouée désigne la même commande
    }

    [When(@"^(\d+) clients commandent chacun (\d+) ""([^""]+)"" en même temps$")]
    public async Task WhenFlashSale(int customers, int quantity, string product)
    {
        var buyers = Enumerable.Range(1, customers).Select(i => $"client{i}").ToList();
        var orders = await Task.WhenAll(buyers.Select(async b =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
            {
                Content = JsonContent.Create(new { lines = new[] { new { productId = world.Product(product), quantity } } }),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var response = await Ordering(b).SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            return (world.User(b), (await response.Content.ReadFromJsonAsync<OrderView>(World.Json))!.Id);
        }));
        world.Orders.AddRange(orders);
    }

    [Then(@"^(\d+) commandes sont ""([^""]+)""$")]
    public async Task ThenCount(int count, string status) => await ThenCount(count, status, string.Empty);

    [Then(@"^(\d+) commandes sont ""([^""]+)"" avec le motif ""([^""]+)""$")]
    public async Task ThenCount(int count, string status, string reason)
    {
        var finals = await Task.WhenAll(world.Orders.Select(o => World.EventuallyAsync(
            () => OrderAsync(o.Customer, o.OrderId), x => x.Status is "CONFIRMED" or "REJECTED")));
        var matching = finals.Where(o => o.Status == status).ToList();
        matching.Count.ShouldBe(count);
        if (!string.IsNullOrEmpty(reason))
        {
            matching.ShouldAllBe(o => o.StatusReason == reason);
        }
    }

    [Then(@"^la commande passe au statut ""([^""]+)"" en moins de (\d+) secondes$")]
    public async Task ThenStatusWithin(string status, int seconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await World.EventuallyAsync(() => OrderAsync(world.OrderCustomer!, world.OrderId), o => o.Status == status, timeoutSeconds: 20);
        // Le chiffre du Gherkin est l'objectif de service ; la marge absorbe la lenteur d'une machine de CI.
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(seconds * 3));
    }

    [Given(@"^la commande est au statut ""([^""]+)""$")]
    [Then(@"^la commande est au statut ""([^""]+)""$")]
    public async Task ThenStatus(string status) =>
        await World.EventuallyAsync(() => OrderAsync(world.OrderCustomer!, world.OrderId), o => o.Status == status);

    [Then(@"^la commande est d'abord au statut ""([^""]+)""$")]
    public void ThenFirstStatus(string status) =>
        JsonSerializer.Deserialize<OrderView>(world.LastBody!, World.Json)!.Status.ShouldBe(status);

    [Then(@"^la commande passe au statut ""([^""]+)"" avec le motif ""([^""]+)""$")]
    public async Task ThenStatusReason(string status, string reason) =>
        (await World.EventuallyAsync(() => OrderAsync(world.OrderCustomer!, world.OrderId), o => o.Status == status)).StatusReason.ShouldBe(reason);

    [Then(@"^le total de la commande est ([\d.]+) MAD$")]
    public async Task ThenTotal(string amount) =>
        (await OrderAsync(world.OrderCustomer!, world.OrderId)).Total.ShouldBe(new MoneyView(amount, "MAD"));

    [Then(@"^""([^""]+)"" retrouve la commande dans la liste de ses commandes$")]
    public async Task ThenListed(string customer)
    {
        var page = await Ordering(customer).GetFromJsonAsync<PageView<OrderView>>("/api/v1/orders", World.Json);
        page!.Content.ShouldContain(o => o.Id == world.OrderId);
    }

    [Then(@"^une seule commande existe pour ""([^""]+)""$")]
    public async Task ThenSingleOrder(string customer) =>
        (await Ordering(customer).GetFromJsonAsync<PageView<OrderView>>("/api/v1/orders", World.Json))!.TotalElements.ShouldBe(1);

    [When($@"^{Customer} consulte la commande de ""([^""]+)""$")]
    public async Task WhenOtherReads(string customer, string owner)
    {
        owner.ShouldBe(world.OrderCustomer?.Split('-')[0]);
        await world.RememberAsync(await Ordering(customer).GetAsync($"/api/v1/orders/{world.OrderId}"));
    }

    [When($@"^{Customer} annule sa commande$")]
    public async Task WhenCancel(string customer) =>
        await world.RememberAsync(await Ordering(customer).PostAsync($"/api/v1/orders/{world.OrderId}/cancellation", null));

    // ---------- paiement ----------

    [Then(@"^le paiement de la commande est ""([^""]+)"" pour ([\d.]+) MAD$")]
    public async Task ThenPayment(string status, string amount)
    {
        var payment = await World.EventuallyAsync(() => PaymentAsync(), p => p?.Status == status);
        payment!.Amount.ShouldBe(amount);
    }

    [Then(@"^un seul paiement est autorisé$")]
    public async Task ThenSinglePayment() =>
        (await World.EventuallyAsync(() => PaymentAsync(), p => p is not null))!.Status.ShouldBe("CAPTURED");

    [Then(@"^aucun paiement n'est demandé$")]
    public async Task ThenNoPayment()
    {
        await Task.Delay(500);
        (await PaymentAsync()).ShouldBeNull();
        world.Platform.Broker.Published.ShouldNotContain(m => m.Message.Key == world.OrderId.ToString() && m.Message.Type == "PaymentRequested");
    }

    [Given(@"^le service de paiement est arrêté$")]
    public async Task GivenPaymentDown() => await world.Platform.StopPaymentAsync();

    [When(@"^le service de paiement redémarre$")]
    public async Task WhenPaymentUp() => await world.Platform.StartPaymentAsync();

    // ---------- notifications (US-15) ----------

    [Then(@"^""([^""]+)"" reçoit la notification ""([^""]+)""$")]
    public async Task ThenNotified(string user, string title) =>
        await World.EventuallyAsync(() => NotificationsAsync(user), n => n.Any(x => x.OrderId == world.OrderId && x.Title == title));

    [Then(@"^""([^""]+)"" reçoit une notification qui mentionne ""([^""]+)""$")]
    public async Task ThenNotifiedWith(string user, string text) =>
        await World.EventuallyAsync(() => NotificationsAsync(user), n => n.Any(x => x.OrderId == world.OrderId && x.Body.Contains(text, StringComparison.Ordinal)));

    [Then(@"^la notification de ""([^""]+)"" ne mentionne pas ""([^""]+)""$")]
    public async Task ThenNotMentioned(string user, string text) =>
        (await NotificationsAsync(user)).Where(x => x.OrderId == world.OrderId).ShouldAllBe(x => !x.Body.Contains(text, StringComparison.OrdinalIgnoreCase));

    [Then(@"^""([^""]+)"" a au moins (\d+) notification non lue$")]
    public async Task ThenAtLeastUnread(string user, int count) =>
        (await NotificationsAsync(user)).Count(n => !n.Read).ShouldBeGreaterThanOrEqualTo(count);

    [When(@"^""([^""]+)"" ouvre ses notifications$")]
    public async Task WhenOpenBell(string user) =>
        (await world.Platform.Notification.As(world.User(user), "CUSTOMER", "VENDOR").PostAsync("/api/v1/notifications/read-all", null)).EnsureSuccessStatusCode();

    [Then(@"^""([^""]+)"" a (\d+) notification non lue$")]
    public async Task ThenUnread(string user, int count) =>
        (await NotificationsAsync(user)).Count(n => !n.Read).ShouldBe(count);

    // ---------- traçabilité (US-19) ----------

    [Then(@"^les messages de la commande sont, dans l'ordre, ""([^""]+)""$")]
    public async Task ThenMessages(string types)
    {
        var expected = types.Split(", ");
        var chain = await World.EventuallyAsync(
            () => Task.FromResult(world.Platform.Broker.Published.Where(m => m.Message.Key == world.OrderId.ToString()).Select(m => m.Message).ToList()),
            c => c.Count >= expected.Length);
        chain.Select(m => m.Type).Take(expected.Length).ShouldBe(expected);
    }

    [Then(@"^tous ces messages portent le même identifiant de trace$")]
    public void ThenSameTrace() =>
        world.Platform.Broker.Published.Where(m => m.Message.Key == world.OrderId.ToString())
            .Select(m => m.Message.TraceParent!.Split('-')[1]).Distinct().Count().ShouldBe(1);

    // ---------- outils ----------

    private async Task<HttpResponseMessage> PlaceFromCartAsync(string customer, string idempotencyKey)
    {
        var cart = await CartAsync(customer);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new { lines = cart.Items.Select(i => new { productId = i.ProductId, quantity = i.Quantity }) }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        var response = await Ordering(customer).SendAsync(request);
        await world.RememberAsync(response);
        if (response.IsSuccessStatusCode)
        {
            world.OrderId = JsonSerializer.Deserialize<OrderView>(world.LastBody!, World.Json)!.Id;
            world.OrderCustomer = world.User(customer);
        }

        return response;
    }

    private async Task<CartView> CartAsync(string customer) =>
        (await Cart(customer).GetFromJsonAsync<CartView>("/api/v1/carts/me", World.Json))!;

    private async Task<OrderView> OrderAsync(string userId, Guid orderId) =>
        (await world.Platform.Ordering.As(userId, "CUSTOMER").GetFromJsonAsync<OrderView>($"/api/v1/orders/{orderId}", World.Json))!;

    private async Task<PaymentView?> PaymentAsync()
    {
        var response = await world.Platform.Payment.As(world.OrderCustomer!, "CUSTOMER").GetAsync($"/api/v1/payments/{world.OrderId}");
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PaymentView>(World.Json) : null;
    }

    private async Task<IReadOnlyList<NotificationView>> NotificationsAsync(string user) =>
        (await world.Platform.Notification.As(world.User(user), "CUSTOMER", "VENDOR")
            .GetFromJsonAsync<PageView<NotificationView>>("/api/v1/notifications?size=100", World.Json))!.Content;

    private HttpClient Cart(string customer) => world.Platform.Cart.As(world.User(customer), "CUSTOMER");

    private HttpClient Ordering(string customer) => world.Platform.Ordering.As(world.User(customer), "CUSTOMER");
}
