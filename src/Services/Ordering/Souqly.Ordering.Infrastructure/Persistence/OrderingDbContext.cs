using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souqly.Ordering.Domain;

namespace Souqly.Ordering.Infrastructure.Persistence;

/// <summary>Base ordering_db (propriété exclusive du service Ordering).</summary>
public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(ConfigureOrder);
        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("idempotency_keys");
            b.HasKey(i => new { i.CustomerId, i.Key });   // une clé n'a de sens que pour un client donné
            b.Property(i => i.CustomerId).HasMaxLength(64);
            b.Property(i => i.RequestHash).HasMaxLength(64).IsRequired();
            b.HasIndex(i => i.CreatedAt);   // purge des clés de plus de 24 h (sprint 3)
        });
    }

    private static void ConfigureOrder(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders", t => t.HasCheckConstraint("ck_orders_total_positive", "total > 0"));
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).ValueGeneratedNever();
        b.Property(o => o.CustomerId).HasMaxLength(64).IsRequired();
        b.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(o => o.StatusReason).HasMaxLength(64);
        b.Property(o => o.Total).HasPrecision(12, 2);
        b.Property(o => o.Version).IsRowVersion();
        b.HasIndex(o => new { o.CustomerId, o.CreatedAt });
        b.HasIndex(o => o.Status);

        // Les lignes appartiennent à la commande (agrégat) : table order_lines, chargée avec elle.
        b.OwnsMany(o => o.Lines, line =>
        {
            line.ToTable("order_lines", t => t.HasCheckConstraint("ck_order_lines_quantity", "quantity BETWEEN 1 AND 10"));
            line.WithOwner().HasForeignKey("OrderId");
            line.Property<int>("Id").UseIdentityAlwaysColumn();
            line.HasKey("Id");
            line.Property(l => l.VendorId).HasMaxLength(64).IsRequired();
            line.Property(l => l.ProductName).HasMaxLength(120).IsRequired();
            line.Property(l => l.UnitPrice).HasPrecision(12, 2);   // prix figé (RG-02)
            line.Ignore(l => l.LineTotal);
            line.HasIndex(l => l.VendorId);
        });
        b.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
