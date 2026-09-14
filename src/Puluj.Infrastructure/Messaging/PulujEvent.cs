using System.Text.Json.Serialization;

namespace Puluj.Infrastructure.Messaging;

/// <summary>Payload of the `puluj_events` NOTIFY channel. Tiny by design: consumers re-read the entity from the database.</summary>
public sealed record PulujEvent(
    [property: JsonPropertyName("type")] PulujEventType Type,
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("at")] DateTimeOffset At);

[JsonConverter(typeof(JsonStringEnumConverter<PulujEventType>))]
public enum PulujEventType
{
    TrackUpserted,
    TrackClosed,
    AlertChanged,
    TargetCreated,
    /// <summary>A raw message was stored Pending (Id = raw_message_id); wakes the processor in whichever process it runs.</summary>
    RawMessageStored,
}
