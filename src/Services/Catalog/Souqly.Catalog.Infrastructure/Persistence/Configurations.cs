using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souqly.Catalog.Domain.Categories;
using Souqly.Catalog.Domain.Products;

namespace Souqly.Catalog.Infrastructure.Persistence;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", t =>
        {
            t.HasCheckConstraint("ck_products_price_positive", "price > 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).UseIdentityAlwaysColumn();
        builder.Property(p => p.VendorId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Sku).HasMaxLength(Product.SkuMaxLength).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(Product.NameMaxLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength).IsRequired();
        builder.Property(p => p.Price).HasPrecision(12, 2);   // RG-08 : NUMERIC(12,2)
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);

        // Concurrence optimiste sur la colonne système xmin de PostgreSQL.
        builder.Property(p => p.Version).IsRowVersion();

        builder.HasOne<Category>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.VendorId, p.Sku }).IsUnique();
        builder.HasIndex(p => new { p.Status, p.CategoryId, p.Price });
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Slug).HasMaxLength(60).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();
        builder.HasIndex(c => c.Slug).IsUnique();
        builder.HasOne<Category>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);

        // Catégories de référence, versionnées avec le schéma.
        builder.HasData(
            new Category(1, "artisanat", "Artisanat", null),
            new Category(2, "tapis", "Tapis et kilims", 1),
            new Category(3, "poterie", "Poterie et céramique", 1),
            new Category(4, "cuir", "Maroquinerie", 1),
            new Category(5, "cosmetique", "Cosmétique naturelle", null),
            new Category(6, "argan", "Huile d'argan", 5),
            new Category(7, "epicerie", "Épicerie fine", null),
            new Category(8, "mode", "Mode et caftans", null));
    }
}
