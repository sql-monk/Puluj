using NetTopologySuite.Geometries;
using Puluj.Contracts;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Api.Services;

/// <summary>Entity to DTO mapping. Every DTO carries the data the client needs to explain what it shows and how sure we are.</summary>
public sealed class DtoMapper(ReferenceCache refs)
{
    public ThreatDto Threat(int categoryId, int? classId, int? familyId, int? modelId)
    {
        var cat = refs.Categories.TryGetValue(categoryId, out var c) ? c : null;
        var cls = classId is int ci && refs.Classes.TryGetValue(ci, out var k) ? k : null;
        var fam = familyId is int fi && refs.Families.TryGetValue(fi, out var f) ? f : null;
        var mod = modelId is int mi && refs.Models.TryGetValue(mi, out var m) ? m : null;
        var (display, fade) = refs.Display(classId);
        return new ThreatDto(cat?.Code ?? "UNKNOWN", cat?.Name ?? "Невідомо",
            cls?.Code, cls?.Name, fam?.Code, fam?.Name, mod?.Code, mod?.CanonicalName,
            display, fade, refs.SpeedProfile(classId, modelId));
    }

    public LocationDto? Location(LocationKind kind, int? placeId, Geometry? geometry, double? accuracyKm)
    {
        if (kind == LocationKind.Unknown && placeId is null && geometry is null)
        {
            return null;
        }
        var place = refs.Place(placeId);
        var region = refs.RegionOf(placeId);
        var point = geometry?.Centroid;
        if (point is not null)
        {
            point.SRID = Geo.Srid;
        }
        return new LocationDto(kind.ToString(), placeId, place?.Name, region?.Id, region?.Id == place?.Id ? null : region?.Name, point, accuracyKm);
    }

    public LocationDto? PlaceLocation(int? placeId)
    {
        var place = refs.Place(placeId);
        if (place is null)
        {
            return null;
        }
        var region = refs.RegionOf(placeId);
        var kind = place.Level switch
        {
            PlaceLevel.Region or PlaceLevel.Country => LocationKind.Region,
            PlaceLevel.NamedArea => LocationKind.Area,
            PlaceLevel.District or PlaceLevel.Hromada => LocationKind.District,
            _ => LocationKind.City,
        };
        return new LocationDto(kind.ToString(), place.Id, place.Name, region?.Id, region?.Id == place.Id ? null : region?.Name, Geo.Point(place.Lon, place.Lat), place.RadiusKm);
    }

    /// <summary>The position an observation reported: its location, else the destination it named (an approach).</summary>
    public FixDto? Fix(Observation o)
    {
        if (o.Location is not null && o.LocationKind != LocationKind.DirectionOnly)
        {
            var p = o.Location.Centroid;
            p.SRID = Geo.Srid;
            return new FixDto(o.ObservedAt, refs.Place(o.LocationPlaceId)?.Name, o.LocationKind.ToString(), p, o.LocationAccuracyKm, false);
        }
        var destId = o.LocationKind == LocationKind.DirectionOnly ? (o.LocationPlaceId ?? o.DestinationPlaceId) : o.DestinationPlaceId;
        if (refs.Place(destId) is { } dest)
        {
            return new FixDto(o.ObservedAt, dest.Name, LocationKind.DirectionOnly.ToString(), Geo.Point(dest.Lon, dest.Lat), dest.RadiusKm, true);
        }
        return null;
    }

    public static DirectionDto? Direction(DirectionKind kind, double? deg, ConfidenceLevel confidence) =>
        deg is double d && kind != DirectionKind.Unknown ? new DirectionDto(Math.Round(d), kind.ToString(), confidence.ToString()) : null;

