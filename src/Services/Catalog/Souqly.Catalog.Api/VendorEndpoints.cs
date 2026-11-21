using Microsoft.AspNetCore.Http.HttpResults;
using Souqly.Catalog.Application.Vendors;
using Souqly.Chassis;
using Souqly.Chassis.Paging;
using Souqly.Chassis.Security;
using Souqly.Chassis.Validation;

namespace Souqly.Catalog.Api;

/// <summary>US-16 : demandes d'ouverture de boutique et validation par un administrateur.</summary>
internal static class VendorEndpoints
{
    public static IEndpointRouteBuilder MapVendorEndpoints(this IEndpointRouteBuilder app)
    {
        var vendors = app.MapGroup("/api/v1/vendors").WithTags("Vendeurs");

        vendors.MapPost("/", SubmitAsync)
            .WithSummary("Demander l'ouverture d'une boutique (utilisateur connecté)")
            .RequireAuthorization()
            .Validate<VendorApplicationRequest>();

        vendors.MapGet("/me", async (HttpContext http, VendorService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetMineAsync(http.CurrentUser(), ct)))
            .WithName("GetMyVendorApplication")
            .WithSummary("Ma demande d'ouverture de boutique")
            .RequireAuthorization();

        vendors.MapGet("/", ListAsync)
            .WithSummary("Demandes à traiter (administrateur)")
            .RequireAuthorization(Policies.Admin);

        vendors.MapPost("/{id:long}/approval", DecideAsync)
            .WithSummary("Valider ou refuser une boutique (administrateur)")
            .RequireAuthorization(Policies.Admin)
            .Validate<VendorDecisionRequest>();

        vendors.MapGet("/shops", async (VendorService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListShopsAsync(ct)))
            .WithSummary("Boutiques validées (nom et ville)")
            .AllowAnonymous();

        return app;
    }

    private static async Task<Results<CreatedAtRoute<VendorApplicationResponse>, Ok<VendorApplicationResponse>>> SubmitAsync(
        VendorApplicationRequest request, HttpContext http, VendorService service, CancellationToken ct)
    {
        var (application, created) = await service.SubmitAsync(request, http.CurrentUser(), ct);
        return created
            ? TypedResults.CreatedAtRoute(application, "GetMyVendorApplication")
            : TypedResults.Ok(application);
    }

    private static async Task<Ok<PageResponse<VendorApplicationResponse>>> ListAsync(
        string? status, int? page, int? size, VendorService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ListAsync(status, page, size, ct));

    private static async Task<Ok<VendorApplicationResponse>> DecideAsync(
        long id, VendorDecisionRequest request, HttpContext http, VendorService service, CancellationToken ct) =>
        TypedResults.Ok(await service.DecideAsync(id, request, http.CurrentUser(), ct));
}
