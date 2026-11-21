using FluentValidation;
using Souqly.Catalog.Domain.Vendors;

namespace Souqly.Catalog.Application.Vendors;

/// <summary>Corps de POST /api/v1/vendors : demande d'ouverture de boutique.</summary>
public sealed record VendorApplicationRequest(string ShopName, string City, string? Description);

/// <summary>Corps de POST /api/v1/vendors/{id}/approval : décision de l'administrateur.</summary>
public sealed record VendorDecisionRequest(bool Approved, string? Reason);

public sealed record VendorApplicationResponse(
    long Id,
    string UserId,
    string ShopName,
    string City,
    string Description,
    string Status,
    string? RejectionReason,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DecidedAt)
{
    public static VendorApplicationResponse From(VendorApplication a) => new(
        a.Id, a.UserId, a.ShopName, a.City, a.Description, a.Status.ToString().ToUpperInvariant(),
        a.RejectionReason, a.SubmittedAt, a.DecidedAt);
}

/// <summary>Boutique validée, telle qu'affichée publiquement (aucune donnée personnelle).</summary>
public sealed record VendorShopResponse(string VendorId, string ShopName, string City);

public sealed class VendorApplicationRequestValidator : AbstractValidator<VendorApplicationRequest>
{
    public VendorApplicationRequestValidator()
    {
        RuleFor(r => r.ShopName).NotEmpty().MaximumLength(VendorApplication.ShopNameMaxLength);
        RuleFor(r => r.City).NotEmpty().MaximumLength(VendorApplication.CityMaxLength);
        RuleFor(r => r.Description).MaximumLength(VendorApplication.DescriptionMaxLength);
    }
}

public sealed class VendorDecisionRequestValidator : AbstractValidator<VendorDecisionRequest>
{
    public VendorDecisionRequestValidator()
    {
        RuleFor(r => r.Reason).NotEmpty().When(r => !r.Approved).WithMessage("Un refus doit être motivé.");
        RuleFor(r => r.Reason).MaximumLength(VendorApplication.ReasonMaxLength);
    }
}

/// <summary>
/// Port vers le fournisseur d'identité : attribue le rôle de realm VENDOR à un utilisateur.
/// Implémenté avec l'API d'administration Keycloak et le compte technique catalog-service.
/// </summary>
public interface IVendorRoleGranter
{
    Task GrantVendorRoleAsync(string userId, CancellationToken ct);
}