    public TrackDto Track(ThreatTrack t, IReadOnlyList<int> sourceIds, IReadOnlyList<FixDto>? fixes = null, IReadOnlyList<long>? messageIds = null) => new(
        t.ThreatTrackId, t.Status.ToString(), t.ClosedReason,
        Threat(t.ThreatCategoryId, t.ThreatClassId, t.ThreatFamilyId, t.ThreatModelId),
        t.ModelConfidence.ToString(), t.TrackConfidence.ToString(),
        t.FirstSeenAt, t.LastSeenAt, t.UpdatedAt,
        Location(t.LastLocationKind, t.LastLocationPlaceId, t.LastLocation, t.LastLocationAccuracyKm),
        t.TrackGeometry, Direction(t.DirectionKind, t.DirectionDeg, t.DirectionConfidence),
        t.ObjectCount, t.ObservationCount, t.DistinctSourceCount, sourceIds, fixes ?? [], messageIds ?? []);

    /// <summary>A track as it was at the time of the revision (historical mode).</summary>
    public TrackDto Track(ThreatTrackRevision r, IReadOnlyList<int> sourceIds, IReadOnlyList<FixDto>? fixes = null, IReadOnlyList<long>? messageIds = null) => new(
        r.ThreatTrackId, r.Status.ToString(), null,
        Threat(r.ThreatCategoryId, r.ThreatClassId, r.ThreatFamilyId, r.ThreatModelId),
        r.ModelConfidence.ToString(), r.TrackConfidence.ToString(),
        r.LastSeenAt, r.LastSeenAt, r.RevisionAt,
        Location(r.LastLocationKind, r.LastLocationPlaceId, r.LastLocation, r.LastLocationAccuracyKm),
        r.TrackGeometry, Direction(r.DirectionKind, r.DirectionDeg, r.DirectionConfidence),
        r.ObjectCount, r.ObservationCount, Math.Max(1, sourceIds.Count), sourceIds, fixes ?? [], messageIds ?? []);

    public AlertDto Alert(AirAlert a) =>
        new(a.AirAlertId, a.PlaceId, refs.Place(a.PlaceId)?.Name ?? $"#{a.PlaceId}", a.AlertType.ToString(), a.Level.ToString(), a.StartedAt, a.EndedAt, PlaceLocation(a.PlaceId));

    public SourceDto Source(Source s) => new(s.SourceId, s.Code, s.Name, s.Type.ToString(), s.TrustLevel, s.Url);

    public static RawMessageDto RawMessage(RawMessage r) => new(r.RawMessageId, r.SourceMessageId, r.PublishedAt, r.ReceivedAt, r.RawText, r.Url);

    public ObservationDto Observation(Observation o, double? associationConfidence, long? trackId = null)
    {
        var source = refs.Sources.TryGetValue(o.SourceId, out var s) ? Source(s) : new SourceDto(o.SourceId, "?", "?", "?", 0, null);
        return new ObservationDto(
            o.ObservationId, o.ObservedAt, o.EventType.ToString(),
            o.ThreatCategoryId is int cat ? Threat(cat, o.ThreatClassId, o.ThreatFamilyId, o.ThreatModelId) : null,
            o.ModelConfidence.ToString(), o.ClassificationConfidence.ToString(), o.ObservationConfidence.ToString(),
            Location(o.LocationKind, o.LocationPlaceId, o.Location, o.LocationAccuracyKm),
            PlaceLocation(o.OriginPlaceId), PlaceLocation(o.DestinationPlaceId),
            Direction(o.DirectionKind, o.DirectionDeg, o.DirectionConfidence),
            o.ObjectCount, o.ObjectCountIsApproximate,
            o.IdentificationMethod.ToString(), o.IdentificationSource, o.SegmentText, o.DuplicateOfObservationId,
            associationConfidence, source, RawMessage(o.RawMessage!), trackId);
    }

    public PlaceDto Place(ReferenceCache.PlaceInfo p) =>
        new(p.Id, p.Name, p.Level.ToString(), p.ParentId, refs.Place(p.ParentId)?.Name, p.Lon, p.Lat, p.RadiusKm, p.Population);
}
