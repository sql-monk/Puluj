using System.Text.Json;
using NetTopologySuite.Geometries;

namespace Puluj.Domain.Entities;

/// <summary>Per-collector cursor and health.</summary>
public class CollectorState
{
    public int SourceId { get; set; }
    public Source? Source { get; set; }
    public string? LastSourceMessageId { get; set; }
    public DateTimeOffset? LastPolledAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
    public JsonDocument? Cursor { get; set; }
}

public class ProcessingError
{
    public long ProcessingErrorId { get; set; }
    public long? RawMessageId { get; set; }
    public int? SourceId { get; set; }
    public required string Stage { get; set; }
    public required string Message { get; set; }
    public string? Exception { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public JsonDocument? Payload { get; set; }
}

/// <summary>Reserved for future server-side features (push notifications). Not used in MVP: HomeLocation stays in the browser.</summary>
public class UserLocation
{
    public Guid UserLocationId { get; set; }
    public required string ClientKey { get; set; }
    public required Point Location { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
