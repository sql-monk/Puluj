using System.Net;

namespace Puluj.Processing.Llm;

/// <summary>
/// Keeps the model out of the pipeline after a failure another call would only repeat: a rejected request, a bad key,
/// an exhausted balance (400/401/403) pause it for <see cref="FailurePause"/>; a rate limit (429) for a minute. Without
/// this every message that looks like a target report costs an HTTP round trip and a warning while the key is dead.
/// Server errors and timeouts are not counted: the next message tries again.
/// </summary>
public sealed class LlmBreaker(TimeSpan failurePause)
{
    public static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(1);

    private readonly object _lock = new();
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private string? _reason;

    public TimeSpan FailurePause { get; } = failurePause;

    /// <summary>How long a failure with this status keeps the model paused; null when it does not.</summary>
    public TimeSpan? PauseFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => FailurePause,
        HttpStatusCode.TooManyRequests => RateLimitPause,
        _ => null,
    };

    /// <summary>Records the failure; returns the pause it caused, null when the call may simply be retried next time.</summary>
    public TimeSpan? Trip(HttpStatusCode status, string reason, DateTimeOffset now)
    {
        if (PauseFor(status) is not { } pause)
        {
            return null;
        }
        lock (_lock)
        {
            _pausedUntil = now + pause;
            _reason = reason;
        }
        return pause;
    }

    /// <summary>True while calls are held back; <paramref name="reason"/> is what tripped it.</summary>
    public bool IsOpen(DateTimeOffset now, out string? reason)
    {
        lock (_lock)
        {
            reason = _reason;
            return now < _pausedUntil;
        }
    }

    /// <summary>A successful call clears an expired pause's reason.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _pausedUntil = DateTimeOffset.MinValue;
            _reason = null;
        }
    }
}
