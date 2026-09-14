using NetTopologySuite.Geometries;

namespace Puluj.Contracts;

/// <summary>Class/model behaviour the client needs for fading and ETA (spec §12, §17). Speeds are ranges, never a single number.</summary>
public sealed record SpeedProfileDto(double? MinKmh, double? MaxKmh, bool EtaEnabled);

public sealed record TargetTypeDto(
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

/// <summary>One earlier reported position of a track ("was near Romny at 21:40"): the crumbs drawn behind the marker.</summary>
/// <param name="Approach">The report only named a destination: the point is that place, the object was on the way to it.</param>
/// <param name="Probability">That this earlier report is the same object as the next one in the chain (1 for the current position).</param>
public sealed record FixDto(DateTimeOffset At, string? PlaceName, string Kind, Point Point, double? AccuracyKm, bool Approach, double Probability);

public sealed record TrackDto(
    long Id,
    string Status,
    string? ClosedReason,
    TargetTypeDto Type,
    string ModelConfidence,
    string TrackConfidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset UpdatedAt,
    LocationDto? LastLocation,
    LineString? TrackGeometry,
    DirectionDto? Direction,
    int? ObjectCount,
    int TargetCount,
    int DistinctSourceCount,
    IReadOnlyList<int> SourceIds,
    /// <summary>The last few distinct reported positions, oldest first, the current one last.</summary>
    IReadOnlyList<FixDto> Fixes,
    /// <summary>Raw messages behind the newest targets: tracks sharing one are "neighbours by message".</summary>
    IReadOnlyList<long> MessageIds);

/// <param name="Level">Unknown | Yellow | Red (regional administrations publish levels; alerts.in.ua does not).</param>
/// <param name="Location">Where to draw it when the place has no polygon (raion towns): point + radius.</param>
public sealed record AlertDto(long Id, int PlaceId, string PlaceName, string AlertType, string Level, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, LocationDto? Location);

public sealed record SnapshotDto(DateTimeOffset At, bool Historical, IReadOnlyList<TrackDto> Tracks, IReadOnlyList<AlertDto> Alerts);

/// <summary>One reported position of a track in a replay window: where it was said to be, when, and on what course.</summary>
public sealed record ReplaySampleDto(DateTimeOffset At, Point Point, double? DirectionDeg, bool Approach);
/// <summary>A track over a replay window: its class and every reported position in order, oldest first.</summary>
public sealed record ReplayTrackDto(long Id, TargetTypeDto Type, IReadOnlyList<ReplaySampleDto> Samples);
/// <summary>Everything a timelapse of the window needs in one payload; the client interpolates between the samples.</summary>
public sealed record ReplayDto(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReplayTrackDto> Tracks);

public sealed record SourceDto(int Id, string Code, string Name, string Type, double TrustLevel, string? Url);

public sealed record RawMessageDto(long Id, string SourceMessageId, DateTimeOffset PublishedAt, DateTimeOffset ReceivedAt, string? Text, string? Url);

/// <summary>One target with its full provenance chain (spec §18, §31).</summary>
public sealed record TargetDto(
    long Id,
    DateTimeOffset ObservedAt,
    string EventType,
    TargetTypeDto? Type,
    string ModelConfidence,
    string ClassificationConfidence,
    string Confidence,
    LocationDto? Location,
    LocationDto? Origin,
    LocationDto? Destination,
    DirectionDto? Direction,
    int? ObjectCount,
    bool ObjectCountIsApproximate,
    string IdentificationMethod,
    string? IdentificationSource,
    string? SegmentText,
    long? DuplicateOfTargetId,
    double? AssociationConfidence,
    SourceDto Source,
    RawMessageDto RawMessage,
    long? TrackId,
    IReadOnlyList<TargetLinkDto> Links);

/// <summary>A link from this target to another: which one, how ("continuation" = kinematic predecessor, "duplicate"), how probable,
/// whether the other one is earlier ("from") or later ("to"), and the kinematics behind the number.</summary>
/// <summary>A probable earlier report of the same object: generation 1 = directly before the track's newest target, 2 = before that.</summary>
/// <summary>
/// A node of the selected target's family. Generation = how many generations above the head (0 = the head's own,
/// i.e. siblings and cousins). Ancestral = on the head's own ancestry (parent, grandparent); otherwise a relative:
/// where an ancestor could have flown instead. DisplayMode / TypeLabel / DirectionDeg let the map draw the node as
/// the target it is (class glyph, turned by its course).
/// </summary>
public sealed record PredecessorDto(long TargetId, int Generation, bool Ancestral, DateTimeOffset At, string? PlaceName, string Kind, Point? Point, double? AccuracyKm, bool Approach, string? Label,
    string DisplayMode, string TypeLabel, double? DirectionDeg);

/// <param name="Probability">The link's own probability.</param>
/// <param name="PathProbability">Product of the probabilities from the head down to this link.</param>
/// <summary>Generation = the generation of `From` above the head. Ancestral = a link on the head's own ancestry.</summary>
public sealed record PredecessorLinkDto(long FromTargetId, long ToTargetId, int Generation, bool Ancestral, string Kind, double Probability, double PathProbability);

public sealed record PredecessorsDto(long TrackId, long HeadTargetId, IReadOnlyList<PredecessorDto> Targets, IReadOnlyList<PredecessorLinkDto> Links);

public sealed record TargetLinkDto(long TargetId, string Kind, double Probability, string Direction, double? DistanceKm, double? MinutesApart, double? HeadingDiffDeg, double? RequiredMinutes);

/// <summary>Source rating over time: per-day counters and the earned rating (0..1), plus who copies whom and the groups that follow.</summary>
public sealed record SourceRatingDayDto(DateOnly Day, int Targets, int Copies, int CopiedBy, double? AvgLeadSeconds, double? Rating);
public sealed record SourceRatingDto(int Id, string Name, double TrustLevel, double? Rating, int? Group, IReadOnlyList<SourceRatingDayDto> Days);
public sealed record SourceCopyDto(int CopierId, int OriginalId, int Count, double AvgDelaySeconds);
public sealed record SourceRatingReportDto(IReadOnlyList<DateOnly> Days, IReadOnlyList<SourceRatingDto> Sources, IReadOnlyList<SourceCopyDto> Copies, IReadOnlyList<IReadOnlyList<int>> Groups);

public sealed record TrackDetailsDto(TrackDto Track, IReadOnlyList<TargetDto> Targets);

public sealed record PlaceDto(int Id, string Name, string Level, int? ParentId, string? ParentName, double Lon, double Lat, double RadiusKm, int Population);

/// <param name="ParentId">Set for city districts (Kyiv): the city-region they belong to.</param>
public sealed record RegionDto(int Id, string Name, string Level, string CountryCode, int? ParentId, Geometry Geometry);

public sealed record TaxonomyModelDto(int Id, string Code, string Name, string? Manufacturer, string? Country, SpeedProfileDto SpeedProfile);
public sealed record TaxonomyFamilyDto(int Id, string Code, string Name, IReadOnlyList<TaxonomyModelDto> Models);
public sealed record TaxonomyClassDto(int Id, string Code, string Name, string DisplayMode, int FadeMinutes, SpeedProfileDto SpeedProfile, IReadOnlyList<TaxonomyFamilyDto> Families);
public sealed record TaxonomyCategoryDto(int Id, string Code, string Name, IReadOnlyList<TaxonomyClassDto> Classes);
public sealed record TaxonomyDto(IReadOnlyList<TaxonomyCategoryDto> Categories);

public sealed record TimelineBucketDto(DateTimeOffset From, int Targets, int TracksOpened, int Alerts);

/// <summary>Development-only: inject a message as a collector would. Payload (optional) is stored as RawPayload, e.g. an alerts.in.ua alert.</summary>
public sealed record IngestRequest(string SourceCode, string? Text, DateTimeOffset? PublishedAt, string? SourceMessageId, System.Text.Json.JsonElement? Payload);
