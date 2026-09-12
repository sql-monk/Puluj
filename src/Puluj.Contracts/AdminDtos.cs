namespace Puluj.Contracts;

/// <summary>One configurable key. Secret values are never sent; `hasValue` tells whether something is set.</summary>
public sealed record SettingDto(string Key, string? Value, bool IsSecret, bool HasValue, string Source);

public sealed record SettingsUpdateRequest(Dictionary<string, string?> Values);

public sealed record AdminSourceDto(
    int Id, string Code, string Name, string Type, bool Enabled, double TrustLevel, int Priority, string? Url, string? Channel,
    int? PollingIntervalSeconds, string? HomeRegion, long RawMessageCount,
    DateTimeOffset? LastSuccessAt, DateTimeOffset? LastMessageAt, int ConsecutiveFailures, string? LastError, string Status);

public sealed record SourceUpdateRequest(bool? Enabled, double? TrustLevel, string? Name, int? Priority, int? PollingIntervalSeconds, string? Channel, string? Url, string? HomeRegion);

public sealed record SourceCreateRequest(string Name, string Type, string? Channel, string? Url, double? TrustLevel, int? Priority, int? PollingIntervalSeconds);

public sealed record AdminStatusDto(
    bool AlertsConfigured, bool TelegramConfigured, bool LlmConfigured,
    string? TelegramStatus, bool AdminTokenSet, bool WorkerAlive, DateTimeOffset? WorkerLastSeen);

public sealed record TestResultDto(bool Ok, string Message);

public sealed record TelegramCodeRequest(string Code);
