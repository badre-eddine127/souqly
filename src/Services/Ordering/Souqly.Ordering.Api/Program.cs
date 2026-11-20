using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Souqly.Chassis;
using Souqly.Messaging;
using Souqly.Ordering.Api;
using Souqly.Ordering.Application;
using Souqly.Ordering.Infrastructure;
using Souqly.Ordering.Infrastructure.Persistence;

if (await HealthProbe.TryRunAsync(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddSouqlyDefaults();
builder.Services.AddOrderingInfrastructure(builder.Configuration);
builder.Services.AddScoped<OrderService>();
builder.AddSouqlyMessaging<OrderingDbContext>(m => m
    .Consume<StockReserved, OrderSaga>(Topics.InventoryEvents)
    .Consume<StockReservationFailed, OrderSaga>(Topics.InventoryEvents)
    .Consume<PaymentSucceeded, OrderSaga>(Topics.PaymentEvents)
    .Consume<PaymentFailed, OrderSaga>(Topics.PaymentEvents));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<PlaceOrderRequestValidator>();
builder.AddSouqlyOpenApi("Souqly Ordering API");
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("OrderDb")!, name: "order-db", tags: ["ready"]);

var app = builder.Build();

app.UseSouqlyDefaults();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapOrderingEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Point d'entrée exposé pour WebApplicationFactory dans les tests.</summary>
public partial class Program;
