namespace Puluj.Domain.Entities;

/// <summary>
/// One source copying another, per day: every fact that turned out to be a duplicate of an earlier fact from a
/// different source adds one to the pair (copier → original) with the delay. Groups of sources that feed on each
/// other are derived from these rows.
/// </summary>
public class SourceCopy
{
    public int CopierSourceId { get; set; }
    public int OriginalSourceId { get; set; }
    public DateOnly Day { get; set; }
    public int Count { get; set; }
    /// <summary>Sum of seconds between the original and the copy, for the average delay.</summary>
    public double DelaySecondsSum { get; set; }
}

/// <summary>
/// Per-source, per-day counters behind the source rating (distinct from the trust level the operator sets by hand):
/// how many facts the source reported, how many of them merely repeated another source, and how often other sources
/// repeated it (and how much later).
/// </summary>
public class SourceDailyStat
{
    public int SourceId { get; set; }
    public DateOnly Day { get; set; }
    /// <summary>All target facts the source reported that day, duplicates included.</summary>
    public int Targets { get; set; }
    /// <summary>Facts that were duplicates of another source's earlier fact.</summary>
    public int Copies { get; set; }
    /// <summary>Times another source later repeated one of this source's facts.</summary>
    public int CopiedBy { get; set; }
    /// <summary>Sum of seconds by which others were behind this source, for the average lead.</summary>
    public double LeadSecondsSum { get; set; }
}
