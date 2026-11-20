namespace Souqly.Catalog.Domain.Products;

/// <summary>
/// Produit mis en vente par un vendeur. L'entité protège ses invariants :
/// prix strictement positif (MAD, 2 décimales), nom et SKU obligatoires, transitions de statut contrôlées.
/// </summary>
public sealed class Product
{
    public const int NameMaxLength = 120;
    public const int SkuMaxLength = 40;
    public const int DescriptionMaxLength = 4000;
    public const decimal MaxPrice = 50_000m;

    private Product()
    {
        // Pour EF Core.
        VendorId = Sku = Name = Description = string.Empty;
    }

    public long Id { get; private set; }
    public string VendorId { get; private set; }
    public long CategoryId { get; private set; }
    public string Sku { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public decimal Price { get; private set; }
    public ProductStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Jeton de concurrence optimiste (colonne système xmin de PostgreSQL).</summary>
    public uint Version { get; private set; }

    public static Product Create(string vendorId, long categoryId, string sku, string name, string description,
        decimal price, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(vendorId))
        {
            throw new ProductRuleException("vendor-required", "Un produit appartient toujours à un vendeur.");
        }

        var product = new Product
        {
            VendorId = vendorId,
            Status = ProductStatus.Draft,
            CreatedAt = now,
        };
        product.Apply(categoryId, sku, name, description, price, now);
        return product;
    }

    public void Update(long categoryId, string sku, string name, string description, decimal price, DateTimeOffset now)
    {
        EnsureNotArchived();
        Apply(categoryId, sku, name, description, price, now);
    }

    /// <summary>Publie le produit : il devient visible dans le catalogue public.</summary>
    public void Publish(DateTimeOffset now)
    {
        EnsureNotArchived();
        Status = ProductStatus.Active;
        UpdatedAt = now;
    }

    /// <summary>Archive le produit (suppression logique) : il disparaît du catalogue mais reste lié aux anciennes commandes.</summary>
    public void Archive(DateTimeOffset now)
    {
        if (Status == ProductStatus.Archived)
        {
            return;
        }

        Status = ProductStatus.Archived;
        UpdatedAt = now;
    }

    public bool IsOwnedBy(string vendorId) => string.Equals(VendorId, vendorId, StringComparison.Ordinal);

    public bool IsPubliclyVisible => Status == ProductStatus.Active;

    private void Apply(long categoryId, string sku, string name, string description, decimal price, DateTimeOffset now)
    {
        if (categoryId <= 0)
        {
            throw new ProductRuleException("category-required", "La catégorie est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
        {
            throw new ProductRuleException("invalid-name", $"Le nom est obligatoire et fait au plus {NameMaxLength} caractères.");
        }

        if (string.IsNullOrWhiteSpace(sku) || sku.Trim().Length > SkuMaxLength)
        {
            throw new ProductRuleException("invalid-sku", $"Le SKU est obligatoire et fait au plus {SkuMaxLength} caractères.");
        }

        if (price <= 0 || price > MaxPrice || decimal.Round(price, 2) != price)
        {
            throw new ProductRuleException("invalid-price", $"Le prix doit être compris entre 0,01 et {MaxPrice} MAD, avec au plus 2 décimales.");
        }

        CategoryId = categoryId;
        Sku = sku.Trim().ToUpperInvariant();
        Name = name.Trim();
        Description = (description ?? string.Empty).Trim();
        Price = price;
        UpdatedAt = now;
    }

    private void EnsureNotArchived()
    {
        if (Status == ProductStatus.Archived)
        {
            throw new ProductStateException($"Le produit {Id} est archivé et ne peut plus être modifié.");
        }
    }
}

public enum ProductStatus
{
    Draft,
    Active,
    Archived,
}
