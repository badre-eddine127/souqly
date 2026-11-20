using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Souqly.Chassis;
using Souqly.Chassis.Errors;
using Souqly.Chassis.Security;
using Souqly.Messaging;
using Souqly.Payment.Api.Application;
using Souqly.Payment.Api.Infrastructure;

if (await HealthProbe.TryRunAsync(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddSouqlyDefaults();
var connectionString = builder.Configuration.GetConnectionString("PaymentDb")
    ?? throw new InvalidOperationException("ConnectionStrings:PaymentDb manquante");
builder.Services.AddDbContext<PaymentDbContext>(o => o
    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(builder.Configuration.GetSection("Payment").Get<PaymentOptions>() ?? new PaymentOptions());
builder.Services.AddSingleton(TimeProvider.System);
builder.AddSouqlyMessaging<PaymentDbContext>(m => m
    .Consume<PaymentRequested, PaymentProcessor>(Topics.OrderingEvents)
    .Consume<OrderCancelled, PaymentProcessor>(Topics.OrderingEvents));
builder.AddSouqlyOpenApi("Souqly Payment API");
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "payment-db", tags: ["ready"]);

var app = builder.Build();

app.UseSouqlyDefaults();
app.MapOpenApi();
app.MapScalarApiReference();

app.MapGet("/api/v1/payments/{orderId:guid}", async (Guid orderId, HttpContext http, PaymentDbContext db, CancellationToken ct) =>
    {
        var user = http.CurrentUser();
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is null || (payment.CustomerId != user.Id && !user.IsInRole(Roles.Admin)))
        {
            throw new NotFoundException("Paiement", orderId);   // RG-07 : pas de fuite d'existence
        }

        return TypedResults.Ok(new PaymentResponse(payment.Id, payment.OrderId, PaymentStatusNames.Of(payment.Status),
            payment.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), payment.Currency, payment.FailureReason, payment.UpdatedAt));
    })
    .WithTags("Paiements")
    .WithSummary("État du paiement d'une de mes commandes")
    .RequireAuthorization(Policies.Customer);

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Point d'entrée exposé pour WebApplicationFactory dans les tests.</summary>
public partial class Program;
