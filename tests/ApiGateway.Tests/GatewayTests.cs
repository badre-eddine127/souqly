using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Souqly.Testing;

namespace ApiGateway.Tests;

/// <summary>Un faux service Catalog sur un port local, pour vérifier que la passerelle route vraiment.</summary>
public sealed class FakeCatalog : IAsyncLifetime
{
    private WebApplication? _app;

    public string Address { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.MapGet("/api/v1/products", () => Results.Text("catalog:products"));
        _app.MapPost("/api/v1/products", () => Results.Text("catalog:created"));
        _app.MapGet("/internal/v1/products/prices", () => Results.Text("SECRET"));
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

public sealed class GatewayFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public FakeCatalog Catalog { get; } = new();

    public Task InitializeAsync() => Catalog.InitializeAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Catalog.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services => services.UseTestJwt());
        builder.UseSetting("ReverseProxy:Clusters:catalog:Destinations:d1:Address", Catalog.Address);
        builder.UseSetting("RateLimiting:PermitsPerSecond", "5");
        builder.UseSetting("RateLimiting:Burst", "5");
    }
}

public class GatewayTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private HttpClient ClientAs(string user) => factory.CreateClient().WithToken(TestJwt.Token(user, "VENDOR"));

    [Fact]
    public async Task La_passerelle_route_le_catalogue_vers_le_service()
    {
        var body = await ClientAs("route-user").GetStringAsync("/api/v1/products");
        body.ShouldBe("catalog:products");
    }

    [Fact]
    public async Task Les_endpoints_internes_ne_sont_jamais_exposes()
    {
        var response = await ClientAs("internal-user").GetAsync("/internal/v1/products/prices?ids=1");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("SECRET");
    }

    [Fact]
    public async Task Au_dela_du_quota_la_passerelle_repond_429_avec_Retry_After()
    {
        var client = ClientAs("greedy-user");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
        {
            statuses.Add((await client.GetAsync("/api/v1/products")).StatusCode);
        }

        statuses.ShouldContain(HttpStatusCode.TooManyRequests);
        statuses.Take(5).ShouldAllBe(s => s == HttpStatusCode.OK);
        var rejected = await client.GetAsync("/api/v1/products");
        rejected.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task Le_quota_est_par_utilisateur()
    {
        for (var i = 0; i < 10; i++)
        {
            await ClientAs("noisy-neighbour").GetAsync("/api/v1/products");
        }

        (await ClientAs("calm-user").GetAsync("/api/v1/products")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Les_en_tetes_de_securite_sont_ajoutes()
    {
        var response = await ClientAs("headers-user").GetAsync("/api/v1/products");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        response.Headers.GetValues("X-Frame-Options").ShouldContain("DENY");
    }

    [Fact]
    public async Task Une_route_inconnue_renvoie_404()
    {
        (await ClientAs("unknown-user").GetAsync("/api/v1/nope")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Une_ecriture_sans_jeton_est_refusee_401_par_la_passerelle()
    {
        var response = await factory.CreateClient().PostAsync("/api/v1/products", null);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_jeton_falsifie_expire_ou_pour_une_autre_audience_est_refuse_401()
    {
        foreach (var token in new[]
                 {
                     TestJwt.ForgedToken("pirate", "VENDOR"),
                     TestJwt.ExpiredToken("ancien", "VENDOR"),
                     TestJwt.WrongAudienceToken("autre-app", "VENDOR"),
                 })
        {
            var response = await factory.CreateClient().WithToken(token).PostAsync("/api/v1/products", null);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Une_ecriture_avec_un_jeton_valide_est_routee()
    {
        var response = await ClientAs("writer").PostAsync("/api/v1/products", null);
        (await response.Content.ReadAsStringAsync()).ShouldBe("catalog:created");
    }

    [Fact]
    public async Task La_lecture_du_catalogue_reste_publique()
    {
        (await factory.CreateClient().GetStringAsync("/api/v1/products")).ShouldBe("catalog:products");
    }
}
