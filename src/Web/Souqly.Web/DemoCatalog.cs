using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;

namespace Souqly.Web;

/// <summary>Un vendeur de démonstration : compte Keycloak et nom affiché sur le site.</summary>
public sealed record DemoVendor(string Username, string DisplayName, string City);

/// <summary>Annuaire des vendeurs : identifiant Keycloak → nom de l'atelier et ville (affichage seulement).</summary>
public sealed class VendorDirectory
{
    private readonly ConcurrentDictionary<string, VendorView> _vendors = new();

    public IReadOnlyDictionary<string, VendorView> All => _vendors;

    public void Add(string vendorId, string name, string city) => _vendors[vendorId] = new VendorView(name, city);

    public sealed record VendorView(string Name, string City);
}

/// <summary>
/// Données de démonstration (environnement de développement seulement) : au démarrage, les vendeurs fatima et
/// karim publient une vingtaine de produits avec leur stock, en passant par l'API comme un vrai vendeur.
/// Idempotent : un produit dont la référence (SKU) existe déjà n'est pas recréé.
/// </summary>
public sealed class DemoCatalogSeeder(
    IHttpClientFactory httpClients,
    IConfiguration configuration,
    VendorDirectory directory,
    ILogger<DemoCatalogSeeder> logger) : BackgroundService
{
    public const string HttpClientName = "demo-seed";

    internal static readonly DemoVendor[] Vendors =
    [
        new("fatima", "Atelier Fatima", "Marrakech"),
        new("karim", "Coopérative Karim", "Essaouira"),
    ];

    // (vendeur, SKU, catégorie, nom, prix, stock, description)
    internal static readonly (string Vendor, string Sku, int Category, string Name, decimal Price, int Stock, string Description)[] Products =
    [
        ("fatima", "DEMO-TAP-01", 2, "Tapis berbère Beni Ouarain", 2890m, 4, "Laine vierge tissée à la main dans le Moyen Atlas, motifs losanges traditionnels. 150 × 230 cm."),
        ("fatima", "DEMO-TAP-02", 2, "Kilim de l'Atlas aux motifs géométriques", 1450m, 6, "Tissage plat réversible aux couleurs naturelles, idéal en salon ou en chambre. 120 × 180 cm."),
        ("fatima", "DEMO-TAP-03", 2, "Tapis Azilal coloré", 1990m, 3, "Pièce unique aux couleurs vives, laine et coton recyclé. 140 × 200 cm."),
        ("fatima", "DEMO-POT-01", 3, "Tajine de Safi en terre cuite", 240m, 15, "Tajine émaillé à la main, résistant au feu doux. Diamètre 30 cm."),
        ("fatima", "DEMO-POT-02", 3, "Assiettes en céramique de Fès (lot de 4)", 420m, 10, "Céramique émaillée, motifs bleus de Fès, passe au lave-vaisselle."),
        ("fatima", "DEMO-POT-03", 3, "Bol en terre cuite tourné main", 95m, 25, "Terre cuite de Safi, finition mate. Diamètre 15 cm."),
        ("fatima", "DEMO-ART-01", 1, "Fontaine murale en zellige", 3200m, 2, "Zellige de Fès taillé et posé à la main, vasque en marbre."),
        ("fatima", "DEMO-ART-02", 1, "Lanterne marocaine en laiton ciselé", 560m, 8, "Laiton ajouré à la main, projette des motifs étoilés. Hauteur 45 cm."),
        ("fatima", "DEMO-MOD-01", 8, "Panier en osier tressé avec anses en cuir", 320m, 12, "Panier de souk tressé à Marrakech, anses en cuir naturel."),
        ("fatima", "DEMO-MOD-02", 8, "Sac cabas en palmier doum", 260m, 14, "Fibre de palmier tressée, doublure en coton. Le compagnon du marché."),
        ("karim", "DEMO-CUI-01", 4, "Cartable en cuir tanné de Fès", 890m, 5, "Cuir de vachette tanné végétal aux tanneries Chouara, coutures sellier."),
        ("karim", "DEMO-CUI-02", 4, "Babouches en cuir jaune", 220m, 20, "Babouches traditionnelles cousues main, semelle cuir. Pointures 36 à 45."),
        ("karim", "DEMO-CUI-03", 4, "Pouf en cuir brodé", 650m, 7, "Cuir de chèvre brodé de fils de soie, livré non rembourré."),
        ("karim", "DEMO-ARG-01", 6, "Huile d'argan cosmétique bio 100 ml", 189.90m, 30, "Pressée à froid par une coopérative de femmes près d'Essaouira. Certifiée bio."),
        ("karim", "DEMO-ARG-02", 6, "Huile d'argan alimentaire torréfiée 250 ml", 160m, 24, "Graines torréfiées, goût de noisette, pour l'amlou et les salades."),
        ("karim", "DEMO-COS-01", 5, "Savon noir beldi à l'eucalyptus", 45m, 40, "Savon traditionnel du hammam à l'huile d'olive. Pot de 250 g."),
        ("karim", "DEMO-COS-02", 5, "Savons artisanaux à la rose de Kelaat M'Gouna (lot de 3)", 75m, 35, "Savons saponifiés à froid, eau de rose de la vallée du Dadès."),
        ("karim", "DEMO-EPI-01", 7, "Miel de thym de l'Atlas 500 g", 140m, 18, "Miel brut récolté à 2 000 m d'altitude, non chauffé."),
        ("karim", "DEMO-EPI-02", 7, "Ras el hanout du souk (coffret d'épices)", 85m, 22, "Mélange de 27 épices moulues à la main, safran, cumin et cannelle."),
        ("karim", "DEMO-EPI-03", 7, "Thé vert à la menthe et ses verres gravés", 210m, 16, "Gunpowder, menthe nanah séchée et six verres à thé gravés."),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await WaitForGatewayAsync(stoppingToken);
            foreach (var vendor in Vendors)
            {
                await SeedVendorAsync(vendor, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // arrêt de l'application pendant le remplissage
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException)
        {
            // Le site fonctionne sans les données de démonstration : on le signale sans planter.
            logger.LogWarning(ex, "Données de démonstration non créées.");
        }
    }

    private HttpClient Gateway()
    {
        var client = httpClients.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(configuration["Gateway:Address"] ?? "http://localhost:8080");
        return client;
    }

    private async Task WaitForGatewayAsync(CancellationToken ct)
    {
        using var client = Gateway();
        for (var attempt = 0; attempt < 90; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(new Uri("/api/v1/categories", UriKind.Relative), ct);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // passerelle pas encore prête
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        throw new TimeoutException("La passerelle ne répond pas.");
    }

    private async Task SeedVendorAsync(DemoVendor vendor, CancellationToken ct)
    {
        var token = await LoginAsync(vendor.Username, ct);
        var vendorId = SessionEndpoints.Claim(token, "sub") ?? throw new InvalidOperationException("Jeton sans sujet.");
        directory.Add(vendorId, vendor.DisplayName, vendor.City);

        using var client = Gateway();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var existing = await client.GetFromJsonAsync<Page>(new Uri("/api/v1/vendor/products?size=100", UriKind.Relative), ct);
        var skus = existing?.Content.Select(p => p.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var created = 0;
        foreach (var p in Products.Where(p => p.Vendor == vendor.Username && !skus.Contains(p.Sku)))
        {
            using var response = await client.PostAsJsonAsync(new Uri("/api/v1/products", UriKind.Relative), new
            {
                categoryId = p.Category,
                sku = p.Sku,
                name = p.Name,
                description = p.Description,
                price = new { amount = p.Price.ToString("0.00", CultureInfo.InvariantCulture), currency = "MAD" },
                publish = true,
            }, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Produit de démonstration {Sku} refusé : {Status}", p.Sku, (int)response.StatusCode);
                continue;
            }

            var product = await response.Content.ReadFromJsonAsync<ProductRef>(ct);
            using var stock = await client.PutAsJsonAsync(new Uri($"/api/v1/stock-items/{product!.Id}", UriKind.Relative), new { onHand = p.Stock }, ct);
            created++;
        }

        logger.LogInformation("Démonstration : {Created} produit(s) créé(s) pour {Vendor}.", created, vendor.Username);
    }

    private async Task<string> LoginAsync(string username, CancellationToken ct)
    {
        var tokenEndpoint = configuration["Oidc:Authority"]!.TrimEnd('/') + "/protocol/openid-connect/token";
        using var client = httpClients.CreateClient(HttpClientName);
        using var response = await client.PostAsync(new Uri(tokenEndpoint), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = configuration["Demo:ClientId"] ?? "souqly-dev",
            ["username"] = username,
            ["password"] = configuration["Demo:Password"] ?? throw new InvalidOperationException("Demo:Password manquant."),
        }), ct);
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        return tokens!.AccessToken;
    }

    private sealed record Page(IReadOnlyList<ProductRef> Content);

    private sealed record ProductRef(long Id, string Sku);

    private sealed record TokenResponse([property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken);
}
