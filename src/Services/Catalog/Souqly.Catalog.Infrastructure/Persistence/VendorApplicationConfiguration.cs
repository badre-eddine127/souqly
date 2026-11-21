using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souqly.Catalog.Domain.Vendors;

namespace Souqly.Catalog.Infrastructure.Persistence;

internal sealed class VendorApplicationConfiguration : IEntityTypeConfiguration<VendorApplication>
{
    public void Configure(EntityTypeBuilder<VendorApplication> builder)
    {
        builder.ToTable("vendor_applications");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();
        builder.Property(a => a.UserId).HasMaxLength(64).IsRequired();
        builder.Property(a => a.ShopName).HasMaxLength(VendorApplication.ShopNameMaxLength).IsRequired();
        builder.Property(a => a.City).HasMaxLength(VendorApplication.CityMaxLength).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(VendorApplication.DescriptionMaxLength).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.RejectionReason).HasMaxLength(VendorApplication.ReasonMaxLength);
        builder.Property(a => a.DecidedBy).HasMaxLength(64);
        builder.Property(a => a.Version).IsRowVersion();

        // Une seule demande par utilisateur (elle est réutilisée après un refus).
        builder.HasIndex(a => a.UserId).IsUnique();
        builder.HasIndex(a => new { a.Status, a.SubmittedAt });
    }
}
