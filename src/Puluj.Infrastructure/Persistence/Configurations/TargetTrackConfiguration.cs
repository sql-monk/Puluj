using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;

namespace Puluj.Infrastructure.Persistence.Configurations;

public class TargetTrackConfiguration : IEntityTypeConfiguration<TargetTrack>
{
    public void Configure(EntityTypeBuilder<TargetTrack> b)
    {
        b.HasKey(x => x.TargetTrackId);
        b.Property(x => x.ClosedReason).HasMaxLength(256);
        b.Property(x => x.LastLocation).HasColumnType("geography (geometry, 4326)");
        b.Property(x => x.TrackGeometry).HasColumnType("geometry (linestring, 4326)");

        b.HasIndex(x => x.LastLocation).HasMethod("gist");
        b.HasIndex(x => new { x.Status, x.LastSeenAt });
        // Correlation candidates: active or timed-out tracks last seen inside the window (an OR the status index cannot serve).
        b.HasIndex(x => x.LastSeenAt);
        b.HasIndex(x => new { x.TargetClassId, x.Status });

        b.HasOne<TargetCategory>().WithMany().HasForeignKey(x => x.TargetCategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TargetClass>().WithMany().HasForeignKey(x => x.TargetClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TargetFamily>().WithMany().HasForeignKey(x => x.TargetFamilyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TargetModel>().WithMany().HasForeignKey(x => x.TargetModelId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Place>().WithMany().HasForeignKey(x => x.LastLocationPlaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TrackTargetConfiguration : IEntityTypeConfiguration<TrackTarget>
{
    public void Configure(EntityTypeBuilder<TrackTarget> b)
    {
        b.HasKey(x => new { x.TargetTrackId, x.TargetId });
        b.Property(x => x.AssociationReason).HasColumnType("jsonb");
        b.HasIndex(x => x.TargetId);
        b.HasOne(x => x.Track).WithMany(x => x.Targets).HasForeignKey(x => x.TargetTrackId);
        b.HasOne(x => x.Target).WithMany().HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TargetTrackRevisionConfiguration : IEntityTypeConfiguration<TargetTrackRevision>
{
    public void Configure(EntityTypeBuilder<TargetTrackRevision> b)
    {
        b.HasKey(x => x.TargetTrackRevisionId);
        b.Property(x => x.LastLocation).HasColumnType("geography (geometry, 4326)");
        b.Property(x => x.TrackGeometry).HasColumnType("geometry (linestring, 4326)");
        // Replay query: latest revision per track with revision_at <= T.
        b.HasIndex(x => new { x.TargetTrackId, x.RevisionAt });
        b.HasIndex(x => x.RevisionAt).HasMethod("brin");
        b.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TargetTrackId);
    }
}
