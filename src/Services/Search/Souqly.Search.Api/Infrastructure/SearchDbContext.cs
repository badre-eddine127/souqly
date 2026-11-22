using Microsoft.EntityFrameworkCore;
using Souqly.Messaging;
using Souqly.Search.Api.Domain;

namespace Souqly.Search.Api.Infrastructure;

/// <summary>Base search_db : l'index de recherche (PostgreSQL + pg_trgm, ADR-015) et l'inbox des événements.</summary>
public sealed class SearchDbContext(DbContextOptions<SearchDbContext> options) : DbContext(options)
{
    public DbSet<SearchDocument> Documents => Set<SearchDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.AddMessagingTables();
        modelBuilder.Entity<SearchDocument>(b =>
        {
            b.ToTable("search_documents");
            b.HasKey(d => d.ProductId);
            b.Property(d => d.ProductId).ValueGeneratedNever();
            b.Property(d => d.VendorId).HasMaxLength(64).IsRequired();
            b.Property(d => d.Sku).HasMaxLength(64).IsRequired();
            b.Property(d => d.Name).HasMaxLength(160).IsRequired();
            b.Property(d => d.Description).HasMaxLength(4000).IsRequired();
            b.Property(d => d.Price).HasPrecision(12, 2);
            b.Property(d => d.Currency).HasMaxLength(3).IsRequired();
            b.Property(d => d.SearchText).HasMaxLength(4200).IsRequired();
            // Index trigrammes : recherche tolérante aux fautes sans parcourir toute la table.
            b.HasIndex(d => d.SearchText).HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(d => new { d.CategoryId, d.Price });
        });
    }
}
