using NetTopologySuite.Geometries;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Processing.Correlation;

/// <summary>Applies an observation to a track and produces the append-only revision (spec §20). Pure functions over entities.</summary>
public static class TrackUpdater
{
    public static ThreatTrack CreateTrack(Observation o, DateTimeOffset now)
    {
        var t = new ThreatTrack
        {
            Status = TrackStatus.Active,
            ThreatCategoryId = o.ThreatCategoryId!.Value,
            FirstSeenAt = o.ObservedAt,
            LastSeenAt = o.ObservedAt,
            UpdatedAt = o.ObservedAt,
            DirectionKind = DirectionKind.Unknown,
        };
        Apply(t, o, now, isNewer: true);
        return t;
    }

    /// <summary>
    /// Merges the observation into the track. Older observations (out-of-order delivery) add provenance but do not move the marker.
    /// UpdatedAt is event time (never earlier than before), so revisions replay the situation as it unfolded, not as it was processed.
    /// </summary>
    public static void Apply(ThreatTrack t, Observation o, DateTimeOffset now, bool isNewer)
    {
        t.ObservationCount++;
        if (o.ObservedAt > t.UpdatedAt)
        {
            t.UpdatedAt = o.ObservedAt;
        }
        if (o.ObservedAt < t.FirstSeenAt)
        {
            t.FirstSeenAt = o.ObservedAt;
        }

        // Classification only becomes more specific, never less; equal specificity keeps the higher confidence.
        var oDepth = Depth(o.ThreatModelId, o.ThreatFamilyId, o.ThreatClassId);
        var tDepth = Depth(t.ThreatModelId, t.ThreatFamilyId, t.ThreatClassId);
        if (oDepth > tDepth || (oDepth == tDepth && o.ModelConfidence > t.ModelConfidence))
        {
            t.ThreatClassId = o.ThreatClassId ?? t.ThreatClassId;
            t.ThreatFamilyId = o.ThreatFamilyId ?? t.ThreatFamilyId;
            t.ThreatModelId = o.ThreatModelId ?? t.ThreatModelId;
            t.ModelConfidence = o.ModelConfidence;
        }
        if (o.ObjectCount is int n && (t.ObjectCount is null || isNewer))
        {
            t.ObjectCount = n;
        }

        if (!isNewer)
        {
            return;
        }

        t.LastSeenAt = o.ObservedAt;
        t.LastSourceId = o.SourceId;
        t.LastObservationId = o.ObservationId;
        // "з Чернігівщини на Київ" locates the observation at a coarse origin only for want of anything better; once the
        // track has a position, repeating such an origin must not drag the marker (and the path) back to it.
        // A precise origin ("з Броварів") is a real position and moves the track.
        var locatedAtOrigin = o.LocationPlaceId is not null && o.LocationPlaceId == o.OriginPlaceId && t.LastLocation is not null
            && (o.LocationAccuracyKm ?? 0) > PathPointKm;
        if (o.Location is not null && !locatedAtOrigin)
        {
            var previous = t.LastLocation;
            var previousAccuracy = t.LastLocationAccuracyKm ?? 0;
            var accuracy = o.LocationAccuracyKm ?? 0;
            var dist = previous is null ? 0 : Geo.DistanceKm(previous.Centroid.Coordinate, o.Location.Centroid.Coordinate);
            // A step between two coarse positions goes on the path when it is larger than the areas themselves
            // (adjacent oblasts qualify); it yields a direction only when it exceeds both uncertainties combined,
            // because a bearing between two area centres is otherwise noise.
            var stepped = previous is not null && dist > 20 && dist > 0.5 * (previousAccuracy + accuracy);
            var moved = stepped && dist > previousAccuracy + accuracy;
            t.LastLocation = o.Location;
            t.LastLocationKind = o.LocationKind;
            t.LastLocationPlaceId = o.LocationPlaceId;
            t.LastLocationAccuracyKm = o.LocationAccuracyKm;
            // The observed path is made of facts: precise positions, or coarse ones only when the move itself is real.
            if (accuracy <= PathPointKm || stepped)
            {
                AppendToGeometry(t, previous?.Centroid, o.Location.Centroid);
            }

            // Real movement between located observations gives a direction when the source reported none;
            // it never overrides a direction the source stated.
            if (o.DirectionDeg is null && moved && previous is not null && (t.DirectionDeg is null || t.DirectionConfidence <= ConfidenceLevel.Low))
            {
                t.DirectionDeg = Geo.BearingDeg(previous.Centroid.Coordinate, o.Location.Centroid.Coordinate);
                t.DirectionKind = DirectionKind.TowardsPlace;
                t.DirectionConfidence = ConfidenceLevel.Low;
            }
        }
        if (o.DirectionDeg is double deg)
        {
            t.DirectionDeg = deg;
            t.DirectionKind = o.DirectionKind;
            t.DirectionConfidence = o.DirectionConfidence;
        }
    }

    /// <summary>Track confidence grows with corroboration: more observations, more independent sources (spec §15).</summary>
    public static ConfidenceLevel ComputeTrackConfidence(ThreatTrack t, ConfidenceLevel bestObservationConfidence)
    {
        var score = (int)bestObservationConfidence;
        if (t.DistinctSourceCount >= 2)
        {
            score++;
        }
        if (t.ObservationCount >= 3)
        {
            score++;
        }
        return (ConfidenceLevel)Math.Clamp(score, (int)ConfidenceLevel.Low, (int)ConfidenceLevel.High);
    }

    public static ThreatTrackRevision Revision(ThreatTrack t, long? observationId, DateTimeOffset at) => new()
    {
        Track = t,
        RevisionAt = at,
        Status = t.Status,
        ThreatCategoryId = t.ThreatCategoryId,
        ThreatClassId = t.ThreatClassId,
        ThreatFamilyId = t.ThreatFamilyId,
        ThreatModelId = t.ThreatModelId,
        ModelConfidence = t.ModelConfidence,
        LastSeenAt = t.LastSeenAt,
        LastLocationKind = t.LastLocationKind,
        LastLocationPlaceId = t.LastLocationPlaceId,
        LastLocation = t.LastLocation,
        LastLocationAccuracyKm = t.LastLocationAccuracyKm,
        TrackGeometry = t.TrackGeometry,
        DirectionKind = t.DirectionKind,
        DirectionDeg = t.DirectionDeg,
        DirectionConfidence = t.DirectionConfidence,
        ObjectCount = t.ObjectCount,
        TrackConfidence = t.TrackConfidence,
        ObservationCount = t.ObservationCount,
        ObservationId = observationId,
    };

    /// <summary>The line starts with the first located observation (kept only as LastLocation until a second point arrives).</summary>
    /// <summary>Positions this precise are drawn on the observed path even without a detectable move.</summary>
    private const double PathPointKm = 50;

    private static void AppendToGeometry(ThreatTrack t, Point? previous, Point p)
    {
        var coords = t.TrackGeometry?.Coordinates.ToList() ?? (previous is null ? [] : [previous.Coordinate.Copy()]);
        if (coords.Count > 0 && coords[^1].Equals2D(p.Coordinate))
        {
            return;
        }
        coords.Add(p.Coordinate.Copy());
        if (coords.Count >= 2)
        {
            t.TrackGeometry = Geo.Factory.CreateLineString(coords.ToArray());
        }
    }

    private static int Depth(int? model, int? family, int? cls) => model is not null ? 3 : family is not null ? 2 : cls is not null ? 1 : 0;
}
