namespace Puluj.Domain.Entities;

/// <summary>How one reported target relates to an earlier one.</summary>
public enum TargetLinkKind
{
    /// <summary>The same object reported again later (the previous sighting of the track).</summary>
    Continuation = 0,
    /// <summary>One earlier report continues into several new ones: a group broke up, or one message lists its parts.</summary>
    Split = 1,
    /// <summary>Several earlier reports continue into one: groups joined, or the new report covers them all.</summary>
    Merge = 2,
    /// <summary>Plausible but not confirmed: scored below the attach threshold, above the "possible" floor.</summary>
    Possible = 3,
    /// <summary>The same fact from another message.</summary>
    Duplicate = 4,
}

/// <summary>
/// Directed many-to-many link between targets: "this report is a continuation of that one". Continuations and
/// duplicates come from the track the report joined; splits, merges and possible links from the other candidates
/// the correlator scored, so a drone that crossed from Chernihiv oblast into Kyiv oblast, a group that broke up and
/// groups that joined all stay connected across tracks.
/// </summary>
public class TargetLink
{
    public long FromTargetId { get; set; }
    public Target? From { get; set; }
    public long ToTargetId { get; set; }
    public Target? To { get; set; }
    public TargetLinkKind Kind { get; set; }
    /// <summary>0..1: the correlator's association score for the pair (1 for duplicates).</summary>
    public double Confidence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
