using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Acceptance.Tests.Support;

/// <summary>Démarre la plateforme une fois pour toute la campagne de scénarios.</summary>
[Binding]
public sealed class Hooks
{
    public static Platform Platform { get; private set; } = null!;

    [BeforeTestRun]
    public static async Task StartPlatformAsync() => Platform = await Platform.StartAsync();

    [AfterTestRun]
    public static async Task StopPlatformAsync() => await Platform.DisposeAsync();
}

/// <summary>
/// État d'un scénario : qui agit, quels produits existent, la dernière commande et la dernière réponse.
/// Les noms du Gherkin (« sara », « fatima ») deviennent des identifiants uniques par scénario, si bien que
/// les scénarios ne se gênent pas même en partageant les mêmes bases.
/// </summary>
public sealed class World
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, long> CategoryIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["artisanat"] = 1,
        ["tapis"] = 2,
        ["poterie"] = 3,
        ["cuir"] = 4,
        ["cosmetique"] = 5,
        ["argan"] = 6,
        ["epicerie"] = 7,
        ["mode"] = 8,
    };

    /// <summary>Marqueur glissé dans la description des produits : le catalogue est filtré dessus.</summary>
    public string Token { get; } = Guid.NewGuid().ToString("N")[..10];

    public Platform Platform => Hooks.Platform;

    public Dictionary<string, long> Products { get; } = [];

    public HttpResponseMessage? Last { get; set; }

    public string? LastBody { get; set; }

    public Guid OrderId { get; set; }

    public string? OrderCustomer { get; set; }

    public List<(string Customer, Guid OrderId)> Orders { get; } = [];

    public string User(string name) => $"{name}-{Token}";

    public long Product(string name) => Products.TryGetValue(name, out var id) ? id : throw new InvalidOperationException($"Produit inconnu dans le scénario : {name}");

    public static long Category(string slug) => CategoryIds[slug];

    /// <summary>« 1 200,00 » (format français, espaces insécables compris) en décimal.</summary>
    public static decimal Money(string text) =>
        decimal.Parse(new string([.. text.Where(c => !char.IsWhiteSpace(c) && c != ' ')]).Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture);

    public static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public async Task RememberAsync(HttpResponseMessage response)
    {
        Last = response;
        LastBody = await response.Content.ReadAsStringAsync();
    }

    /// <summary>Réessaie jusqu'à ce que la condition soit vraie (la saga est asynchrone).</summary>
    public static async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done, int timeoutSeconds = 20)
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

// Vues JSON locales : les tests ne dépendent pas des classes internes des services.
public sealed record MoneyView(string Amount, string Currency);

public sealed record ProductView(long Id, string VendorId, long CategoryId, string Name, MoneyView Price, string Status);

public sealed record PageView<T>(IReadOnlyList<T> Content, int Page, int Size, long TotalElements, int TotalPages);

public sealed record CartView(string CustomerId, IReadOnlyList<CartLineView> Items, int TotalUnits);

public sealed record CartLineView(long ProductId, int Quantity);

public sealed record ShipmentView(string VendorId, string Carrier, string TrackingNumber, string Status);

public sealed record OrderLineView(long ProductId, string VendorId, string ProductName, int Quantity);

public sealed record VendorOrderView(Guid OrderId, string Status, IReadOnlyList<OrderLineView> Lines);

public sealed record OrderView(Guid Id, string CustomerId, string Status, string? StatusReason, MoneyView Total, IReadOnlyList<OrderLineView> Lines, IReadOnlyList<ShipmentView> Shipments);

public sealed record StockView(long ProductId, string VendorId, int OnHand, int Reserved, int Available);

public sealed record PaymentView(Guid Id, Guid OrderId, string Status, string Amount, string Currency);

public sealed record NotificationView(Guid Id, string Kind, Guid OrderId, string Title, string Body, bool Read);
