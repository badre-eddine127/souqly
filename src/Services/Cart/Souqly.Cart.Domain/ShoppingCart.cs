namespace Souqly.Cart.Domain;

/// <summary>
/// Panier d'un client (US-05). Il ne contient que des produits et des quantités :
/// le prix est lu dans le catalogue au moment de la commande (RG-02), jamais stocké ici.
/// Règles RG-04 : 1 à 10 unités par ligne, 20 lignes au plus.
/// </summary>
public sealed class ShoppingCart
{
    public const int MaxLines = 20;
    public const int MaxQuantity = 10;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private readonly Dictionary<long, int> _lines;

    public ShoppingCart(string customerId, IDictionary<long, int>? lines = null, DateTimeOffset? updatedAt = null)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new CartRuleException("customer-required", "Un panier appartient toujours à un client.");
        }

        CustomerId = customerId;
        _lines = lines is null ? [] : new Dictionary<long, int>(lines);
        UpdatedAt = updatedAt ?? DateTimeOffset.MinValue;
    }

    public string CustomerId { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyDictionary<long, int> Lines => _lines;

    public int TotalUnits => _lines.Values.Sum();

    /// <summary>Ajoute un produit ou fixe sa quantité (PUT idempotent).</summary>
    public void SetQuantity(long productId, int quantity, DateTimeOffset now)
    {
        if (productId <= 0)
        {
            throw new CartRuleException("invalid-product", "Identifiant de produit invalide.");
        }

        if (quantity is < 1 or > MaxQuantity)
        {
            throw new CartRuleException("invalid-quantity", $"La quantité par ligne va de 1 à {MaxQuantity}.");
        }

        if (!_lines.ContainsKey(productId) && _lines.Count >= MaxLines)
        {
            throw new CartRuleException("too-many-lines", $"Un panier contient au plus {MaxLines} produits différents.");
        }

        _lines[productId] = quantity;
        UpdatedAt = now;
    }

    /// <summary>Retire un produit ; sans effet s'il n'y est pas.</summary>
    public void Remove(long productId, DateTimeOffset now)
    {
        if (_lines.Remove(productId))
        {
            UpdatedAt = now;
        }
    }

    public void Clear(DateTimeOffset now)
    {
        _lines.Clear();
        UpdatedAt = now;
    }
}

public sealed class CartRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Port de stockage du panier (Redis).</summary>
public interface ICartStore
{
    Task<ShoppingCart?> FindAsync(string customerId, CancellationToken ct);

    /// <summary>Enregistre le panier et repousse son expiration de 30 jours.</summary>
    Task SaveAsync(ShoppingCart cart, CancellationToken ct);

    Task DeleteAsync(string customerId, CancellationToken ct);
}
