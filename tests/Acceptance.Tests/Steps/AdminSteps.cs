using System.Net.Http.Json;
using Acceptance.Tests.Support;

namespace Acceptance.Tests.Steps;

/// <summary>
/// Espace vendeur et administration (US-16, US-17, US-18). Ces étapes passent par la passerelle, comme le site web :
/// c'est elle qui tient le journal d'audit des actions sensibles.
/// </summary>
[Binding]
public sealed class AdminSteps(World world)
{
    private const string Customer = @"(?:le|la) client(?:e)? ""([^""]+)""";
    private const string Vendor = @"(?:le|la) vendeu(?:r|se) ""([^""]+)""";
    private const string Money = @"(-?[\d   ]+,\d{2}) MAD";
    private const string Admin = "admin";

    private DashboardView? _dashboard;
    private IReadOnlyList<StockView>? _alerts;
    private PageView<AuditView>? _journal;

    private HttpClient Gateway(string user, params string[] roles) => world.Platform.Gateway.As(world.User(user), roles);

    // ---- US-16 : devenir vendeur ----

    [Given($@"^{Customer} a demandé l'ouverture de la boutique ""([^""]+)"" à ""([^""]+)""$")]
    public async Task GivenApplication(string customer, string shop, string city)
    {
        await WhenApply(customer, shop, city);
        world.Last!.EnsureSuccessStatusCode();
    }

    [When($@"^{Customer} demande l'ouverture de la boutique ""([^""]+)"" à ""([^""]+)""$")]
    public async Task WhenApply(string customer, string shop, string city) =>
        await world.RememberAsync(await Gateway(customer, "CUSTOMER")
            .PostAsJsonAsync("/api/v1/vendors", new { shopName = shop, city, description = $"Boutique de démonstration {world.Token}" }));

    [Given(@"^l'administrateur valide la demande de ""([^""]+)""$")]
    [When(@"^l'administrateur valide la demande de ""([^""]+)""$")]
    public async Task WhenAdminApproves(string customer) => await DecideAsync(Admin, "ADMIN", customer, true, null);

    [When(@"^l'administrateur refuse la demande de ""([^""]+)"" pour le motif ""([^""]+)""$")]
    public async Task WhenAdminRejects(string customer, string reason) => await DecideAsync(Admin, "ADMIN", customer, false, reason);

    [When(@"^l'administrateur refuse la demande de ""([^""]+)"" sans motif$")]
    public async Task WhenAdminRejectsWithoutReason(string customer) => await DecideAsync(Admin, "ADMIN", customer, false, null);

    [Given($@"^{Customer} essaie de valider la demande de ""([^""]+)""$")]
    [When($@"^{Customer} essaie de valider la demande de ""([^""]+)""$")]
    public async Task WhenCustomerApproves(string intruder, string customer) => await DecideAsync(intruder, "CUSTOMER", customer, true, null);

    [Then(@"^la demande de ""([^""]+)"" est ""(en attente|validée|refusée)""$")]
    public async Task ThenApplicationStatus(string customer, string status)
    {
        var expected = status switch
        {
            "en attente" => "PENDING",
            "validée" => "APPROVED",
            _ => "REJECTED",
        };
        (await ApplicationOfAsync(customer)).Status.ShouldBe(expected);
    }

    [Then(@"^""([^""]+)"" a reçu le rôle vendeur$")]
    public void ThenRoleGranted(string customer) => world.Platform.GrantedVendors.ShouldContain(world.User(customer));

    [Then(@"^""([^""]+)"" n'a pas reçu le rôle vendeur$")]
    public void ThenRoleNotGranted(string customer) => world.Platform.GrantedVendors.ShouldNotContain(world.User(customer));

    [Then(@"^la boutique ""([^""]+)"" de ""([^""]+)"" apparaît parmi les boutiques$")]
    public async Task ThenShopListed(string shop, string customer)
    {
        var shops = await world.Platform.Gateway.Anonymous().GetFromJsonAsync<List<ShopView>>("/api/v1/vendors/shops", World.Json);
        shops!.ShouldContain(s => s.VendorId == world.User(customer) && s.ShopName == shop);
    }

    // ---- US-17 : tableau de bord vendeur ----

