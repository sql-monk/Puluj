using System.Threading.Channels;

namespace Puluj.Infrastructure.Ingestion;

/// <summary>
/// In-process signal "raw message N was just stored", fed by the NOTIFY listener. Durable state lives in
/// RawMessage.ProcessingStatus (a claim in the database decides who processes it); this only wakes the workers of this
/// instance so a live message is taken ahead of the backlog. Several workers read it.
/// </summary>
public interface IRawMessageQueue
{
    ValueTask EnqueueAsync(long rawMessageId, CancellationToken ct = default);
    bool TryDequeue(out long rawMessageId);
    /// <summary>Completes when an id is available (true) or the queue is closed (false).</summary>
    ValueTask<bool> WaitToReadAsync(CancellationToken ct);
}

public sealed class RawMessageQueue : IRawMessageQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public ValueTask EnqueueAsync(long rawMessageId, CancellationToken ct = default) => _channel.Writer.WriteAsync(rawMessageId, ct);

    public bool TryDequeue(out long rawMessageId) => _channel.Reader.TryRead(out rawMessageId);

    public ValueTask<bool> WaitToReadAsync(CancellationToken ct) => _channel.Reader.WaitToReadAsync(ct);
}
