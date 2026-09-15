namespace Puluj.Analytics;

/// <summary>Settings of the source analytics service (`Analytics` section). Thresholds apply at pairing time, not at indexing: a reset with new values reuses the stored fingerprints.</summary>
public sealed class AnalyticsOptions
{
    public const string Section = "Analytics";

    /// <summary>Instance name for the heartbeat (`Runtime:Worker:{Name}:Heartbeat`), logs and the runs table.</summary>
    public string Name { get; set; } = "analytics";

    /// <summary>Sleep between runs once the backlog is drained.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Raw messages taken per transaction.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>Only rows received at least this long ago are taken, so the watermark never passes a row whose insert is still uncommitted.</summary>
    public TimeSpan SafetyLag { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How far apart (either direction) two posts may be published and still count as original / copy.</summary>
    public TimeSpan PairWindow { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Canonical texts shorter than this get no fingerprint: "Відбій тривоги" is a template, not a copy.</summary>
    public int MinTextLength { get; set; } = 40;

    /// <summary>Templates ("Ракета з акваторії Чорного моря у напрямку …щини") of two sources reach 0.6 on different events; real copies sit above 0.8.</summary>
    public double JaccardThreshold { get; set; } = 0.7;

    /// <summary>|A∩B| / min(|A|,|B|): catches "reposted with a comment of our own".</summary>
    public double ContainmentThreshold { get; set; } = 0.85;

    /// <summary>Containment only counts when the shorter text has at least this many canonical characters: a 40-character "Київщина: БпЛА курсом на Обухів" is contained in every list post that mentions the same town.</summary>
    public int ContainmentMinLength { get; set; } = 80;

    public double VerbatimThreshold { get; set; } = 0.9;

    /// <summary>Candidates fetched from the LSH index (nearest in time) and how many of them are verified exactly.</summary>
    public int CandidateScan { get; set; } = 200;
    public int CandidateLimit { get; set; } = 50;

    /// <summary>Days of `track_firsts` rebuilt after each run.</summary>
    public int TrackFirstsDays { get; set; } = 30;
}
