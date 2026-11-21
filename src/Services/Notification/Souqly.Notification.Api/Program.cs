using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Souqly.Chassis;
using Souqly.Chassis.Errors;
using Souqly.Chassis.Paging;
using Souqly.Chassis.Security;
using Souqly.Messaging;
using Souqly.Notification.Api.Application;
using Souqly.Notification.Api.Infrastructure;

if (await HealthProbe.TryRunAsync(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddSouqlyDefaults();
var connectionString = builder.Configuration.GetConnectionString("NotificationDb")
    ?? throw new InvalidOperationException("ConnectionStrings:NotificationDb manquante");
builder.Services.AddDbContext<NotificationDbContext>(o => o
    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(TimeProvider.System);
builder.AddSouqlyMessaging<NotificationDbContext>(m => m
    .Consume<OrderConfirmed, NotificationHandlers>(Topics.OrderingEvents)
    .Consume<OrderRejected, NotificationHandlers>(Topics.OrderingEvents)
    .Consume<OrderCancelled, NotificationHandlers>(Topics.OrderingEvents)
    .Consume<PaymentRefunded, NotificationHandlers>(Topics.PaymentEvents)
    .Consume<OrderShipped, NotificationHandlers>(Topics.OrderingEvents)
    .Consume<OrderDelivered, NotificationHandlers>(Topics.OrderingEvents));
builder.AddSouqlyOpenApi("Souqly Notification API");
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "notification-db", tags: ["ready"]);

var app = builder.Build();

app.UseSouqlyDefaults();
app.MapOpenApi();
app.MapScalarApiReference();

var notifications = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization();

notifications.MapGet("/", async (int? page, int? size, HttpContext http, NotificationDbContext db, CancellationToken ct) =>
    {
        var me = http.CurrentUser().Id;
        var request = PageRequest.Of(page, size);
        var query = db.Notifications.AsNoTracking().Where(n => n.RecipientId == me);
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(request.Skip).Take(request.Size)
            .Select(n => new NotificationResponse(n.Id, n.Kind, n.OrderId, n.Title, n.Body, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(ct);
        return TypedResults.Ok(PageResponse<NotificationResponse>.From(items, request, total));
    })
    .WithSummary("Mes notifications (client ou vendeur), les plus récentes d'abord");

notifications.MapPost("/{id:guid}/read", async (Guid id, HttpContext http, NotificationDbContext db, TimeProvider clock, CancellationToken ct) =>
    {
        var me = http.CurrentUser().Id;
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientId == me, ct)
                           ?? throw new NotFoundException("Notification", id);
        notification.MarkRead(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    })
    .WithSummary("Marquer une notification comme lue");

notifications.MapPost("/read-all", async (HttpContext http, NotificationDbContext db, TimeProvider clock, CancellationToken ct) =>
    {
        var me = http.CurrentUser().Id;
        var now = clock.GetUtcNow();
        var updated = await db.Notifications
            .Where(n => n.RecipientId == me && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), ct);
        return TypedResults.Ok(new ReadAllResponse(updated));
    })
    .WithSummary("Marquer toutes mes notifications comme lues (ouverture de la cloche)");

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Point d'entrée exposé pour WebApplicationFactory dans les tests.</summary>
public partial class Program;
