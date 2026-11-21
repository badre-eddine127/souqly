namespace Souqly.Catalog.Domain.Vendors;

/// <summary>
/// Demande d'ouverture de boutique (US-16). Un utilisateur a au plus une demande : refusée, il peut la soumettre à nouveau ;
/// en attente ou validée, elle ne bouge plus côté demandeur. Seul un administrateur décide.
/// </summary>
public sealed class VendorApplication
{
    public const int ShopNameMaxLength = 80;
    public const int CityMaxLength = 60;
    public const int DescriptionMaxLength = 1000;
    public const int ReasonMaxLength = 500;

    private VendorApplication()
    {
        // Pour EF Core.
        UserId = ShopName = City = Description = string.Empty;
    }

    public long Id { get; private set; }

    /// <summary>Identifiant Keycloak (sub) du demandeur.</summary>
    public string UserId { get; private set; }

    public string ShopName { get; private set; }
    public string City { get; private set; }
    public string Description { get; private set; }
    public VendorApplicationStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Identifiant (sub) de l'administrateur qui a tranché.</summary>
    public string? DecidedBy { get; private set; }

    public uint Version { get; private set; }

    public bool IsApproved => Status == VendorApplicationStatus.Approved;

    public static VendorApplication Submit(string userId, string shopName, string city, string description, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new VendorRuleException("user-required", "Une demande appartient toujours à un utilisateur.");
        }

        var application = new VendorApplication { UserId = userId };
        application.Fill(shopName, city, description, now);
        return application;
    }

    /// <summary>Nouvelle soumission après un refus : la demande repasse en attente.</summary>
    public void Resubmit(string shopName, string city, string description, DateTimeOffset now)
    {
        if (Status != VendorApplicationStatus.Rejected)
        {
            throw new VendorStateException(Status == VendorApplicationStatus.Approved
                ? "Votre boutique est déjà validée."
                : "Votre demande est déjà en cours d'examen.");
        }

        Fill(shopName, city, description, now);
    }

    public void Approve(string adminId, DateTimeOffset now)
    {
        EnsurePending();
        Status = VendorApplicationStatus.Approved;
        RejectionReason = null;
        Decide(adminId, now);
    }

    public void Reject(string adminId, string reason, DateTimeOffset now)
    {
        EnsurePending();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new VendorRuleException("reason-required", "Un refus doit être motivé.");
        }

        Status = VendorApplicationStatus.Rejected;
        RejectionReason = Truncate(reason.Trim(), ReasonMaxLength);
        Decide(adminId, now);
    }

    private void Fill(string shopName, string city, string description, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(shopName) || shopName.Trim().Length > ShopNameMaxLength)
        {
            throw new VendorRuleException("invalid-shop-name", $"Le nom de la boutique est obligatoire ({ShopNameMaxLength} caractères au plus).");
        }

        if (string.IsNullOrWhiteSpace(city) || city.Trim().Length > CityMaxLength)
        {
            throw new VendorRuleException("invalid-city", $"La ville est obligatoire ({CityMaxLength} caractères au plus).");
        }

        ShopName = shopName.Trim();
        City = city.Trim();
        Description = Truncate((description ?? string.Empty).Trim(), DescriptionMaxLength);
        Status = VendorApplicationStatus.Pending;
        RejectionReason = null;
        SubmittedAt = now;
        DecidedAt = null;
        DecidedBy = null;
    }

    private void EnsurePending()
    {
        if (Status != VendorApplicationStatus.Pending)
        {
            throw new VendorStateException("Cette demande a déjà été traitée.");
        }
    }

    private void Decide(string adminId, DateTimeOffset now)
    {
        DecidedBy = adminId;
        DecidedAt = now;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

public enum VendorApplicationStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>Règle métier d'une demande vendeur (traduite en 422).</summary>
public sealed class VendorRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Transition interdite sur une demande vendeur (traduite en 409).</summary>
public sealed class VendorStateException(string message) : Exception(message);

/// <summary>Port de persistance des demandes vendeur.</summary>
public interface IVendorApplicationRepository
{
    Task<VendorApplication?> FindAsync(long id, CancellationToken ct);

    Task<VendorApplication?> FindByUserAsync(string userId, CancellationToken ct);

    Task<(IReadOnlyList<VendorApplication> Items, long Total)> ListAsync(VendorApplicationStatus? status, int skip, int take, CancellationToken ct);

    Task<IReadOnlyList<VendorApplication>> ListApprovedAsync(CancellationToken ct);

    void Add(VendorApplication application);

    Task SaveChangesAsync(CancellationToken ct);
}
