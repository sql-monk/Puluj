using System.Threading.Channels;

namespace Puluj.Infrastructure.Ingestion;

/// <summary>In-process signal "raw message N is pending". Durable state lives in RawMessage.ProcessingStatus, this only wakes the loop.</summary>
public interface IRawMessageQueue
{
    ValueTask EnqueueAsync(long rawMessageId, CancellationToken ct = default);
    IAsyncEnumerable<long> DequeueAllAsync(CancellationToken ct);
}

public sealed class RawMessageQueue : IRawMessageQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(long rawMessageId, CancellationToken ct = default) => _channel.Writer.WriteAsync(rawMessageId, ct);

    public IAsyncEnumerable<long> DequeueAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
