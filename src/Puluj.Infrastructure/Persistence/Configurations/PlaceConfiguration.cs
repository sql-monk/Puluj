using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Puluj.Domain.Entities;

namespace Puluj.Infrastructure.Persistence.Configurations;

public class PlaceConfiguration : IEntityTypeConfiguration<Place>
{
    public void Configure(EntityTypeBuilder<Place> b)
    {
        b.HasKey(x => x.PlaceId);
        b.Property(x => x.Name).HasMaxLength(256);
        b.Property(x => x.CountryCode).HasMaxLength(2);
        b.Property(x => x.KatottgCode).HasMaxLength(32);
        b.Property(x => x.NameVariants).HasColumnType("text[]");
        b.Property(x => x.Geometry).HasColumnType("geometry (geometry, 4326)");
        b.Property(x => x.Centroid).HasColumnType("geography (point, 4326)");

        b.HasIndex(x => x.Geometry).HasMethod("gist");
        b.HasIndex(x => x.Centroid).HasMethod("gist");
        b.HasIndex(x => x.NameVariants).HasMethod("gin");
        b.HasIndex(x => new { x.Level, x.CountryCode });
        b.HasIndex(x => x.KatottgCode).IsUnique().HasFilter("katottg_code IS NOT NULL");
        b.Property(x => x.ExternalKey).HasMaxLength(64);
        b.HasIndex(x => x.ExternalKey).IsUnique();

        b.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}
