namespace Puluj.Infrastructure.Persistence;

/// <summary>
/// Keys for pg_advisory_xact_lock. Processor instances run in parallel, but everything that reads-and-modifies the
/// derived tables (targets and their triggers, tracks, alerts, links) runs under <see cref="Store"/>, one message at a
/// time, so correlation and deduplication see a consistent picture. Parsing runs outside the lock. Lock order everywhere:
/// raw_messages row first, then the advisory lock, then track / alert rows; ReprocessService follows the same order.
/// </summary>
public static class AdvisoryLocks
{
    /// <summary>"PULUJ" 0x50554C554A followed by 01: the store stage of the processing pipeline.</summary>
    public const long Store = 0x50554C554A01;

    public static FormattableString Take(long key) => $"SELECT pg_advisory_xact_lock({key})";
}
