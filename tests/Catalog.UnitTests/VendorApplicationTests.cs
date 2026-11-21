using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Souqly.Catalog.Application.Vendors;
using Souqly.Catalog.Domain.Vendors;
using Souqly.Chassis.Errors;
using Souqly.Chassis.Security;

namespace Catalog.UnitTests;

/// <summary>US-16 : règles de la demande vendeur (domaine).</summary>
public class VendorApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Une_demande_soumise_est_en_attente()
    {
        var application = VendorApplication.Submit("user-1", "  Atelier Zellige  ", "Fès", "Zellige fait main", Now);

        application.Status.ShouldBe(VendorApplicationStatus.Pending);
        application.ShopName.ShouldBe("Atelier Zellige");
    }

    [Fact]
    public void Un_nom_de_boutique_vide_est_refuse()
    {
        var error = Should.Throw<VendorRuleException>(() => VendorApplication.Submit("user-1", " ", "Fès", "", Now));
        error.Code.ShouldBe("invalid-shop-name");
    }

    [Fact]
    public void Un_refus_doit_etre_motive()
    {
        var application = VendorApplication.Submit("user-1", "Atelier", "Fès", "", Now);

        Should.Throw<VendorRuleException>(() => application.Reject("admin", "  ", Now)).Code.ShouldBe("reason-required");
    }

    [Fact]
    public void Une_demande_deja_traitee_ne_peut_pas_etre_retraitee()
    {
        var application = VendorApplication.Submit("user-1", "Atelier", "Fès", "", Now);
        application.Approve("admin", Now);

        Should.Throw<VendorStateException>(() => application.Reject("admin", "Doublon", Now));
        Should.Throw<VendorStateException>(() => application.Approve("admin", Now));
    }

    [Fact]
    public void Apres_un_refus_le_demandeur_peut_soumettre_a_nouveau()
    {
        var application = VendorApplication.Submit("user-1", "Atelier", "Fès", "", Now);
        application.Reject("admin", "Description trop courte", Now);

        application.Resubmit("Atelier Zellige", "Fès", "Zellige émaillé, 15 ans d'expérience", Now.AddDays(1));

        application.Status.ShouldBe(VendorApplicationStatus.Pending);
        application.RejectionReason.ShouldBeNull();
        application.DecidedBy.ShouldBeNull();
    }

    [Fact]
    public void Une_demande_en_attente_ne_peut_pas_etre_resoumise()
    {
        var application = VendorApplication.Submit("user-1", "Atelier", "Fès", "", Now);

        Should.Throw<VendorStateException>(() => application.Resubmit("Autre", "Fès", "", Now));
    }
}

/// <summary>US-16 : cas d'usage (le rôle VENDOR n'est attribué qu'après validation).</summary>
public class VendorServiceTests
{
    private readonly IVendorApplicationRepository _repository = Substitute.For<IVendorApplicationRepository>();
    private readonly IVendorRoleGranter _roles = Substitute.For<IVendorRoleGranter>();
    private readonly VendorService _service;
    private static readonly CurrentUser Customer = new("user-1", null, new HashSet<string> { Roles.Customer });
    private static readonly CurrentUser Admin = new("admin-1", null, new HashSet<string> { Roles.Admin });

    public VendorServiceTests() => _service = new VendorService(_repository, _roles, TimeProvider.System);

    private static VendorApplicationRequest Request() => new("Atelier Zellige", "Fès", "Zellige fait main");

    [Fact]
    public async Task Un_vendeur_ne_peut_pas_redemander_une_boutique()
    {
        var vendor = new CurrentUser("v-1", null, new HashSet<string> { Roles.Vendor });

        await Should.ThrowAsync<ConflictException>(() => _service.SubmitAsync(Request(), vendor, CancellationToken.None));
    }

    [Fact]
    public async Task Valider_attribue_le_role_VENDOR_puis_enregistre()
    {
        var application = VendorApplication.Submit("user-1", "Atelier", "Fès", "", DateTimeOffset.UtcNow);
        _repository.FindAsync(7, Arg.Any<CancellationToken>()).Returns(application);

        var result = await _service.DecideAsync(7, new VendorDecisionRequest(true, null), Admin, CancellationToken.None);

        result.Status.ShouldBe("APPROVED");
        Received.InOrder(() =>
        {
            _roles.GrantVendorRoleAsync("user-1", Arg.Any<CancellationToken>());
            _repository.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Keycloak_indisponible_donne_503_et_rien_n_est_enregistre()
    {
        var application = VendorApplication.Submit("user-1", "Atelier", "Fès", "", DateTimeOffset.UtcNow);
        _repository.FindAsync(7, Arg.Any<CancellationToken>()).Returns(application);
        _roles.GrantVendorRoleAsync("user-1", Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("down"));

        await Should.ThrowAsync<DependencyUnavailableException>(() =>
            _service.DecideAsync(7, new VendorDecisionRequest(true, null), Admin, CancellationToken.None));
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuser_n_attribue_aucun_role()
    {
        var application = VendorApplication.Submit("user-1", "Atelier", "Fès", "", DateTimeOffset.UtcNow);
        _repository.FindAsync(7, Arg.Any<CancellationToken>()).Returns(application);

        var result = await _service.DecideAsync(7, new VendorDecisionRequest(false, "Pièces justificatives manquantes"), Admin, CancellationToken.None);

        result.Status.ShouldBe("REJECTED");
        result.RejectionReason.ShouldBe("Pièces justificatives manquantes");
        await _roles.DidNotReceiveWithAnyArgs().GrantVendorRoleAsync(default!, default);
    }

    [Fact]
    public async Task Un_statut_de_filtre_inconnu_est_une_erreur_metier()
    {
        var error = await Should.ThrowAsync<BusinessRuleException>(() => _service.ListAsync("PAID", null, null, CancellationToken.None));
        error.Code.ShouldBe("invalid-status");
    }
}
