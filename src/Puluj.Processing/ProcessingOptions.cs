namespace Puluj.Processing;

public sealed class ProcessingOptions
{
    public const string Section = "Processing";
    /// <summary>How often an idle worker looks for Pending rows without a NOTIFY (a lost notification, a rebuild), the
    /// pause flag is re-read and expired claims are returned.</summary>
    public TimeSpan PendingPollInterval { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxAttempts { get; set; } = 3;
    /// <summary>How many times a message may fail on a transient database error (deadlock, serialization failure: another
    /// transaction got in the way) before such a failure counts as an attempt like any other. Transient retries are
    /// tracked in memory per instance, so a restart starts them over.</summary>
    public int MaxTransientRetries { get; set; } = 10;
    /// <summary>Workers per instance, each processing one message at a time. 1 keeps the strict oldest-first order of a
    /// single consumer; the number of instances is independent of this.</summary>
    public int Concurrency { get; set; } = 2;
    /// <summary>An InProgress claim older than this belongs to a dead instance and goes back to Pending (attempt counted).
    /// Must exceed the longest single message (LLM call with retries).</summary>
    public TimeSpan ClaimLease { get; set; } = TimeSpan.FromMinutes(5);
}
