using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Souqly.Testing;

namespace Saga.IntegrationTests;

// Vues JSON locales (le test ne dépend pas des classes internes des services).
public sealed record OrderView(Guid Id, string CustomerId, string Status, string? StatusReason, MoneyView Total);

public sealed record MoneyView(string Amount, string Currency);

public sealed record StockView(long ProductId, string VendorId, int OnHand, int Reserved, int Available);

public sealed record PaymentView(Guid Id, Guid OrderId, string Status, string Amount, string Currency, string? FailureReason);

public sealed record NotificationView(Guid Id, string Kind, Guid? OrderId, string Title, string Body, bool Read);

public sealed record PageView<T>(IReadOnlyList<T> Content, long TotalElements);

/// <summary>
/// Scénarios de la saga de commande (US-06, US-08, US-10 à US-13) joués sur les vrais services.
/// Chaque test crée ses propres produits : les tests sont indépendants les uns des autres.
/// </summary>
public abstract class SagaScenarios(SagaFixture platform)
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static long _nextProductId = 1_000;

    protected SagaFixture Platform => platform;

    [Fact]
    public async Task US10_commande_payee_CONFIRMED_stock_sorti_paiement_encaisse_client_et_vendeurs_notifies()
    {
        var tapis = await NewProductAsync("vendor-fatima", "Tapis berbère", 1200m, stock: 5);
        var argan = await NewProductAsync("vendor-karim", "Huile d'argan", 189.90m, stock: 10);
        var customer = NewCustomer();

        var order = await PlaceAsync(customer, (tapis, 2), (argan, 1));
        order.Status.ShouldBe("PENDING");

        var final = await WaitForStatusAsync(customer, order.Id, "CONFIRMED");
        final.Total.ShouldBe(new MoneyView("2589.90", "MAD"));   // 2 × 1 200,00 + 189,90

        (await EventuallyAsync(() => StockAsync("vendor-fatima", tapis), s => s.Reserved == 0))
            .ShouldBe(new StockView(tapis, "vendor-fatima", OnHand: 3, Reserved: 0, Available: 3));
        (await EventuallyAsync(() => StockAsync("vendor-karim", argan), s => s.Reserved == 0)).ShouldBe(new StockView(argan, "vendor-karim", OnHand: 9, Reserved: 0, Available: 9));

        var payment = await PaymentAsync(customer, order.Id);
        payment.Status.ShouldBe("CAPTURED");
        payment.Amount.ShouldBe("2589.90");

        (await EventuallyAsync(() => NotificationsAsync(customer), n => n.Any(x => x.Kind == "ORDER_CONFIRMED" && x.OrderId == order.Id)))
            .Single(x => x.OrderId == order.Id).Title.ShouldBe("Commande confirmée");
        var fatima = await EventuallyAsync(() => NotificationsAsync("vendor-fatima"), n => n.Any(x => x.OrderId == order.Id));
        fatima.Single(x => x.OrderId == order.Id).Body.ShouldContain("2 × Tapis berbère");
        fatima.Single(x => x.OrderId == order.Id).Body.ShouldNotContain("argan");   // RG-05 : seulement ses lignes
    }

    [Fact]
    public async Task US11_stock_insuffisant_REJECTED_rien_n_est_debite()
    {
        var tapis = await NewProductAsync("vendor-fatima", "Tapis rare", 900m, stock: 1);
        var customer = NewCustomer();

        var order = await PlaceAsync(customer, (tapis, 2));

        var final = await WaitForStatusAsync(customer, order.Id, "REJECTED");
        final.StatusReason.ShouldBe("OUT_OF_STOCK");
        (await StockAsync("vendor-fatima", tapis)).Reserved.ShouldBe(0);
        (await PaymentAs(customer).GetAsync($"/api/v1/payments/{order.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await EventuallyAsync(() => NotificationsAsync(customer), n => n.Any(x => x.Kind == "ORDER_REJECTED" && x.Body.Contains("plus disponible")));
    }

    [Fact]
    public async Task US12_paiement_refuse_REJECTED_et_compensation_le_stock_reserve_est_rendu()
    {
        var tapis = await NewProductAsync("vendor-fatima", "Tapis de prestige", 1200m, stock: 10);
        var customer = NewCustomer();

        var order = await PlaceAsync(customer, (tapis, 9));   // 10 800 MAD > plafond simulé de 10 000 MAD

        var final = await WaitForStatusAsync(customer, order.Id, "REJECTED");
        final.StatusReason.ShouldBe("PAYMENT_DECLINED");
        (await PaymentAsync(customer, order.Id)).Status.ShouldBe("DECLINED");

        // Compensation : les 9 unités réservées reviennent au disponible.
        var stock = await EventuallyAsync(() => StockAsync("vendor-fatima", tapis), s => s.Reserved == 0);
        stock.ShouldBe(new StockView(tapis, "vendor-fatima", OnHand: 10, Reserved: 0, Available: 10));
    }

    [Fact]
    public async Task US08_annulation_apres_confirmation_remise_en_stock_et_remboursement()
    {
        var tapis = await NewProductAsync("vendor-fatima", "Tapis kilim", 750m, stock: 4);
        var customer = NewCustomer();
        var order = await PlaceAsync(customer, (tapis, 3));
        await WaitForStatusAsync(customer, order.Id, "CONFIRMED");
        await EventuallyAsync(() => StockAsync("vendor-fatima", tapis), s => s.OnHand == 1);   // OrderConfirmed traité par Inventory

        var cancel = await OrderingAs(customer).PostAsync($"/api/v1/orders/{order.Id}/cancellation", null);
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await EventuallyAsync(() => StockAsync("vendor-fatima", tapis), s => s.OnHand == 4)).Available.ShouldBe(4);
        (await EventuallyAsync(() => PaymentAsync(customer, order.Id), p => p.Status == "REFUNDED")).Amount.ShouldBe("2250.00");
        var notes = await EventuallyAsync(() => NotificationsAsync(customer), n => n.Any(x => x.Kind == "PAYMENT_REFUNDED"));
        notes.Select(n => n.Kind).ShouldBe(["PAYMENT_REFUNDED", "ORDER_CANCELLED", "ORDER_CONFIRMED"], ignoreOrder: true);
    }

    [Fact]
    public async Task US13_US14_de_la_commande_a_la_livraison_chaque_vendeur_expedie_et_le_client_est_prevenu()
    {
        var tapis = await NewProductAsync("vendor-fatima", "Tapis à livrer", 900m, stock: 3);
        var argan = await NewProductAsync("vendor-karim", "Argan à livrer", 120m, stock: 3);
        var customer = NewCustomer();
        var order = await PlaceAsync(customer, (tapis, 1), (argan, 1));
        await WaitForStatusAsync(customer, order.Id, "CONFIRMED");

        (await OrderingVendorAs("vendor-fatima").PostAsJsonAsync($"/api/v1/vendor/orders/{order.Id}/shipment", new { carrier = "AMANA", trackingNumber = "AM123456789MA" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OrderingVendorAs("vendor-karim").PostAsJsonAsync($"/api/v1/vendor/orders/{order.Id}/shipment", new { carrier = "CTM", trackingNumber = "CTM-998877" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await WaitForStatusAsync(customer, order.Id, "SHIPPED")).Status.ShouldBe("SHIPPED");

        var shipped = await EventuallyAsync(() => NotificationsAsync(customer), n => n.Count(x => x.Kind == "ORDER_SHIPPED") == 2);
        shipped.ShouldContain(n => n.Body.Contains("AM123456789MA") && n.Body.Contains("Amana"));

        await OrderingVendorAs("vendor-fatima").PostAsync($"/api/v1/vendor/orders/{order.Id}/delivery", null);
        await OrderingVendorAs("vendor-karim").PostAsync($"/api/v1/vendor/orders/{order.Id}/delivery", null);
        await WaitForStatusAsync(customer, order.Id, "DELIVERED");
        await EventuallyAsync(() => NotificationsAsync(customer), n => n.Any(x => x.Kind == "ORDER_DELIVERED" && x.Body.Contains("entièrement livrée")));
    }

    [Fact]
    public async Task Vente_flash_20_clients_pour_5_unites_5_commandes_confirmees_15_rejetees_zero_survente()
    {
        var lampe = await NewProductAsync("vendor-karim", "Lampe en cuivre", 350m, stock: 5);
        var customers = Enumerable.Range(0, 20).Select(_ => NewCustomer()).ToList();

        var orders = await Task.WhenAll(customers.Select(async c => (Customer: c, Order: await PlaceAsync(c, (lampe, 1)))));
        var finals = await Task.WhenAll(orders.Select(o => WaitForStatusAsync(o.Customer, o.Order.Id, "CONFIRMED", "REJECTED")));

        finals.Count(o => o.Status == "CONFIRMED").ShouldBe(5);
        finals.Where(o => o.Status == "REJECTED").ShouldAllBe(o => o.StatusReason == "OUT_OF_STOCK");
        (await EventuallyAsync(() => StockAsync("vendor-karim", lampe), s => s.Reserved == 0)).ShouldBe(new StockView(lampe, "vendor-karim", OnHand: 0, Reserved: 0, Available: 0));
    }

    // ---------- outils ----------

    protected async Task<long> NewProductAsync(string vendor, string name, decimal price, int stock)
    {
        var id = Interlocked.Increment(ref _nextProductId);
        Platform.Catalog.AddProduct(id, vendor, name, price);
        var response = await InventoryAs(vendor).PutAsJsonAsync($"/api/v1/stock-items/{id}", new { onHand = stock });
        response.EnsureSuccessStatusCode();
        return id;
    }

    protected static string NewCustomer() => "customer-" + Guid.NewGuid().ToString("N")[..8];

    protected async Task<OrderView> PlaceAsync(string customer, params (long Product, int Quantity)[] lines)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new { lines = lines.Select(l => new { productId = l.Product, quantity = l.Quantity }) }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await OrderingAs(customer).SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OrderView>(Json))!;
    }

    protected async Task<OrderView> WaitForStatusAsync(string customer, Guid orderId, params string[] statuses) =>
        await EventuallyAsync(
            async () => (await OrderingAs(customer).GetFromJsonAsync<OrderView>($"/api/v1/orders/{orderId}", Json))!,
            o => statuses.Contains(o.Status));

    protected async Task<StockView> StockAsync(string vendor, long productId) =>
        (await InventoryAs(vendor).GetFromJsonAsync<StockView>($"/api/v1/stock-items/{productId}/details", Json))!;

    protected async Task<PaymentView> PaymentAsync(string customer, Guid orderId) =>
        await EventuallyAsync(
            async () =>
            {
                var response = await PaymentAs(customer).GetAsync($"/api/v1/payments/{orderId}");
                return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PaymentView>(Json) : null;
            },
            p => p is not null) ?? throw new InvalidOperationException();

    protected async Task<IReadOnlyList<NotificationView>> NotificationsAsync(string user) =>
        (await Platform.NotificationHost.CreateClient().WithToken(TestJwt.Token(user, "CUSTOMER", "VENDOR")).GetFromJsonAsync<PageView<NotificationView>>("/api/v1/notifications?size=100", Json))!.Content;

    protected HttpClient OrderingAs(string customer) => Platform.OrderingHost.CreateClient().WithToken(TestJwt.Token(customer, "CUSTOMER"));

    protected HttpClient OrderingVendorAs(string vendor) => Platform.OrderingHost.CreateClient().WithToken(TestJwt.Token(vendor, "VENDOR"));

    protected HttpClient InventoryAs(string vendor) => Platform.InventoryHost.CreateClient().WithToken(TestJwt.Token(vendor, "VENDOR"));

    protected HttpClient PaymentAs(string customer) => Platform.PaymentHost.CreateClient().WithToken(TestJwt.Token(customer, "CUSTOMER"));

    /// <summary>Réessaie jusqu'à ce que la condition soit vraie (la saga est asynchrone) ; échoue après 20 s.</summary>
    protected static async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done, int timeoutSeconds = 20)
    {
        var watch = Stopwatch.StartNew();
        T value;
        do
        {
            value = await read();
            if (done(value))
            {
                return value;
            }

            await Task.Delay(100);
        }
        while (watch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds));

        throw new TimeoutException($"Condition non atteinte en {timeoutSeconds} s ; dernière valeur : {JsonSerializer.Serialize(value)}");
    }
}
