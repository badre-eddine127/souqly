namespace Souqly.Catalog.Domain.Products;

/// <summary>Une règle métier du produit n'est pas respectée (traduite en 422).</summary>
public sealed class ProductRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Transition d'état interdite (traduite en 409).</summary>
public sealed class ProductStateException(string message) : Exception(message);
