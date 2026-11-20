using Microsoft.EntityFrameworkCore;
using Souqly.Messaging;

namespace Souqly.Notification.Api.Infrastructure;

/// <summary>Base notification_db.</summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Notification> Notifications => Set<Domain.Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddMessagingTables();
        modelBuilder.Entity<Domain.Notification>(b =>
        {
            b.ToTable("notifications");
            b.HasKey(n => n.Id);
            b.Property(n => n.Id).ValueGeneratedNever();
            b.Property(n => n.RecipientId).HasMaxLength(64).IsRequired();
            b.Property(n => n.Kind).HasMaxLength(32).IsRequired();
            b.Property(n => n.Title).HasMaxLength(120).IsRequired();
            b.Property(n => n.Body).HasMaxLength(1000).IsRequired();
            b.HasIndex(n => new { n.RecipientId, n.CreatedAt });
        });
    }
}
