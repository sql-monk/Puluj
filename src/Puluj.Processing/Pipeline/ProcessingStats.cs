using System.Collections.Concurrent;
using Puluj.Contracts;

namespace Puluj.Processing.Pipeline;

/// <summary>
/// What this processor instance has done since it started, for the status document the admin panel reads
/// (WorkerStatusReporter, plan §2.1): outcome counters, per-stage timings over the last five minutes (ring buffers of
/// <see cref="Capacity"/> samples, each with its time), throughput per minute (per-second buckets of successful
/// processings), the messages currently claimed. Updated by <see cref="RawMessageProcessor"/> next to PulujMetrics and
/// by <see cref="ProcessingLoop"/> at claim and release; read by <see cref="Snapshot"/> from another thread.
/// </summary>
public sealed class ProcessingStats(TimeProvider clock)
{
    public const int Capacity = 2_000;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly int WindowSeconds = (int)Window.TotalSeconds;

    private readonly object _lock = new();
    private readonly Dictionary<string, Ring> _stages = new()
    {
        ["parse"] = new Ring(),
        ["lock"] = new Ring(),
        ["store"] = new Ring(),
        ["total"] = new Ring(),
    };
    private readonly int[] _perSecond = new int[WindowSeconds];
    private readonly long[] _perSecondAt = new long[WindowSeconds];
    private readonly ConcurrentDictionary<long, DateTimeOffset> _claims = new();
    private long _processed, _skipped, _failed, _retried, _retriedTransient;
    private DateTimeOffset? _lastProcessedAt;
    private long? _lastRawMessageId;

    /// <summary>stage: parse | lock | store | total. Unknown stages are ignored.</summary>
    public void Record(string stage, double milliseconds)
    {
        var now = clock.GetUtcNow();
        lock (_lock)
        {
            if (_stages.TryGetValue(stage, out var ring))
            {
                ring.Add(now, milliseconds);
            }
        }
    }

    /// <summary>outcome: processed | skipped | failed | retried | retried_transient (the values of PulujMetrics.RawProcessed).</summary>
    public void Outcome(string outcome, long rawMessageId)
    {
        var now = clock.GetUtcNow();
        lock (_lock)
        {
            switch (outcome)
            {
                case "processed":
                    _processed++;
                    _lastProcessedAt = now;
                    _lastRawMessageId = rawMessageId;
                    var second = now.ToUnixTimeSeconds();
                    var slot = (int)(second % WindowSeconds);
                    if (_perSecondAt[slot] != second)
                    {
                        _perSecondAt[slot] = second;
                        _perSecond[slot] = 0;
                    }
                    _perSecond[slot]++;
                    break;
                case "skipped":
                    _skipped++;
                    break;
                case "failed":
                    _failed++;
                    break;
                case "retried":
                    _retried++;
                    break;
                case "retried_transient":
                    _retriedTransient++;
                    break;
            }
        }
    }

    public void Claimed(long rawMessageId) => _claims[rawMessageId] = clock.GetUtcNow();

    public void Released(long rawMessageId) => _claims.TryRemove(rawMessageId, out _);

    public ProcessingStatusDto Snapshot(int concurrency)
    {
        var now = clock.GetUtcNow();
        var claims = _claims.OrderBy(c => c.Value).Select(c => new ClaimDto(c.Key, c.Value)).ToList();
        lock (_lock)
        {
            return new ProcessingStatusDto(
                concurrency,
                _processed, _skipped, _failed, _retried, _retriedTransient,
                CountSince(now, 60) / 1.0,
                CountSince(now, WindowSeconds) / 5.0,
                _stages["parse"].Timing(now),
                _stages["lock"].Timing(now),
                _stages["store"].Timing(now),
                _stages["total"].Timing(now),
                _lastProcessedAt,
                _lastRawMessageId,
                claims);
        }
    }

    /// <summary>Successful processings in the last <paramref name="seconds"/> seconds, the current second included.</summary>
    private long CountSince(DateTimeOffset now, int seconds)
    {
        var current = now.ToUnixTimeSeconds();
        long count = 0;
        for (var i = 0; i < WindowSeconds; i++)
        {
            if (current - _perSecondAt[i] < seconds && _perSecondAt[i] <= current)
            {
                count += _perSecond[i];
            }
        }
        return count;
    }

    /// <summary>Fixed-size ring of timed samples; the newest overwrites the oldest. Percentiles are nearest-rank.</summary>
    private sealed class Ring
    {
        private readonly DateTimeOffset[] _at = new DateTimeOffset[Capacity];
        private readonly double[] _ms = new double[Capacity];
        private int _next;
        private int _count;

        public void Add(DateTimeOffset at, double ms)
        {
            _at[_next] = at;
            _ms[_next] = ms;
            _next = (_next + 1) % Capacity;
            _count = Math.Min(_count + 1, Capacity);
        }

        public StageTimingDto Timing(DateTimeOffset now)
        {
            var since = now - Window;
            var values = new List<double>(_count);
            for (var i = 0; i < _count; i++)
            {
                if (_at[i] >= since)
                {
                    values.Add(_ms[i]);
                }
            }
            if (values.Count == 0)
            {
                return new StageTimingDto(0, 0, 0, 0, 0);
            }
            values.Sort();
            return new StageTimingDto(values.Count, values.Average(), Percentile(values, 0.5), Percentile(values, 0.9), values[^1]);
        }

        private static double Percentile(List<double> sorted, double p) =>
            sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
    }
}
