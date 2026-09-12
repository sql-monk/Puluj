using System.Text.Json;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;
using Puluj.Processing.Indexes;

namespace Puluj.Processing.Correlation;

/// <summary>Breakdown of an association score (stored in ThreatTrackObservation.AssociationReason).</summary>
public sealed record AssociationScore(double Total, double Time, double Space, double Direction, double Class, double DistanceKm, double MaxDistanceKm, double MinutesApart)
{
    public JsonDocument ToJson() => JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        total = Math.Round(Total, 3),
        time = Math.Round(Time, 3),
        space = Math.Round(Space, 3),
        direction = Math.Round(Direction, 3),
        @class = Math.Round(Class, 3),
        distanceKm = Math.Round(DistanceKm, 1),
        maxDistanceKm = Math.Round(MaxDistanceKm, 1),
        minutesApart = Math.Round(MinutesApart, 1),
    }));
}

/// <summary>
/// Pure scoring of "does this observation continue that track" (spec §10): type, model/family, time, geography,
/// direction, count. Deterministic and side-effect free so it can be unit-tested on synthetic scenarios.
/// </summary>
public static class Correlator
{
    private const double DefaultSpeedKmh = 200;
    private const double SameSourceSplitMinutes = 8;

    public static bool ClassCompatible(Observation o, ThreatTrack t)
    {
        if (o.ThreatCategoryId is null || o.ThreatCategoryId != t.ThreatCategoryId)
        {
            return false;
        }
        return o.ThreatClassId is null || t.ThreatClassId is null || o.ThreatClassId == t.ThreatClassId;
    }

    public static AssociationScore Score(Observation o, ThreatTrack t, ClassProfile? profile, double slackKm)
    {
        var windowMin = profile?.CorrelationWindowMinutes ?? 30;
        var minutes = Math.Abs((o.ObservedAt - t.LastSeenAt).TotalMinutes);
        var time = Math.Clamp(1 - minutes / windowMin, 0, 1);

        double space, distance = 0, maxDistance = 0;
        if (o.Location is not null && t.LastLocation is not null)
        {
            distance = Geo.DistanceKm(o.Location.Centroid.Coordinate, t.LastLocation.Centroid.Coordinate);
            var speed = profile?.SpeedKmhMax ?? DefaultSpeedKmh;
            maxDistance = speed * minutes / 60 + (o.LocationAccuracyKm ?? 0) + (t.LastLocationAccuracyKm ?? 0) + slackKm;
            space = distance <= maxDistance ? 1 - 0.5 * distance / maxDistance : 0;
        }
        else if (o.LocationPlaceId is not null && o.LocationPlaceId == t.LastLocationPlaceId)
        {
            space = 0.8;
        }
        else
        {
            space = 0.4; // one side has no usable location; time/class decide
        }

        double direction;
        if (o.DirectionDeg is double od && t.DirectionDeg is double td)
        {
            direction = 1 - Geo.AngleDiffDeg(od, td) / 180;
        }
        else if (t.LastLocation is not null && o.Location is not null && t.DirectionDeg is double td2
            && distance > 15 + (t.LastLocationAccuracyKm ?? 0) + (o.LocationAccuracyKm ?? 0))
        {
            // Did the object move roughly where the track was heading?
            var bearing = Geo.BearingDeg(t.LastLocation.Centroid.Coordinate, o.Location.Centroid.Coordinate);
            direction = 1 - Geo.AngleDiffDeg(bearing, td2) / 180;
        }
        else
        {
            direction = 0.5;
        }

        double cls;
        if (o.ThreatModelId is not null && o.ThreatModelId == t.ThreatModelId)
        {
            cls = 1;
        }
        else if (o.ThreatModelId is not null && t.ThreatModelId is not null)
        {
            cls = 0.2; // both specific and different (Kh-101 vs Kalibr)
        }
        else if (o.ThreatFamilyId is not null && o.ThreatFamilyId == t.ThreatFamilyId)
        {
            cls = 0.9;
        }
        else if (o.ThreatFamilyId is not null && t.ThreatFamilyId is not null)
        {
            cls = 0.3;
        }
        else
        {
            cls = 0.7; // same class, one side unspecific
        }

        var total = 0.30 * time + 0.35 * space + 0.15 * direction + 0.20 * cls;
        if (space == 0)
        {
            total = Math.Min(total, 0.3); // physically impossible jump: never attach
        }
        // The same source naming two different areas within a few minutes is reporting two objects, not one moving
        // faster than region-level accuracy can tell (typical "БпЛА на Сумщині / БпЛА на Чернігівщині" lists).
        if (o.SourceId == t.LastSourceId && minutes < SameSourceSplitMinutes
            && o.LocationPlaceId is not null && t.LastLocationPlaceId is not null && o.LocationPlaceId != t.LastLocationPlaceId
            && distance > 2 * (profile?.SpeedKmhMax ?? DefaultSpeedKmh) * minutes / 60 + 20)
        {
            total = Math.Min(total, 0.3);
        }
        return new AssociationScore(total, time, space, direction, cls, distance, maxDistance, minutes);
    }
}
