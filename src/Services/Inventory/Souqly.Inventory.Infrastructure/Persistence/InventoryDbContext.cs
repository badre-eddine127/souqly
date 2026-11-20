using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souqly.Inventory.Domain;

namespace Souqly.Inventory.Infrastructure.Persistence;

/// <summary>Base inventory_db.</summary>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockItem>(ConfigureStockItem);
        modelBuilder.Entity<Reservation>(ConfigureReservation);
    }

    private static void ConfigureStockItem(EntityTypeBuilder<StockItem> b)
    {
        // RG-01 garanti aussi par la base : même un bug applicatif ne peut pas rendre le stock négatif.
        b.ToTable("stock_items", t =>
        {
            t.HasCheckConstraint("ck_stock_on_hand_positive", "on_hand >= 0");
            t.HasCheckConstraint("ck_stock_reserved_positive", "reserved >= 0");
            t.HasCheckConstraint("ck_stock_reserved_le_on_hand", "reserved <= on_hand");
        });
        b.HasKey(s => s.ProductId);
        b.Property(s => s.ProductId).ValueGeneratedNever();
        b.Property(s => s.VendorId).HasMaxLength(64).IsRequired();
        b.Property(s => s.Version).IsRowVersion();
        b.Ignore(s => s.Available);
        b.HasIndex(s => s.VendorId);
    }

    private static void ConfigureReservation(EntityTypeBuilder<Reservation> b)
    {
        b.ToTable("reservations");
        b.HasKey(r => r.Id);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        b.Ignore(r => r.IsActive);
        // Idempotence : une seule réservation par (commande, produit).
        b.HasIndex(r => new { r.OrderId, r.ProductId }).IsUnique();
        // Index partiel pour la tâche d'expiration.
        b.HasIndex(r => r.ExpiresAt).HasFilter("status = 'Active'");
        b.HasOne<StockItem>().WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
