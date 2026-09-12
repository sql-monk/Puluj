using NetTopologySuite.Geometries;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;
using Puluj.Processing.Indexes;

namespace Puluj.Processing.Correlation;

/// <summary>Applies an observation to a track and produces the append-only revision (spec §20). Pure functions over entities.</summary>
public static class TrackUpdater
{
    public static ThreatTrack CreateTrack(Observation o, DateTimeOffset now, PlaceEntry? destination = null, double? approachBearing = null)
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
        Apply(t, o, now, isNewer: true, destination, approachBearing);
        return t;
    }

    /// <summary>
    /// Merges the observation into the track. Older observations (out-of-order delivery) add provenance but do not move the marker.
    /// UpdatedAt is event time (never earlier than before), so revisions replay the situation as it unfolded, not as it was processed.
    /// </summary>
    /// <param name="destination">The place the observation says the object is heading to, when it names one.</param>
    /// <param name="approachBearing">Course from the hostile side towards that destination (GazetteerIndex.ApproachBearingTo), when known.</param>
    public static void Apply(ThreatTrack t, Observation o, DateTimeOffset now, bool isNewer, PlaceEntry? destination = null, double? approachBearing = null)
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
            // An approach-zone anchor ("на Конотоп") is not a fix: the path never starts or passes there.
            var previousIsFix = previous is not null && t.LastLocationKind != LocationKind.DirectionOnly;
            var previousAccuracy = t.LastLocationAccuracyKm ?? 0;
            var accuracy = o.LocationAccuracyKm ?? 0;
            var dist = previous is null ? 0 : Geo.DistanceKm(previous.Centroid.Coordinate, o.Location.Centroid.Coordinate);
            // Two coarse positions only prove a move when their areas do not overlap: a bearing between the centres of
            // two adjacent oblasts is noise, and a line between them is not a route anyone flew.
            var moved = previousIsFix && dist > 20 && dist > previousAccuracy + accuracy;
            // A coarse previous position seeds the line only when the move away from it is real.
            var previousOnPath = previousIsFix && (previousAccuracy <= PathPointKm || moved);
            t.LastLocation = o.Location;
            t.LastLocationKind = o.LocationKind;
            t.LastLocationPlaceId = o.LocationPlaceId;
            t.LastLocationAccuracyKm = o.LocationAccuracyKm;
            // The observed path is made of facts: precise positions, or coarse ones only when the move itself is real.
            if (accuracy <= PathPointKm || moved)
            {
                AppendToGeometry(t, previousOnPath ? previous!.Centroid : null, o.Location.Centroid);
            }

            // Real movement between located observations gives a direction when the source reported none;
            // it never overrides a direction the source stated.
            if (o.DirectionDeg is null && moved && (t.DirectionDeg is null || t.DirectionConfidence <= ConfidenceLevel.Low))
            {
                t.DirectionDeg = Geo.BearingDeg(previous!.Centroid.Coordinate, o.Location.Centroid.Coordinate);
                t.DirectionKind = DirectionKind.TowardsPlace;
                t.DirectionConfidence = ConfidenceLevel.Low;
            }
        }
        else if (o.Location is null && destination is not null)
        {
            ApplyDestinationOnly(t, o, destination, approachBearing);
        }
        if (o.DirectionDeg is double deg)
        {
            t.DirectionDeg = deg;
            t.DirectionKind = o.DirectionKind;
            t.DirectionConfidence = o.DirectionConfidence;
        }
    }

    /// <summary>
    /// "БпЛА курсом на Конотоп" is the newest word on where the object is: on the approach to that place. The marker
    /// moves there (an approximate position, drawn pale), whatever the track knew before — a report "на Сумщині"
    /// ten minutes ago must not keep the marker on the oblast centroid. A precise previous fix still yields the
    /// course towards the named place; the approach anchor never joins the observed path.
    /// </summary>
    private static void ApplyDestinationOnly(ThreatTrack t, Observation o, PlaceEntry destination, double? approachBearing)
    {
        var accuracy = Math.Max(destination.RadiusKm, Correlator.DestinationAnchorKm);
        if (approachBearing is double bearing)
        {
            // Nothing else is known: the object is short of the destination on the side threats come from, heading in.
            var anchor = Pipeline.ObservationBuilder.ApproachAnchor(destination, bearing);
            t.LastLocation = anchor.Point;
            t.LastLocationKind = LocationKind.DirectionOnly;
            t.LastLocationPlaceId = destination.PlaceId;
            t.LastLocationAccuracyKm = Math.Max(anchor.AccuracyKm, Correlator.DestinationAnchorKm);
            if (o.DirectionDeg is null && (t.DirectionDeg is null || t.DirectionConfidence <= ConfidenceLevel.Low))
            {
                t.DirectionDeg = bearing;
                t.DirectionKind = DirectionKind.TowardsPlace;
                t.DirectionConfidence = ConfidenceLevel.Low;
            }
            return;
        }
        if (t.LastLocation is not null && t.LastLocationKind != LocationKind.DirectionOnly)
        {
            var from = t.LastLocation.Centroid.Coordinate;
            var dist = Geo.DistanceKm(from, destination.Centroid.Coordinate);
            var precise = (t.LastLocationAccuracyKm ?? 0) <= PathPointKm;
            if (o.DirectionDeg is null && precise && dist > 5 && t.LastLocationPlaceId != destination.PlaceId
                && (t.DirectionDeg is null || t.DirectionConfidence <= ConfidenceLevel.Low))
            {
                t.DirectionDeg = Geo.BearingDeg(from, destination.Centroid.Coordinate);
                t.DirectionKind = DirectionKind.TowardsPlace;
                t.DirectionConfidence = ConfidenceLevel.Low;
            }
        }
        t.LastLocation = destination.Centroid;
        t.LastLocationKind = LocationKind.DirectionOnly;
        t.LastLocationPlaceId = destination.PlaceId;
        t.LastLocationAccuracyKm = accuracy;
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

    /// <summary>Positions this precise are drawn on the observed path even without a detectable move.</summary>
    private const double PathPointKm = 50;

    /// <summary>The line starts with the first located observation (kept only as LastLocation until a second point arrives).</summary>
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
