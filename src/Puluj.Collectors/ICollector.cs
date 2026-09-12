using Puluj.Domain.Entities;

namespace Puluj.Collectors;

/// <summary>A long-running collector for one or more sources. Must be logically independent of other collectors (spec §27).</summary>
public interface ICollector
{
    /// <summary>Stable name used in logs/health.</summary>
    string Name { get; }

    /// <summary>Which enabled sources this collector serves. Empty means the collector stays idle.</summary>
    bool Handles(Source source);

    /// <summary>Runs until cancelled. Throwing means "restart me with backoff"; the supervisor never lets one collector kill another.</summary>
    Task RunAsync(IReadOnlyList<Source> sources, CancellationToken ct);
}
