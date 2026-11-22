using System.Text;

namespace Souqly.Search.Api.Domain;

/// <summary>
/// Un produit tel que la recherche le voit : seulement les produits en vente (ACTIVE).
/// Copie dénormalisée tenue à jour par les événements de Catalog : Search ne lit jamais la base de Catalog.
/// </summary>
public sealed class SearchDocument
{
    private SearchDocument()
    {
        VendorId = Sku = Name = Description = Currency = SearchText = string.Empty;
    }

    public long ProductId { get; private set; }
    public string VendorId { get; private set; }
    public long CategoryId { get; private set; }
    public string Sku { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; }

    /// <summary>Date de la version de Catalog reflétée ici : une version plus ancienne n'écrase jamais une plus récente.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Nom et description normalisés (minuscules, sans accents) : c'est sur ce texte que portent les trigrammes.</summary>
    public string SearchText { get; private set; }

    public static SearchDocument Create(long productId) => new() { ProductId = productId };

    /// <returns>false si la version reçue est plus ancienne que celle déjà indexée (rien n'est modifié).</returns>
    public bool Apply(string vendorId, long categoryId, string sku, string name, string description, decimal price, string currency, DateTimeOffset updatedAt)
    {
        if (ProductId != 0 && UpdatedAt > updatedAt)
        {
            return false;
        }

        VendorId = vendorId;
        CategoryId = categoryId;
        Sku = sku;
        Name = name;
        Description = description;
        Price = price;
        Currency = currency;
        UpdatedAt = updatedAt;
        SearchText = TextNormalizer.Normalize($"{name} {description}");
        return true;
    }
}

/// <summary>
/// Normalisation identique pour l'index et pour la requête : « Tapis berbère, Azilal ! » → « tapis berbere azilal ».
/// Faite en C# (et non avec unaccent() de PostgreSQL, qui n'est pas utilisable dans un index).
/// </summary>
public static class TextNormalizer
{
    private static readonly Dictionary<char, string> Folding = new()
    {
        ['à'] = "a",
        ['â'] = "a",
        ['ä'] = "a",
        ['á'] = "a",
        ['ã'] = "a",
        ['ç'] = "c",
        ['é'] = "e",
        ['è'] = "e",
        ['ê'] = "e",
        ['ë'] = "e",
        ['î'] = "i",
        ['ï'] = "i",
        ['í'] = "i",
        ['ô'] = "o",
        ['ö'] = "o",
        ['ó'] = "o",
        ['ù'] = "u",
        ['û'] = "u",
        ['ü'] = "u",
        ['ú'] = "u",
        ['ÿ'] = "y",
        ['ñ'] = "n",
        ['œ'] = "oe",
        ['æ'] = "ae",
    };

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = new StringBuilder(text.Length);
        var space = true;
        foreach (var raw in text)
        {
            var c = char.ToLowerInvariant(raw);
            if (Folding.TryGetValue(c, out var folded))
            {
                result.Append(folded);
                space = false;
            }
            else if (char.IsLetterOrDigit(c))
            {
                result.Append(c);
                space = false;
            }
            else if (!space)
            {
                result.Append(' ');   // ponctuation, apostrophes, tirets : séparateurs de mots
                space = true;
            }
        }

        return result.ToString().TrimEnd();
    }
}
