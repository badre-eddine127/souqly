using Microsoft.AspNetCore.Http.HttpResults;
using Souqly.Chassis;
using Souqly.Chassis.Security;
using Souqly.Chassis.Validation;
using Souqly.Inventory.Application;

namespace Souqly.Inventory.Api;

internal static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var stock = app.MapGroup("/api/v1/stock-items").WithTags("Stock");

        stock.MapGet("/{productId:long}", async (long productId, StockService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AvailabilityAsync(productId, ct)))
            .WithSummary("Disponibilité publique d'un produit (disponible = physique − réservé)")
            .AllowAnonymous();

        stock.MapGet("/{productId:long}/details", async (long productId, HttpContext http, StockService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetForVendorAsync(productId, http.CurrentUser(), ct)))
            .WithSummary("Détail du stock (vendeur propriétaire)")
            .RequireAuthorization(Policies.Vendor);

        stock.MapPut("/{productId:long}", async (long productId, SetStockRequest request, HttpContext http, StockService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SetAsync(productId, request, http.CurrentUser(), ct)))
            .WithSummary("Fixer le stock physique (vendeur propriétaire)")
            .RequireAuthorization(Policies.Vendor)
            .Validate<SetStockRequest>();

        stock.MapPost("/{productId:long}/adjustments", async (long productId, AdjustStockRequest request, HttpContext http, StockService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AdjustAsync(productId, request, http.CurrentUser(), ct)))
            .WithSummary("Ajuster le stock (+/−, avec motif)")
            .RequireAuthorization(Policies.Vendor)
            .Validate<AdjustStockRequest>();

        // Interne : commandes de la saga (passeront par Kafka au sprint 3). Jamais routé par la passerelle.
        var reservations = app.MapGroup("/internal/v1/reservations").WithTags("Interne").RequireAuthorization(Policies.Internal);

        reservations.MapPost("/", ReserveAsync)
            .WithSummary("Réserver le stock d'une commande pour 15 minutes (tout ou rien, idempotent)")
            .Validate<ReserveStockRequest>();

        reservations.MapPost("/{orderId:guid}/release", async (Guid orderId, ReservationService service, CancellationToken ct) =>
            {
                await service.ReleaseAsync(orderId, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Libérer les réservations d'une commande (compensation)");

        reservations.MapPost("/{orderId:guid}/commit", async (Guid orderId, ReservationService service, CancellationToken ct) =>
            {
                await service.CommitAsync(orderId, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Consommer les réservations d'une commande confirmée");

        return app;
    }

    private static async Task<Results<Ok<ReservationResult>, Conflict<ReservationResult>>> ReserveAsync(
        ReserveStockRequest request, ReservationService service, CancellationToken ct)
    {
        var result = await service.ReserveAsync(request, ct);
        return result.Succeeded ? TypedResults.Ok(result) : TypedResults.Conflict(result);
    }
}
