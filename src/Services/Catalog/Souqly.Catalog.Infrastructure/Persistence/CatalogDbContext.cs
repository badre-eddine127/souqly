using Microsoft.EntityFrameworkCore;
using Souqly.Catalog.Domain.Categories;
using Souqly.Catalog.Domain.Products;
using Souqly.Catalog.Domain.Vendors;

namespace Souqly.Catalog.Infrastructure.Persistence;

/// <summary>Base catalog_db : seul ce service y lit et y écrit (une base par service).</summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<VendorApplication> VendorApplications => Set<VendorApplication>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
}
