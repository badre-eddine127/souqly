using System.Threading.RateLimiting;
using Scalar.AspNetCore;
using Souqly.Chassis.Errors;

if (await Souqly.Chassis.HealthProbe.TryRunAsync(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Passerelle : point d'entrée unique. Elle route, limite le débit et ajoute les en-têtes de sécurité.
// Aucune logique métier. La validation JWT (Keycloak) s'ajoute au sprint 2 ; chaque service garde ses propres règles.
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ProblemDetailsExceptionHandler.Customize);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHealthChecks();

var permitsPerSecond = builder.Configuration.GetValue("RateLimiting:PermitsPerSecond", 20);
var burst = builder.Configuration.GetValue("RateLimiting:Burst", 40);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Quota par utilisateur (en-tête d'identité) sinon par adresse IP : 20 requêtes/s, pointes à 40.
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
        var user = http.Request.Headers["X-User-Id"].ToString();
        var key = string.IsNullOrEmpty(user) ? "ip:" + http.Connection.RemoteIpAddress : "user:" + user;
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = burst,
            TokensPerPeriod = permitsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
    o.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "1";
        await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status429TooManyRequests,
                Type = "https://docs.souqly.ma/errors/rate-limited",
                Title = "Trop de requêtes",
                Detail = "Quota dépassé ; réessayez dans une seconde.",
            },
        });
    };
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"])
    .WithMethods("GET", "POST", "PUT", "DELETE")
    .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "If-Match")));

var app = builder.Build();

app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    // En-têtes de sécurité (OWASP A05).
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    // Les endpoints /internal ne sont jamais exposés, même si une route était mal configurée.
    if (context.Request.Path.StartsWithSegments("/internal"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});
app.UseCors();
app.UseRateLimiter();

app.MapHealthChecks("/health/live").DisableRateLimiting();
app.MapHealthChecks("/health/ready").DisableRateLimiting();

// Documentation agrégée : un seul Scalar pour tous les services, alimenté par leurs documents OpenAPI.
app.MapScalarApiReference(o =>
{
    o.Title = "Souqly API";
    o.AddDocument("catalog", "Catalog", "/openapi/catalog.json");
    o.AddDocument("inventory", "Inventory", "/openapi/inventory.json");
    o.AddDocument("cart", "Cart", "/openapi/cart.json");
}).DisableRateLimiting();

app.MapReverseProxy();

await app.RunAsync();

/// <summary>Point d'entrée exposé pour WebApplicationFactory dans les tests.</summary>
public partial class Program;
