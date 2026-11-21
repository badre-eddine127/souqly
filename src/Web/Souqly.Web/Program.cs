using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Souqly.Chassis;
using Souqly.Web;

if (await HealthProbe.TryRunAsync(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSouqlyTelemetry();

// Connexion Keycloak côté serveur (Authorization Code + PKCE, client public « souqly-web ») ;
// les jetons restent dans le cookie de session chiffré, jamais dans le JavaScript.
var oidc = builder.Configuration.GetSection("Oidc");
builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(o =>
    {
        o.Cookie.Name = "souqly.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        // Pour l'API appelée en JavaScript : 401 au lieu d'une redirection vers Keycloak.
        o.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    })
    .AddOpenIdConnect(o =>
    {
        o.Authority = oidc["Authority"];
        o.ClientId = oidc["ClientId"];
        o.RequireHttpsMetadata = oidc.GetValue("RequireHttpsMetadata", true);
        if (oidc["MetadataAddress"] is { Length: > 0 } metadata)
        {
            o.MetadataAddress = metadata;   // en conteneur : découverte par le réseau interne
        }

        o.ResponseType = "code";
        o.UsePkce = true;
        o.SaveTokens = true;
        o.GetClaimsFromUserInfoEndpoint = false;
        o.MapInboundClaims = false;
        o.Scope.Clear();
        o.Scope.Add("openid");
        o.Scope.Add("profile");
        o.Scope.Add("email");
        o.TokenValidationParameters.NameClaimType = "preferred_username";
        o.SignedOutRedirectUri = "/";
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpClient(TokenRefresher.HttpClientName);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TokenRefresher>();
builder.Services.AddSouqlyApiProxy(builder.Configuration);
builder.Services.AddSingleton<VendorDirectory>();
builder.Services.AddHttpClient(DemoCatalogSeeder.HttpClientName);
if (builder.Configuration.GetValue("Demo:Seed", false))
{
    builder.Services.AddHostedService<DemoCatalogSeeder>();
}
builder.Services.AddHealthChecks();

var app = builder.Build();

app.Use(async (context, next) =>
{
    // En-têtes de sécurité (OWASP A05).
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapSessionEndpoints();
app.MapSouqlyApiProxy();

// Navigation côté client (#/...) : toute autre adresse sert la page d'accueil.
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Point d'entrée exposé pour WebApplicationFactory dans les tests.</summary>
public partial class Program;
