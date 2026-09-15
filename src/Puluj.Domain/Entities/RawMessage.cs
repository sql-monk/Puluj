using System.Text.Json;
using Puluj.Domain.Enums;

namespace Puluj.Domain.Entities;

/// <summary>Spec §5. Immutable original message. Only the processing-status columns may change after insert.</summary>
public class RawMessage
{
    public long RawMessageId { get; set; }
    public int SourceId { get; set; }
    public Source? Source { get; set; }
    /// <summary>Identifier inside the source (telegram message id, alert uid...). Unique per source.</summary>
    public required string SourceMessageId { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string? RawText { get; set; }
    public JsonDocument? RawPayload { get; set; }
    public string? Url { get; set; }
    /// <summary>SHA-256 of source code + normalized text/payload — second idempotency key.</summary>
    public required string Hash { get; set; }

    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Pending;
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    /// <summary>Processor instance that took the message (kept after processing as provenance); null while Pending.</summary>
    public string? ClaimedBy { get; set; }
    /// <summary>When it was taken; an InProgress claim older than the lease is returned to Pending by any instance.</summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    public ICollection<Target> Targets { get; set; } = [];
}
