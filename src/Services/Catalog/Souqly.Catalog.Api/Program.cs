using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Souqly.Catalog.Api;
using Souqly.Catalog.Application.Products;
using Souqly.Catalog.Application.Vendors;
using Souqly.Catalog.Infrastructure;
using Souqly.Catalog.Infrastructure.Persistence;
using Souqly.Chassis;

if (await Souqly.Chassis.HealthProbe.TryRunAsync(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddSouqlyDefaults();
builder.Services.AddCatalogInfrastructure(builder.Configuration);
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<VendorService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<ProductRequestValidator>();
builder.AddSouqlyOpenApi("Souqly Catalog API");
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("CatalogDb")!, name: "catalog-db", tags: ["ready"]);

var app = builder.Build();

app.UseSouqlyDefaults();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapCatalogEndpoints();
app.MapVendorEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Point d'entrée exposé pour WebApplicationFactory dans les tests.</summary>
public partial class Program;