    [When($@"^{Vendor} ouvre son tableau de bord$")]
    public async Task WhenDashboard(string vendor)
    {
        var client = Gateway(vendor, "VENDOR");
        var response = await client.GetAsync("/api/v1/vendor/dashboard");
        await world.RememberAsync(response);
        if (response.IsSuccessStatusCode)
        {
            _dashboard = await response.Content.ReadFromJsonAsync<DashboardView>(World.Json);
            _alerts = await client.GetFromJsonAsync<List<StockView>>("/api/v1/vendor/stock-alerts", World.Json);
        }
    }

    [When($@"^{Customer} essaie d'ouvrir le tableau de bord vendeur$")]
    public async Task WhenCustomerDashboard(string customer) =>
        await world.RememberAsync(await Gateway(customer, "CUSTOMER").GetAsync("/api/v1/vendor/dashboard"));

    [Then($@"^le tableau de bord indique (\d+) commandes? payées?, (\d+) unités? vendues? et {Money} de ventes du jour$")]
    public void ThenSales(int orders, int units, string amount)
    {
        _dashboard.ShouldNotBeNull();
        _dashboard.OrdersToday.ShouldBe(orders);
        _dashboard.UnitsSoldToday.ShouldBe(units);
        _dashboard.SalesToday.Amount.ShouldBe(World.Amount(World.Money(amount)));
    }

    [Then(@"^(\d+) commandes? (?:est|sont) à préparer$")]
    public void ThenToPrepare(int count) => _dashboard!.ToPrepareCount.ShouldBe(count);

    [Then(@"^""([^""]+)"" est signalé en stock bas avec (\d+) disponibles?$")]
    public void ThenLowStock(string product, int available) =>
        _alerts!.ShouldContain(a => a.ProductId == world.Product(product) && a.Available == available);

    [Then(@"^""([^""]+)"" n'est pas signalé en stock bas$")]
    public void ThenNotLowStock(string product) => _alerts!.ShouldNotContain(a => a.ProductId == world.Product(product));

    // ---- US-18 : journal d'audit ----

    [When(@"^l'administrateur consulte le journal d'audit$")]
    public async Task WhenAdminReadsJournal()
    {
        // Les autres scénarios écrivent aussi dans le journal : on lit les entrées des acteurs de ce scénario.
        var response = await Gateway(Admin, "ADMIN").GetAsync($"/api/v1/admin/audit?user={world.Token}&size=100");
        await world.RememberAsync(response);
        _journal = await response.Content.ReadFromJsonAsync<PageView<AuditView>>(World.Json);
    }

    [When($@"^{Vendor} essaie de consulter le journal d'audit$")]
    public async Task WhenVendorReadsJournal(string vendor) =>
        await world.RememberAsync(await Gateway(vendor, "VENDOR").GetAsync("/api/v1/admin/audit"));

    [Then(@"^le journal indique que ""([^""]+)"" a validé la demande de ""([^""]+)"" avec le statut (\d+)$")]
    public async Task ThenJournalApproval(string actor, string customer, int status)
    {
        var application = await ApplicationOfAsync(customer);
        var entry = _journal!.Content
            .Where(e => e.UserId == world.User(actor) && e.Path == $"/api/v1/vendors/{application.Id}/approval")
            .ShouldHaveSingleItem();
        entry.Method.ShouldBe("POST");
        entry.Status.ShouldBe(status);
        entry.At.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Then(@"^le journal indique que ""([^""]+)"" a demandé une boutique$")]
    public void ThenJournalApplication(string customer) =>
        _journal!.Content.ShouldContain(e => e.UserId == world.User(customer) && e.Method == "POST" && e.Path == "/api/v1/vendors");

    [Then(@"^le journal ne contient ni e-mail ni jeton$")]
    public void ThenNoPersonalData()
    {
        world.LastBody.ShouldNotBeNull();
        world.LastBody.ShouldNotContain("@example.test");
        world.LastBody.ShouldNotContain("eyJ");   // début de tout JWT encodé
    }

    [Then(@"^le journal ne contient aucune lecture$")]
    public void ThenNoReads() => _journal!.Content.ShouldAllBe(e => e.Method != "GET");

    private async Task DecideAsync(string actor, string role, string customer, bool approved, string? reason)
    {
        var application = await ApplicationOfAsync(customer);
        await world.RememberAsync(await Gateway(actor, role)
            .PostAsJsonAsync($"/api/v1/vendors/{application.Id}/approval", new { approved, reason }));
    }

    private async Task<ApplicationView> ApplicationOfAsync(string customer) =>
        (await Gateway(customer, "CUSTOMER").GetFromJsonAsync<ApplicationView>("/api/v1/vendors/me", World.Json))!;
}
