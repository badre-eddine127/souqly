using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Souqly.Chassis;
using Souqly.Inventory.Api;
using Souqly.Inventory.Application;
using Souqly.Inventory.Infrastructure;
using Souqly.Inventory.Infrastructure.Persistence;
using Souqly.Messaging;

if (await Souqly.Chassis.HealthProbe.TryRunAsync(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddSouqlyDefaults();
builder.Services.AddInventoryInfrastructure(builder.Configuration);
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<ReservationService>();
builder.AddSouqlyMessaging<InventoryDbContext>(m => m
    .Consume<OrderPlaced, InventorySaga>(Topics.OrderingEvents)
    .Consume<OrderConfirmed, InventorySaga>(Topics.OrderingEvents)
    .Consume<OrderRejected, InventorySaga>(Topics.OrderingEvents)
    .Consume<OrderCancelled, InventorySaga>(Topics.OrderingEvents));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<SetStockRequestValidator>();
builder.AddSouqlyOpenApi("Souqly Inventory API");
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("InventoryDb")!, name: "inventory-db", tags: ["ready"]);

var app = builder.Build();

app.UseSouqlyDefaults();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapInventoryEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Point d'entrée exposé pour WebApplicationFactory dans les tests.</summary>
public partial class Program;
