using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Scalar.AspNetCore;
using Souqly.Cart.Application;
using Souqly.Cart.Infrastructure;
using Souqly.Chassis;
using Souqly.Chassis.Security;
using Souqly.Chassis.Validation;

if (await Souqly.Chassis.HealthProbe.TryRunAsync(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddSouqlyDefaults();
builder.Services.AddCartInfrastructure(builder.Configuration);
builder.Services.AddScoped<CartService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<SetQuantityRequestValidator>();
builder.AddSouqlyOpenApi("Souqly Cart API");
builder.Services.AddHealthChecks().AddRedis(builder.Configuration.GetConnectionString("Redis")!, name: "redis", tags: ["ready"]);

var app = builder.Build();

app.UseSouqlyDefaults();
app.MapOpenApi();
app.MapScalarApiReference();

var carts = app.MapGroup("/api/v1/carts/me").WithTags("Panier").RequireAuthorization(Policies.Customer);

carts.MapGet("/", async (HttpContext http, CartService service, CancellationToken ct) =>
        TypedResults.Ok(await service.GetAsync(http.CurrentUser(), ct)))
    .WithSummary("Mon panier (conservé 30 jours)");

carts.MapPut("/items/{productId:long}", async (long productId, SetQuantityRequest request, HttpContext http, CartService service, CancellationToken ct) =>
        TypedResults.Ok(await service.SetQuantityAsync(http.CurrentUser(), productId, request.Quantity, ct)))
    .WithSummary("Ajouter un produit ou fixer sa quantité (1 à 10)")
    .Validate<SetQuantityRequest>();

carts.MapDelete("/items/{productId:long}", async (long productId, HttpContext http, CartService service, CancellationToken ct) =>
        TypedResults.Ok(await service.RemoveAsync(http.CurrentUser(), productId, ct)))
    .WithSummary("Retirer un produit");

carts.MapDelete("/", async Task<NoContent> (HttpContext http, CartService service, CancellationToken ct) =>
    {
        await service.ClearAsync(http.CurrentUser().Id, ct);
        return TypedResults.NoContent();
    })
    .WithSummary("Vider mon panier");

await app.RunAsync();

/// <summary>Point d'entrée exposé pour WebApplicationFactory dans les tests.</summary>
public partial class Program;
