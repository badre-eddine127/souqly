using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Souqly.Chassis;
using Souqly.Chassis.Errors;
using Souqly.Chassis.Paging;
using Souqly.Chassis.Security;
using Souqly.Chassis.Validation;
using Souqly.Ordering.Application;
using Souqly.Ordering.Domain;

namespace Souqly.Ordering.Api;

internal static class OrderingEndpoints
{
    public static IEndpointRouteBuilder MapOrderingEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/v1/orders").WithTags("Commandes").RequireAuthorization(Policies.Customer);

        orders.MapPost("/", PlaceAsync)
            .WithSummary("Passer une commande (en-tête Idempotency-Key obligatoire) : 202 + Location")
            .Validate<PlaceOrderRequest>();

        orders.MapGet("/", async (int? page, int? size, HttpContext http, OrderService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListMineAsync(http.CurrentUser(), page, size, ct)))
            .WithSummary("Mes commandes, les plus récentes d'abord");

        orders.MapGet("/{id:guid}", async (Guid id, HttpContext http, OrderService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetAsync(id, http.CurrentUser(), ct)))
            .WithName("GetOrder")
            .WithSummary("Suivre une de mes commandes (US-07)");

        orders.MapPost("/{id:guid}/cancellation", async (Guid id, HttpContext http, OrderService service, CancellationToken ct) =>
                TypedResults.Ok(await service.CancelAsync(id, http.CurrentUser(), ct)))
            .WithSummary("Annuler une commande non expédiée (US-08)");

        app.MapGet("/api/v1/vendor/orders", VendorOrdersAsync)
            .WithTags("Espace vendeur")
            .WithSummary("Commandes contenant mes produits (seulement mes lignes)")
            .RequireAuthorization(Policies.Vendor);

        return app;
    }

    private static async Task<Accepted<OrderResponse>> PlaceAsync(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        PlaceOrderRequest request, HttpContext http, OrderService service, CancellationToken ct)
    {
        if (!Guid.TryParse(idempotencyKey, out var key))
        {
            throw new FluentValidation.ValidationException(
            [
                new FluentValidation.Results.ValidationFailure("Idempotency-Key",
                    "L'en-tête Idempotency-Key (UUID) est obligatoire : il évite de payer deux fois en cas de double clic."),
            ]);
        }

        var result = await service.PlaceAsync(http.CurrentUser(), key, request, ct);
        if (result.Replayed)
        {
            http.Response.Headers["Idempotent-Replayed"] = "true";
        }

        // 202 : la commande est acceptée en PENDING ; la saga (stock, paiement) la fera avancer.
        // Location relative : derrière la passerelle, une URL absolue exposerait l'adresse interne du service.
        return TypedResults.Accepted($"/api/v1/orders/{result.Order.Id}", result.Order);
    }

    private static async Task<Ok<PageResponse<VendorOrderResponse>>> VendorOrdersAsync(
        string? status, int? page, int? size, HttpContext http, OrderService service, CancellationToken ct)
    {
        OrderStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            filter = Enum.TryParse<OrderStatus>(status.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var parsed)
                ? parsed
                : throw new BusinessRuleException("invalid-status", $"Statut inconnu : {status}.");
        }

        return TypedResults.Ok(await service.ListForVendorAsync(http.CurrentUser(), filter, page, size, ct));
    }
}
