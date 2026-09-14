namespace Puluj.Contracts;

/// <summary>One configurable key. Secret values are never sent; `hasValue` tells whether something is set.</summary>
public sealed record SettingDto(string Key, string? Value, bool IsSecret, bool HasValue, string Source);

public sealed record SettingsUpdateRequest(Dictionary<string, string?> Values);

public sealed record AdminSourceDto(
    int Id, string Code, string Name, string Type, bool Enabled, double TrustLevel, int Priority, string? Url, string? Channel,
    int? PollingIntervalSeconds, string? HomeRegion, bool HasToken, long RawMessageCount,
    DateTimeOffset? LastSuccessAt, DateTimeOffset? LastMessageAt, int ConsecutiveFailures, string? LastError, string Status);

/// <param name="Token">API token stored on the source (write-only: the DTO only says whether one is set). Empty string removes it.</param>
public sealed record SourceUpdateRequest(bool? Enabled, double? TrustLevel, string? Name, int? Priority, int? PollingIntervalSeconds, string? Channel, string? Url, string? HomeRegion, string? Token);

public sealed record SourceCreateRequest(string Name, string Type, string? Channel, string? Url, double? TrustLevel, int? Priority, int? PollingIntervalSeconds);

public sealed record AdminStatusDto(
    bool AlertsConfigured, bool TelegramConfigured, bool LlmConfigured,
    string? TelegramStatus, bool AdminTokenSet, bool WorkerAlive, DateTimeOffset? WorkerLastSeen);

public sealed record TestResultDto(bool Ok, string Message);

public sealed record TelegramCodeRequest(string Code);

// ---- Operations: per-component status, statistics and logs (admin panel only) ----

/// <summary>One service of the system as the admin panel sees it: `ok`, `warn`, `down` or `unknown`.</summary>
public sealed record ServiceStatusDto(string Name, string Status, string? Detail, DateTimeOffset? LastSeen);

public sealed record DbOverviewDto(string Version, long SizeBytes, int Connections, string? LastMigration, int MigrationCount);

public sealed record OpsOverviewDto(DateTimeOffset GeneratedAt, IReadOnlyList<ServiceStatusDto> Services, DbOverviewDto Db);

/// <param name="PerHour">Messages received per hour for the last 24 hours, oldest first.</param>
public sealed record CollectorStatusDto(
    int SourceId, string Code, string Name, string Type, bool Enabled,
    DateTimeOffset? LastPolledAt, DateTimeOffset? LastSuccessAt, DateTimeOffset? LastMessageAt, string? LastError, int ConsecutiveFailures,
    long Messages24h, IReadOnlyList<int> PerHour);

public sealed record HourlyProcessingDto(DateTimeOffset Hour, int Received, int Processed, int Targets, int Links, int Errors);

public sealed record ProcessingErrorDto(long Id, DateTimeOffset OccurredAt, string Stage, string Message, int? SourceId, long? RawMessageId, string? Exception);

/// <param name="Queue">Raw messages by processing status.</param>
public sealed record ProcessingReportDto(
    Dictionary<string, long> Queue, IReadOnlyList<HourlyProcessingDto> Hours, IReadOnlyList<ProcessingErrorDto> RecentErrors,
    Dictionary<string, long> ErrorsByStage24h, long Targets24h, long Links24h, long Duplicates24h);

public sealed record DbTableDto(string Name, long Rows, long Bytes);

public sealed record DbRoleConnectionsDto(string Role, int Connections);

public sealed record DbReportDto(string Version, long SizeBytes, IReadOnlyList<DbTableDto> Tables, IReadOnlyList<string> Migrations, IReadOnlyList<DbRoleConnectionsDto> Connections);

public sealed record LogFileDto(string Name, string Service, long Bytes, DateTimeOffset ModifiedAt);

public sealed record LogTailDto(string File, IReadOnlyList<string> Lines, bool Truncated, long Bytes);
