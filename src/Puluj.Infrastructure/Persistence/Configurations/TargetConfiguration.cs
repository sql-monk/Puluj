using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Puluj.Domain.Entities;

namespace Puluj.Infrastructure.Persistence.Configurations;

public class TargetConfiguration : IEntityTypeConfiguration<Target>
{
    public void Configure(EntityTypeBuilder<Target> b)
    {
        b.HasKey(x => x.TargetId);
        b.Property(x => x.IdentificationSource).HasMaxLength(256);
        b.Property(x => x.ParserVersion).HasMaxLength(32);
        b.Property(x => x.ParserMetadata).HasColumnType("jsonb");
        b.Property(x => x.Location).HasColumnType("geography (geometry, 4326)");

        b.HasIndex(x => x.Location).HasMethod("gist");
        b.HasIndex(x => x.ObservedAt).HasMethod("brin");
        b.HasIndex(x => new { x.TargetClassId, x.ObservedAt });
        b.HasIndex(x => x.RawMessageId);
        b.HasIndex(x => x.DuplicateOfTargetId);

        b.HasOne(x => x.RawMessage).WithMany(x => x.Targets).HasForeignKey(x => x.RawMessageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LocationPlace).WithMany().HasForeignKey(x => x.LocationPlaceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Place>().WithMany().HasForeignKey(x => x.OriginPlaceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Place>().WithMany().HasForeignKey(x => x.DestinationPlaceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TargetCategory>().WithMany().HasForeignKey(x => x.TargetCategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TargetClass>().WithMany().HasForeignKey(x => x.TargetClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TargetFamily>().WithMany().HasForeignKey(x => x.TargetFamilyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TargetModel>().WithMany().HasForeignKey(x => x.TargetModelId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Target>().WithMany().HasForeignKey(x => x.DuplicateOfTargetId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AirAlertConfiguration : IEntityTypeConfiguration<AirAlert>
{
    public void Configure(EntityTypeBuilder<AirAlert> b)
    {
        b.HasKey(x => x.AirAlertId);
        b.Property(x => x.SourceAlertId).HasMaxLength(128);
        b.HasIndex(x => new { x.SourceId, x.SourceAlertId }).IsUnique();
        b.HasIndex(x => x.PlaceId).HasFilter("ended_at IS NULL");
        b.HasIndex(x => x.StartedAt).HasMethod("brin");
        b.HasOne(x => x.Place).WithMany().HasForeignKey(x => x.PlaceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Source>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
    }
}
