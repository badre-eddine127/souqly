using Microsoft.EntityFrameworkCore;
using Souqly.Messaging;
using Souqly.Payment.Api.Domain;

namespace Souqly.Payment.Api.Infrastructure;

/// <summary>Base payment_db.</summary>
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Payment> Payments => Set<Domain.Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddMessagingTables();
        modelBuilder.Entity<Domain.Payment>(b =>
        {
            b.ToTable("payments", t => t.HasCheckConstraint("ck_payments_amount_positive", "amount > 0"));
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.HasIndex(p => p.OrderId).IsUnique();   // un seul paiement par commande, même si deux messages arrivent ensemble
            b.HasIndex(p => p.CustomerId);
            b.Property(p => p.CustomerId).HasMaxLength(64).IsRequired();
            b.Property(p => p.Amount).HasPrecision(12, 2);
            b.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            b.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(p => p.FailureReason).HasMaxLength(64);
            b.Property(p => p.Version).IsRowVersion();
        });
    }
}

public static class PaymentStatusNames
{
    public static string Of(PaymentStatus status) => status.ToString().ToUpperInvariant();
}
