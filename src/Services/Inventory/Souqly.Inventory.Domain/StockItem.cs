namespace Souqly.Inventory.Domain;

/// <summary>
/// Stock d'un produit. Invariant RG-01 : 0 ≤ reserved ≤ onHand, donc la disponibilité n'est jamais négative.
/// La réservation concurrente ne passe PAS par cette entité (lecture puis écriture = course possible) :
/// elle est faite par un UPDATE conditionnel atomique en base (ADR-007).
/// </summary>
public sealed class StockItem
{
    public const int MaxOnHand = 1_000_000;

    /// <summary>Seuil d'alerte par défaut (US-12) : prévenir quand il reste 3 unités disponibles ou moins.</summary>
    public const int DefaultLowStockThreshold = 3;

    public const int MaxLowStockThreshold = 1000;

    private StockItem()
    {
        VendorId = string.Empty;
    }

    public long ProductId { get; private set; }
    public string VendorId { get; private set; }
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }

    /// <summary>US-12 : le vendeur est alerté quand la disponibilité passe à ce seuil ou en dessous.</summary>
    public int LowStockThreshold { get; private set; } = DefaultLowStockThreshold;

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Jeton de concurrence optimiste (xmin) pour les modifications faites par le vendeur.</summary>
    public uint Version { get; private set; }

    public int Available => OnHand - Reserved;

    public static StockItem Declare(long productId, string vendorId, int onHand, DateTimeOffset now)
    {
        if (productId <= 0)
        {
            throw new StockRuleException("invalid-product", "Identifiant de produit invalide.");
        }

        var item = new StockItem { ProductId = productId, VendorId = vendorId };
        item.SetOnHand(onHand, now);
        return item;
    }

    public bool IsOwnedBy(string vendorId) => string.Equals(VendorId, vendorId, StringComparison.Ordinal);

    /// <summary>US-12 : seuil d'alerte choisi par le vendeur (0 = alerte seulement à la rupture).</summary>
    public void SetLowStockThreshold(int threshold, DateTimeOffset now)
    {
        if (threshold is < 0 or > MaxLowStockThreshold)
        {
            throw new StockRuleException("invalid-threshold", $"Le seuil d'alerte doit être compris entre 0 et {MaxLowStockThreshold}.");
        }

        LowStockThreshold = threshold;
        UpdatedAt = now;
    }

    /// <summary>
    /// US-12 : vrai seulement au franchissement du seuil (on était au-dessus, on est à ou sous le seuil) :
    /// une alerte par passage, pas une à chaque vente suivante.
    /// </summary>
    public bool CrossedLowStock(int availableBefore) => availableBefore > LowStockThreshold && Available <= LowStockThreshold;

    /// <summary>US-10 : le vendeur fixe son stock physique. Il ne peut pas descendre sous ce qui est déjà réservé.</summary>
    public void SetOnHand(int onHand, DateTimeOffset now)
    {
        if (onHand is < 0 or > MaxOnHand)
        {
            throw new StockRuleException("invalid-quantity", $"Le stock doit être compris entre 0 et {MaxOnHand}.");
        }

        if (onHand < Reserved)
        {
            throw new StockRuleException("below-reserved",
                $"Impossible de descendre à {onHand} : {Reserved} unité(s) sont réservées par des commandes en cours.");
        }

        OnHand = onHand;
        UpdatedAt = now;
    }

    /// <summary>US-10 : ajustement relatif (réception, casse, inventaire).</summary>
    public void Adjust(int delta, DateTimeOffset now)
    {
        if (delta == 0)
        {
            throw new StockRuleException("invalid-delta", "Un ajustement nul n'a pas de sens.");
        }

        SetOnHand(OnHand + delta, now);
    }
}

public sealed class StockRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
