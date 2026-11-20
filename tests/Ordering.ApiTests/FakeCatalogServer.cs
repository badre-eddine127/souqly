using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Ordering.ApiTests;

/// <summary>
/// Faux Catalog + faux Keycloak sur un vrai port HTTP : on teste le VRAI client HTTP d'Ordering
/// (jeton client credentials, Polly, disjoncteur), pas un substitut en mémoire.
/// </summary>
public sealed class FakeCatalogServer : IAsyncLifetime
{
    private WebApplication? _app;

    /// <summary>Mode courant : "ok", "down" (500), "slow" (10 s).</summary>
    public string Mode { get; set; } = "ok";

    private int _priceCalls;
    private int _tokenCalls;

    public int PriceCalls => _priceCalls;

    public int TokenCalls => _tokenCalls;

    public string? LastAuthorization { get; private set; }

    public string Address { get; private set; } = string.Empty;

    private static readonly Dictionary<long, object> Products = new()
    {
        [1] = new { productId = 1, vendorId = "vendor-fatima", name = "Tapis berbère", price = new { amount = "1200.00", currency = "MAD" }, purchasable = true },
        [2] = new { productId = 2, vendorId = "vendor-karim", name = "Huile d'argan 250 ml", price = new { amount = "189.90", currency = "MAD" }, purchasable = true },
        [3] = new { productId = 3, vendorId = "vendor-karim", name = "Tajine (brouillon)", price = new { amount = "240.00", currency = "MAD" }, purchasable = false },
    };

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.MapPost("/token", () =>
        {
            Interlocked.Increment(ref _tokenCalls);
            return Results.Json(new { access_token = "service-token-123", expires_in = 300, token_type = "Bearer" });
        });

        _app.MapGet("/internal/v1/products/prices", async (string ids, HttpContext http) =>
        {
            Interlocked.Increment(ref _priceCalls);
            LastAuthorization = http.Request.Headers.Authorization.ToString();
            switch (Mode)
            {
                case "down":
                    return Results.StatusCode(500);
                case "slow":
                    await Task.Delay(TimeSpan.FromSeconds(10), http.RequestAborted);
                    break;
            }

            var found = ids.Split(',').Select(long.Parse).Where(Products.ContainsKey).Select(id => Products[id]).ToList();
            return Results.Text(JsonSerializer.Serialize(found), "application/json");
        });

        await _app.StartAsync();
        Address = _app.Urls.First();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
