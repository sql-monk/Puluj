namespace Puluj.Analytics.Contracts;

public sealed record RunDto(long Id, string Instance, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, DateTimeOffset UpdatedAt, string Status,
    long WatermarkFrom, long WatermarkTo, int MessagesScanned, int MessagesFingerprinted, int PairsFound, string? Error);

/// <summary>State of the analytics service: where the index stands against the raw messages and how the last runs went.</summary>
public sealed record AnalyticsStatusDto(
    bool Initialized,
    long Watermark,
    long LatestRawMessageId,
    long Backlog,
    DateTimeOffset? HeartbeatAt,
    RunDto? LastRun,
    IReadOnlyList<RunDto> Runs,
    long MessagesIndexed,
    long MessagesFingerprinted,
    long PairsTotal,
    long SchemaBytes,
    IReadOnlyList<string> Migrations,
    AnalyticsInstanceDto? Instance);

public sealed record SourceDayDto(DateOnly Day, int Posts, int Copies, int CopiedBy);

/// <summary>Per-source picture over the period: what it posts, how much of that repeats others, how much others repeat it.</summary>
public sealed record SourceAnalyticsDto(
    int Id,
    string Code,
    string Name,
    bool Enabled,
    int Posts,
    int Edits,
    int Copies,
    int CopiedBy,
    double? UniqueShare,
    double? AvgCopyDelaySeconds,
    double? MedianCopyDelaySeconds,
    double? AvgLeadSeconds,
    double? VerbatimShare,
    int ForwardsInternal,
    int ForwardsExternal,
    IReadOnlyList<int> PerHour,
    IReadOnlyList<SourceDayDto> PerDay);

/// <summary>Copier → original over the period. `Count` uses the primary edge of every copy (earliest original); `CountAll` includes the intermediate ones (whom the copier actually reads).</summary>
public sealed record CopyPairDto(int CopierId, int OriginalId, int Count, int CountAll, int Verbatim, int Forwards, double AvgDelaySeconds, double MedianDelaySeconds, double MinDelaySeconds, double AvgJaccard);

public sealed record ExternalForwardDto(int SourceId, string ChannelRef, int Count);

public sealed record TrackFirstDto(int SourceId, string CategoryCode, int Firsts, int Participations, double? AvgLagSeconds);

public sealed record AnalyticsReportDto(
    IReadOnlyList<DateOnly> Days,
    IReadOnlyList<SourceAnalyticsDto> Sources,
    IReadOnlyList<CopyPairDto> Pairs,
    IReadOnlyList<ExternalForwardDto> ExternalForwards,
    IReadOnlyList<TrackFirstDto> Firsts);

/// <summary>One detected pair with both texts, to judge the thresholds by eye.</summary>
public sealed record RecentCopyDto(
    int CopierId, long CopyRawMessageId, DateTimeOffset CopyPublishedAt, string? CopyText, string? CopyUrl,
    int OriginalId, long OriginalRawMessageId, DateTimeOffset OriginalPublishedAt, string? OriginalText, string? OriginalUrl,
    double DelaySeconds, double Jaccard, double Containment, string Kind, bool IsPrimary);

/// <summary>What the analytics process says about itself (`Runtime:Worker:{name}:Status`, written every 10 s); null until the process publishes it.</summary>
public sealed record AnalyticsInstanceDto(string? Host, string? Version, DateTimeOffset? BuiltAt, DateTimeOffset? StartedAt, DateTimeOffset? At, int? Pid, long? WorkingSetBytes, double? CpuPercent, int? Threads);

/// <summary>One bar of the copy-delay histogram: [FromSeconds, ToSeconds), the last bucket open-ended.</summary>
public sealed record DelayBucketDto(double FromSeconds, double? ToSeconds, int Count);

/// <summary>One copier → original pair over the period: the same aggregates as CopyPairDto, the delay distribution and the latest examples.</summary>
public sealed record PairDetailsDto(
    int CopierId,
    int OriginalId,
    int Days,
    int Count,
    int CountAll,
    int Verbatim,
    int Forwards,
    double? AvgDelaySeconds,
    double? MedianDelaySeconds,
    double? MinDelaySeconds,
    double? AvgJaccard,
    IReadOnlyList<DelayBucketDto> Delays,
    IReadOnlyList<RecentCopyDto> Recent);
