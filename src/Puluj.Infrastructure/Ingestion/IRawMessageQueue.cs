using System.Threading.Channels;

namespace Puluj.Infrastructure.Ingestion;

/// <summary>
/// In-process signal "raw message N is pending", fed by the NOTIFY listener and the Pending sweep inside the processor.
/// Durable state lives in RawMessage.ProcessingStatus, this only wakes the loop.
/// </summary>
public interface IRawMessageQueue
{
    ValueTask EnqueueAsync(long rawMessageId, CancellationToken ct = default);
    IAsyncEnumerable<long> DequeueAllAsync(CancellationToken ct);
    /// <summary>Ids enqueued and not yet taken by the consumer.</summary>
    int Depth { get; }
}

public sealed class RawMessageQueue : IRawMessageQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions { SingleReader = true });

    // A single-reader unbounded channel does not count its items; the depth is kept by hand.
    private int _depth;

    public async ValueTask EnqueueAsync(long rawMessageId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _depth);
        await _channel.Writer.WriteAsync(rawMessageId, ct);
    }

    public async IAsyncEnumerable<long> DequeueAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var id in _channel.Reader.ReadAllAsync(ct))
        {
            Interlocked.Decrement(ref _depth);
            yield return id;
        }
    }

    public int Depth => Volatile.Read(ref _depth);
}
