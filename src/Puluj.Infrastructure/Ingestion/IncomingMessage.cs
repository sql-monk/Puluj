using System.Text.Json;

namespace Puluj.Infrastructure.Ingestion;

/// <summary>What a collector hands over: the original message, untouched.</summary>
public sealed record IncomingMessage
{
    public required int SourceId { get; init; }
    public required string SourceMessageId { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public string? RawText { get; init; }
    public JsonDocument? RawPayload { get; init; }
    public string? Url { get; init; }
}

public sealed record IngestResult(long? RawMessageId, bool IsNew);
