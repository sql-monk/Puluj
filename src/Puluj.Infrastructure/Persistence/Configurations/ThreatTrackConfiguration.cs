using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;

namespace Puluj.Infrastructure.Persistence.Configurations;

public class ThreatTrackConfiguration : IEntityTypeConfiguration<ThreatTrack>
{
    public void Configure(EntityTypeBuilder<ThreatTrack> b)
    {
        b.HasKey(x => x.ThreatTrackId);
        b.Property(x => x.ClosedReason).HasMaxLength(256);
        b.Property(x => x.LastLocation).HasColumnType("geography (geometry, 4326)");
        b.Property(x => x.TrackGeometry).HasColumnType("geometry (linestring, 4326)");

        b.HasIndex(x => x.LastLocation).HasMethod("gist");
        b.HasIndex(x => new { x.Status, x.LastSeenAt });
        b.HasIndex(x => new { x.ThreatClassId, x.Status });

        b.HasOne<ThreatCategory>().WithMany().HasForeignKey(x => x.ThreatCategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ThreatClass>().WithMany().HasForeignKey(x => x.ThreatClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ThreatFamily>().WithMany().HasForeignKey(x => x.ThreatFamilyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ThreatModel>().WithMany().HasForeignKey(x => x.ThreatModelId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Place>().WithMany().HasForeignKey(x => x.LastLocationPlaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ThreatTrackObservationConfiguration : IEntityTypeConfiguration<ThreatTrackObservation>
{
    public void Configure(EntityTypeBuilder<ThreatTrackObservation> b)
    {
        b.HasKey(x => new { x.ThreatTrackId, x.ObservationId });
        b.Property(x => x.AssociationReason).HasColumnType("jsonb");
        b.HasIndex(x => x.ObservationId);
        b.HasOne(x => x.Track).WithMany(x => x.Observations).HasForeignKey(x => x.ThreatTrackId);
        b.HasOne(x => x.Observation).WithMany().HasForeignKey(x => x.ObservationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ThreatTrackRevisionConfiguration : IEntityTypeConfiguration<ThreatTrackRevision>
{
    public void Configure(EntityTypeBuilder<ThreatTrackRevision> b)
    {
        b.HasKey(x => x.ThreatTrackRevisionId);
        b.Property(x => x.LastLocation).HasColumnType("geography (geometry, 4326)");
        b.Property(x => x.TrackGeometry).HasColumnType("geometry (linestring, 4326)");
        // Replay query: latest revision per track with revision_at <= T.
        b.HasIndex(x => new { x.ThreatTrackId, x.RevisionAt });
        b.HasIndex(x => x.RevisionAt).HasMethod("brin");
        b.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.ThreatTrackId);
    }
}
