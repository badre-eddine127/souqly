using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Souqly.Web;

namespace Web.Tests;

/// <summary>Faux « gateway » : renvoie ce qu'il a reçu pour vérifier ce que le site web relaie.</summary>
public sealed class FakeGateway : IAsyncLifetime
{
    private WebApplication? _app;

    public string Address { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        // Découverte OpenID Connect minimale d'un « Keycloak » de test.
        _app.MapGet("/realms/souqly/.well-known/openid-configuration", (HttpContext http) =>
        {
            var issuer = $"{http.Request.Scheme}://{http.Request.Host}/realms/souqly";
            return Results.Json(new Dictionary<string, object>
            {
                ["issuer"] = issuer,
                ["authorization_endpoint"] = issuer + "/protocol/openid-connect/auth",
                ["token_endpoint"] = issuer + "/protocol/openid-connect/token",
                ["end_session_endpoint"] = issuer + "/protocol/openid-connect/logout",
                ["jwks_uri"] = issuer + "/protocol/openid-connect/certs",
                ["response_types_supported"] = new[] { "code" },
            });
        });
        _app.MapGet("/realms/souqly/protocol/openid-connect/certs", () => Results.Json(new { keys = Array.Empty<object>() }));
        _app.Map("/api/{**rest}", (HttpContext http) => Results.Json(new
        {
            path = http.Request.Path.Value + http.Request.QueryString,
            method = http.Request.Method,
            cookie = http.Request.Headers.Cookie.ToString(),
            authorization = http.Request.Headers.Authorization.ToString(),
        }));
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

public sealed class WebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public FakeGateway Gateway { get; } = new();

    public Task InitializeAsync() => Gateway.InitializeAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Gateway.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Gateway:Address", Gateway.Address);
        builder.UseSetting("Oidc:Authority", Gateway.Address + "/realms/souqly");
    }
}

public sealed record Echo(string Path, string Method, string Cookie, string Authorization);

public class WebTests(WebFactory factory) : IClassFixture<WebFactory>
{
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task La_page_d_accueil_est_du_HTML_avec_le_script_du_site()
    {
        var response = await Client().GetAsync("/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("/js/app.js");
        response.Headers.GetValues("X-Frame-Options").ShouldContain("DENY");
    }

    [Fact]
    public async Task Une_adresse_du_site_sert_aussi_la_page_d_accueil_navigation_cote_client()
    {
        var response = await Client().GetAsync("/commandes/0b2c3d4e-0000-0000-0000-000000000000");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("/js/app.js");
    }

    [Fact]
    public async Task Les_fichiers_JavaScript_et_CSS_sont_servis()
    {
        (await Client().GetAsync("/js/app.js")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client().GetAsync("/css/site.css")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Les_pages_vendeur_et_administration_sont_servies()
    {
        foreach (var page in new[] { "become-vendor", "vendor-dashboard", "admin-vendors", "admin-audit" })
        {
            (await Client().GetAsync($"/js/pages/{page}.js")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await Client().GetStringAsync("/admin/journal")).ShouldContain("/js/app.js");
    }

    [Fact]
    public async Task Le_rapport_Gherkin_exige_une_session_administrateur()
    {
        (await Client().GetAsync("/session/gherkin-report")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client().GetAsync("/js/pages/admin-gherkin.js")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Le_renouvellement_des_roles_exige_une_session()
    {
        (await Client().PostAsync("/session/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_visiteur_n_est_pas_connecte()
    {
        var user = await Client().GetFromJsonAsync<SessionEndpoints.UserInfo>("/session/user");
        user!.Authenticated.ShouldBeFalse();
        user.Roles.ShouldBeEmpty();
    }

    [Fact]
    public async Task La_connexion_redirige_vers_Keycloak_en_code_avec_PKCE()
    {
        var response = await Client().GetAsync("/session/login?returnUrl=/panier");
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.ShouldStartWith(factory.Gateway.Address + "/realms/souqly/protocol/openid-connect/auth");
        location.ShouldContain("client_id=souqly-web");
        location.ShouldContain("response_type=code");
        location.ShouldContain("code_challenge_method=S256");
        location.ShouldContain(Uri.EscapeDataString("/signin-oidc"));
    }

    [Fact]
    public async Task L_API_est_relayee_vers_la_passerelle_sans_le_cookie_du_site()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products?size=2");
        request.Headers.Add("Cookie", "souqly.session=secret");
        var echo = await (await Client().SendAsync(request)).Content.ReadFromJsonAsync<Echo>();
        echo!.Path.ShouldBe("/api/v1/products?size=2");
        echo.Cookie.ShouldBeEmpty();
        echo.Authorization.ShouldBeEmpty();   // visiteur : pas de jeton
    }

    [Fact]
    public async Task Un_appel_modifiant_sans_en_tete_anti_CSRF_est_refuse()
    {
        var response = await Client().PostAsync("/api/v1/orders", new StringContent("{}", Encoding.UTF8, "application/json"));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_appel_modifiant_avec_l_en_tete_anti_CSRF_est_relaye()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.Add(SessionEndpoints.CsrfHeader, "1");
        var echo = await (await Client().SendAsync(request)).Content.ReadFromJsonAsync<Echo>();
        echo!.Method.ShouldBe("POST");
    }

    [Theory]
    [InlineData("/panier", "/panier")]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData(null, "/")]
    public void Le_retour_apres_connexion_reste_sur_le_site(string? returnUrl, string expected) =>
        SessionEndpoints.SafeReturnUrl(returnUrl).ShouldBe(expected);

    [Fact]
    public void Les_roles_sont_lus_dans_le_jeton_Keycloak()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"realm_access":{"roles":["CUSTOMER","offline_access","VENDOR"]}}""")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        SessionEndpoints.RealmRoles($"x.{payload}.y").ShouldBe(["CUSTOMER", "VENDOR"]);
        SessionEndpoints.RealmRoles("pas-un-jeton").ShouldBeEmpty();
    }
}
