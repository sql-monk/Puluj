using NetTopologySuite.Geometries;

namespace Puluj.Contracts;

/// <summary>Class/model behaviour the client needs for fading and ETA (spec §12, §17). Speeds are ranges, never a single number.</summary>
public sealed record SpeedProfileDto(double? MinKmh, double? MaxKmh, bool EtaEnabled);

public sealed record ThreatDto(
    string CategoryCode, string CategoryName,
    string? ClassCode, string? ClassName,
    string? FamilyCode, string? FamilyName,
    string? ModelCode, string? ModelName,
    string DisplayMode, int FadeMinutes, SpeedProfileDto SpeedProfile)
{
    /// <summary>Deepest identified level, for labels ("Shahed-136", "Shahed family", "Крилата ракета").</summary>
    public string Label => ModelName ?? FamilyName ?? ClassName ?? CategoryName;
}

public sealed record LocationDto(string Kind, int? PlaceId, string? PlaceName, int? RegionId, string? RegionName, Point? Point, double? AccuracyKm);

public sealed record DirectionDto(double Degrees, string Kind, string Confidence);

public sealed record TrackDto(
    long Id,
    string Status,
    string? ClosedReason,
    ThreatDto Threat,
    string ModelConfidence,
    string TrackConfidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset UpdatedAt,
    LocationDto? LastLocation,
    LineString? TrackGeometry,
    DirectionDto? Direction,
    int? ObjectCount,
    int ObservationCount,
    int DistinctSourceCount);

/// <param name="Level">Unknown | Yellow | Red (regional administrations publish levels; alerts.in.ua does not).</param>
/// <param name="Location">Where to draw it when the place has no polygon (raion towns): point + radius.</param>
public sealed record AlertDto(long Id, int PlaceId, string PlaceName, string AlertType, string Level, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, LocationDto? Location);

public sealed record SnapshotDto(DateTimeOffset At, bool Historical, IReadOnlyList<TrackDto> Tracks, IReadOnlyList<AlertDto> Alerts);

public sealed record SourceDto(int Id, string Code, string Name, string Type, double TrustLevel, string? Url);

public sealed record RawMessageDto(long Id, string SourceMessageId, DateTimeOffset PublishedAt, DateTimeOffset ReceivedAt, string? Text, string? Url);

/// <summary>One observation with its full provenance chain (spec §18, §31).</summary>
public sealed record ObservationDto(
    long Id,
    DateTimeOffset ObservedAt,
    string EventType,
    ThreatDto? Threat,
    string ModelConfidence,
    string ClassificationConfidence,
    string ObservationConfidence,
    LocationDto? Location,
    LocationDto? Origin,
    LocationDto? Destination,
    DirectionDto? Direction,
    int? ObjectCount,
    bool ObjectCountIsApproximate,
    string IdentificationMethod,
    string? IdentificationSource,
    string? SegmentText,
    long? DuplicateOfObservationId,
    double? AssociationConfidence,
    SourceDto Source,
    RawMessageDto RawMessage);

public sealed record TrackDetailsDto(TrackDto Track, IReadOnlyList<ObservationDto> Observations);

public sealed record PlaceDto(int Id, string Name, string Level, int? ParentId, string? ParentName, double Lon, double Lat, double RadiusKm, int Population);

/// <param name="ParentId">Set for city districts (Kyiv): the city-region they belong to.</param>
public sealed record RegionDto(int Id, string Name, string Level, string CountryCode, int? ParentId, Geometry Geometry);

public sealed record TaxonomyModelDto(int Id, string Code, string Name, string? Manufacturer, string? Country, SpeedProfileDto SpeedProfile);
public sealed record TaxonomyFamilyDto(int Id, string Code, string Name, IReadOnlyList<TaxonomyModelDto> Models);
public sealed record TaxonomyClassDto(int Id, string Code, string Name, string DisplayMode, int FadeMinutes, SpeedProfileDto SpeedProfile, IReadOnlyList<TaxonomyFamilyDto> Families);
public sealed record TaxonomyCategoryDto(int Id, string Code, string Name, IReadOnlyList<TaxonomyClassDto> Classes);
public sealed record TaxonomyDto(IReadOnlyList<TaxonomyCategoryDto> Categories);

public sealed record TimelineBucketDto(DateTimeOffset From, int Observations, int TracksOpened, int Alerts);

/// <summary>Development-only: inject a message as a collector would. Payload (optional) is stored as RawPayload, e.g. an alerts.in.ua alert.</summary>
public sealed record IngestRequest(string SourceCode, string? Text, DateTimeOffset? PublishedAt, string? SourceMessageId, System.Text.Json.JsonElement? Payload);
