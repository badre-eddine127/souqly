namespace Souqly.Catalog.Domain.Categories;

/// <summary>Catégorie du catalogue, organisée en arbre (ex. Artisanat › Tapis).</summary>
public sealed class Category
{
    private Category()
    {
        Slug = Name = string.Empty;
    }

    public Category(long id, string slug, string name, long? parentId)
    {
        Id = id;
        Slug = slug;
        Name = name;
        ParentId = parentId;
    }

    public long Id { get; private set; }
    public string Slug { get; private set; }
    public string Name { get; private set; }
    public long? ParentId { get; private set; }
}
