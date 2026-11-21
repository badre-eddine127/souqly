using Souqly.Catalog.Domain.Vendors;
using Souqly.Chassis.Errors;
using Souqly.Chassis.Paging;
using Souqly.Chassis.Security;

namespace Souqly.Catalog.Application.Vendors;

/// <summary>US-16 : demande d'ouverture de boutique, puis validation ou refus par un administrateur.</summary>
public sealed class VendorService(IVendorApplicationRepository applications, IVendorRoleGranter roles, TimeProvider clock)
{
    /// <summary>Le demandeur soumet (ou soumet à nouveau après un refus) sa demande.</summary>
    public async Task<(VendorApplicationResponse Application, bool Created)> SubmitAsync(
        VendorApplicationRequest request, CurrentUser user, CancellationToken ct)
    {
        if (user.IsInRole(Roles.Vendor))
        {
            throw new ConflictException("Vous êtes déjà vendeur.");
        }

        var now = clock.GetUtcNow();
        var existing = await applications.FindByUserAsync(user.Id, ct);
        if (existing is not null)
        {
            Translate(() => existing.Resubmit(request.ShopName, request.City, request.Description ?? string.Empty, now));
            await applications.SaveChangesAsync(ct);
            return (VendorApplicationResponse.From(existing), false);
        }

        var application = Translate(() =>
            VendorApplication.Submit(user.Id, request.ShopName, request.City, request.Description ?? string.Empty, now));
        applications.Add(application);
        await applications.SaveChangesAsync(ct);
        return (VendorApplicationResponse.From(application), true);
    }

    public async Task<VendorApplicationResponse> GetMineAsync(CurrentUser user, CancellationToken ct)
    {
        var application = await applications.FindByUserAsync(user.Id, ct)
                          ?? throw new NotFoundException("Demande vendeur", "courante");
        return VendorApplicationResponse.From(application);
    }

    /// <summary>File d'attente de l'administrateur, filtrable par statut.</summary>
    public async Task<PageResponse<VendorApplicationResponse>> ListAsync(string? status, int? pageNumber, int? size, CancellationToken ct)
    {
        VendorApplicationStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            filter = Enum.TryParse<VendorApplicationStatus>(status, ignoreCase: true, out var parsed)
                ? parsed
                : throw new BusinessRuleException("invalid-status", "Statut accepté : PENDING, APPROVED ou REJECTED.");
        }

        var page = PageRequest.Of(pageNumber, size);
        var (items, total) = await applications.ListAsync(filter, page.Skip, page.Size, ct);
        return PageResponse<VendorApplicationResponse>.From(items.Select(VendorApplicationResponse.From).ToList(), page, total);
    }

    /// <summary>
    /// Décision de l'administrateur. Validée : le rôle VENDOR est attribué dans Keycloak AVANT d'enregistrer la décision,
    /// pour ne jamais afficher « validée » à un utilisateur qui n'aurait pas le rôle (Keycloak indisponible : 503, on réessaie).
    /// </summary>
    public async Task<VendorApplicationResponse> DecideAsync(long id, VendorDecisionRequest request, CurrentUser admin, CancellationToken ct)
    {
        var application = await applications.FindAsync(id, ct) ?? throw new NotFoundException("Demande vendeur", id);
        var now = clock.GetUtcNow();

        if (request.Approved)
        {
            Translate(() => application.Approve(admin.Id, now));
            try
            {
                await roles.GrantVendorRoleAsync(application.UserId, ct);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw new DependencyUnavailableException("keycloak", 5, e);
            }
        }
        else
        {
            Translate(() => application.Reject(admin.Id, request.Reason ?? string.Empty, now));
        }

        await applications.SaveChangesAsync(ct);
        return VendorApplicationResponse.From(application);
    }

    /// <summary>Boutiques validées (affichage public du nom et de la ville).</summary>
    public async Task<IReadOnlyList<VendorShopResponse>> ListShopsAsync(CancellationToken ct) =>
        (await applications.ListApprovedAsync(ct)).Select(a => new VendorShopResponse(a.UserId, a.ShopName, a.City)).ToList();

    private static void Translate(Action action) => Translate(() =>
    {
        action();
        return 0;
    });

    private static T Translate<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (VendorRuleException e)
        {
            throw new BusinessRuleException(e.Code, e.Message);
        }
        catch (VendorStateException e)
        {
            throw new ConflictException(e.Message);
        }
    }
}
