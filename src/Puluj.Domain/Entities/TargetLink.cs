namespace Puluj.Domain.Entities;

/// <summary>How one reported target relates to an earlier one.</summary>
public enum TargetLinkKind
{
    /// <summary>The earlier target could have flown to where the new one is: a kinematic predecessor, with a probability.</summary>
    Continuation = 0,
    /// <summary>Kept for history; no longer produced (probabilities express splits and merges now).</summary>
    Split = 1,
    /// <summary>Kept for history; no longer produced.</summary>
    Merge = 2,
    /// <summary>Kept for history; no longer produced.</summary>
    Possible = 3,
    /// <summary>The same fact from another message.</summary>
    Duplicate = 4,
}

/// <summary>
/// Directed many-to-many link between targets: "the object reported here is, with this probability, the one reported
/// there before". For every new located target the linker finds the earlier targets that could have reached its position
/// at the class's cruise speed in the time elapsed, allowing for a turn when they were heading elsewhere, and shares the
/// probability among them so that the sum never exceeds 1 (two equally plausible predecessors get 0.5 each; the rest is
/// "a new object"). The crumbs on the map and the "was at" chain are built from these links alone.
/// </summary>
public class TargetLink
{
    public long FromTargetId { get; set; }
    public Target? From { get; set; }
    public long ToTargetId { get; set; }
    public Target? To { get; set; }
    public TargetLinkKind Kind { get; set; }
    /// <summary>0..1: that the target `To` is the same object as `From`. 1 = certain. Links of one `To` sum to at most 1.</summary>
    public double Probability { get; set; }
    /// <summary>Kilometres the object had to cover between the two reported areas (0 when they overlap).</summary>
    public double? DistanceKm { get; set; }
    public double? MinutesApart { get; set; }
    /// <summary>Angle between the earlier target's course and the bearing towards the new one; null when it had no course.</summary>
    public double? HeadingDiffDeg { get; set; }
    /// <summary>Minutes the trip would take at cruise speed, turn included.</summary>
    public double? RequiredMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
