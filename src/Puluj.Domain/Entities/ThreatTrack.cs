using System.Text.Json;
using NetTopologySuite.Geometries;
using Puluj.Domain.Enums;

namespace Puluj.Domain.Entities;

/// <summary>Spec §10. Logical object formed from one or more observations.</summary>
public class ThreatTrack
{
    public long ThreatTrackId { get; set; }
    public TrackStatus Status { get; set; } = TrackStatus.Active;
    public string? ClosedReason { get; set; }

    public int ThreatCategoryId { get; set; }
    public int? ThreatClassId { get; set; }
    public int? ThreatFamilyId { get; set; }
    public int? ThreatModelId { get; set; }
    public ConfidenceLevel ModelConfidence { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    /// <summary>Event time of the last state change (observation time, or closure time). Revisions are stamped with it.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public LocationKind LastLocationKind { get; set; }
    public int? LastLocationPlaceId { get; set; }
    public Geometry? LastLocation { get; set; }
    public double? LastLocationAccuracyKm { get; set; }
    /// <summary>Centroids of successive observations, oldest first. Null until 2+ located observations.</summary>
    public LineString? TrackGeometry { get; set; }

    public DirectionKind DirectionKind { get; set; }
    public double? DirectionDeg { get; set; }
    public ConfidenceLevel DirectionConfidence { get; set; }
    public int? ObjectCount { get; set; }

    public ConfidenceLevel TrackConfidence { get; set; }
    public int ObservationCount { get; set; }
    public int DistinctSourceCount { get; set; }
    /// <summary>Source and id of the newest observation, used by the correlator to keep one source's parallel reports apart.</summary>
    public int? LastSourceId { get; set; }
    public long? LastObservationId { get; set; }

    public ICollection<ThreatTrackObservation> Observations { get; set; } = [];
}

/// <summary>Spec §10 link table.</summary>
public class ThreatTrackObservation
{
    public long ThreatTrackId { get; set; }
    public ThreatTrack? Track { get; set; }
    public long ObservationId { get; set; }
    public Observation? Observation { get; set; }
    public int Sequence { get; set; }
    /// <summary>0..1 score produced by the correlator.</summary>
    public double AssociationConfidence { get; set; }
    /// <summary>Breakdown of the score (time/space/direction/class components).</summary>
    public JsonDocument? AssociationReason { get; set; }
}

/// <summary>Append-only snapshot of a track after every change: the basis for historical replay (spec §20).</summary>
public class ThreatTrackRevision
{
    public long ThreatTrackRevisionId { get; set; }
    public long ThreatTrackId { get; set; }
    public ThreatTrack? Track { get; set; }
    public DateTimeOffset RevisionAt { get; set; }
    public TrackStatus Status { get; set; }
    public int ThreatCategoryId { get; set; }
    public int? ThreatClassId { get; set; }
    public int? ThreatFamilyId { get; set; }
    public int? ThreatModelId { get; set; }
    public ConfidenceLevel ModelConfidence { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public LocationKind LastLocationKind { get; set; }
    public int? LastLocationPlaceId { get; set; }
    public Geometry? LastLocation { get; set; }
    public double? LastLocationAccuracyKm { get; set; }
    public LineString? TrackGeometry { get; set; }
    public DirectionKind DirectionKind { get; set; }
    public double? DirectionDeg { get; set; }
    public ConfidenceLevel DirectionConfidence { get; set; }
    public int? ObjectCount { get; set; }
    public ConfidenceLevel TrackConfidence { get; set; }
    public int ObservationCount { get; set; }
    /// <summary>Observation that triggered this revision, if any.</summary>
    public long? ObservationId { get; set; }
}
