using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Souqly.Cart.Application;
using Souqly.Testing;
using StackExchange.Redis;

namespace Cart.Tests;

public sealed class CartApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly RedisFixture _redis = new();

    public async Task InitializeAsync() => await _redis.InitializeAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _redis.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Redis", _redis.ConnectionString);
    }

    public HttpClient ClientAs(string userId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", userId);
        client.DefaultRequestHeaders.Add("X-User-Roles", string.Join(',', roles));
        return client;
    }
}

[Trait("Category", "Integration")]
public class CartApiTests(CartApiFactory factory) : IClassFixture<CartApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string NewCustomer() => "customer-" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task US05_ajouter_modifier_retirer_et_retrouver_son_panier()
    {
        var sara = factory.ClientAs(NewCustomer(), "CUSTOMER");

        await sara.PutAsJsonAsync("/api/v1/carts/me/items/42", new SetQuantityRequest(1));
        await sara.PutAsJsonAsync("/api/v1/carts/me/items/7", new SetQuantityRequest(2));
        await sara.PutAsJsonAsync("/api/v1/carts/me/items/42", new SetQuantityRequest(3));
        await sara.DeleteAsync("/api/v1/carts/me/items/7");

        // « conservé entre deux visites » : un nouveau client HTTP relit le panier depuis Redis.
        var cart = await factory.ClientAs(sara.DefaultRequestHeaders.GetValues("X-User-Id").Single(), "CUSTOMER")
            .GetFromJsonAsync<CartResponse>("/api/v1/carts/me", Json);
        cart!.Items.ShouldBe([new CartLineResponse(42, 3)]);
        cart.TotalUnits.ShouldBe(3);
    }

    [Fact]
    public async Task Le_panier_expire_apres_30_jours_sans_modification()
    {
        var customer = NewCustomer();
        await factory.ClientAs(customer, "CUSTOMER").PutAsJsonAsync("/api/v1/carts/me/items/1", new SetQuantityRequest(1));

        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var ttl = await redis.KeyTimeToLiveAsync($"souqly:cart:{customer}");

        ttl.ShouldNotBeNull();
        ttl.Value.ShouldBeInRange(TimeSpan.FromDays(29.9), TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task Chaque_client_ne_voit_que_son_panier()
    {
        var sara = factory.ClientAs(NewCustomer(), "CUSTOMER");
        await sara.PutAsJsonAsync("/api/v1/carts/me/items/42", new SetQuantityRequest(2));

        var karim = await factory.ClientAs(NewCustomer(), "CUSTOMER").GetFromJsonAsync<CartResponse>("/api/v1/carts/me", Json);
        karim!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_quantite_de_11_renvoie_400()
    {
        var response = await factory.ClientAs(NewCustomer(), "CUSTOMER").PutAsJsonAsync("/api/v1/carts/me/items/1", new SetQuantityRequest(11));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Un_vendeur_n_a_pas_de_panier_403_et_un_anonyme_401()
    {
        (await factory.ClientAs("vendor-1", "VENDOR").GetAsync("/api/v1/carts/me")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/api/v1/carts/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Vider_le_panier()
    {
        var sara = factory.ClientAs(NewCustomer(), "CUSTOMER");
        await sara.PutAsJsonAsync("/api/v1/carts/me/items/1", new SetQuantityRequest(1));

        (await sara.DeleteAsync("/api/v1/carts/me")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await sara.GetFromJsonAsync<CartResponse>("/api/v1/carts/me", Json))!.Items.ShouldBeEmpty();
    }
}
