using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Souqly.Chassis;
using Souqly.Messaging;
using Souqly.Search.Api.Application;
using Souqly.Search.Api.Infrastructure;

if (await HealthProbe.TryRunAsync(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddSouqlyDefaults();
var connectionString = builder.Configuration.GetConnectionString("SearchDb")
    ?? throw new InvalidOperationException("ConnectionStrings:SearchDb manquante");
builder.Services.AddDbContext<SearchDbContext>(o => o
    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<CatalogEventHandlers>();

// Modèle de lecture (CQRS) : alimenté par les événements produit de Catalog (US-04).
builder.AddSouqlyMessaging<SearchDbContext>(m => m
    .Consume<ProductCreated, CatalogEventHandlers>(Topics.CatalogEvents)
    .Consume<ProductUpdated, CatalogEventHandlers>(Topics.CatalogEvents)
    .Consume<ProductArchived, CatalogEventHandlers>(Topics.CatalogEvents));

// Premier remplissage depuis la liste publique de Catalog si l'index est vide.
var catalogAddress = builder.Configuration["Catalog:BaseAddress"] ?? "http://localhost:5101";
builder.Services.AddHttpClient(CatalogBackfill.Client, c => c.BaseAddress = new Uri(catalogAddress.TrimEnd('/') + "/"))
    .AddStandardResilienceHandler();
if (builder.Configuration.GetValue("Search:BackfillFromCatalog", true))
{
    builder.Services.AddHostedService<CatalogBackfill>();
}

builder.AddSouqlyOpenApi("Souqly Search API");
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "search-db", tags: ["ready"]);

var app = builder.Build();

app.UseSouqlyDefaults();
app.MapOpenApi();
app.MapScalarApiReference();

app.MapGet("/api/v1/search", async ([AsParameters] SearchRequest request, SearchService search, CancellationToken ct) =>
        TypedResults.Ok(await search.SearchAsync(request, ct)))
    .WithTags("Recherche")
    .WithSummary("Recherche en texte libre, tolérante aux fautes, avec filtres catégorie et prix et facettes (US-04)")
    .AllowAnonymous();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<SearchDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Point d'entrée exposé aux tests (WebApplicationFactory).</summary>
public partial class Program;
