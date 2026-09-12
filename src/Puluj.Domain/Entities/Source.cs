using System.Text.Json;
using Puluj.Domain.Enums;

namespace Puluj.Domain.Entities;

/// <summary>Spec §3. One row per collector-backed data source. Secrets live in env/config, never here.</summary>
public class Source
{
    public int SourceId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public SourceType Type { get; set; }
    public string? Url { get; set; }
    /// <summary>0..1 — how much we trust the facts of this source.</summary>
    public double TrustLevel { get; set; } = 0.5;
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public TimeSpan? PollingInterval { get; set; }
    /// <summary>Source-specific config: telegram channel id, home region, etc.</summary>
    public JsonDocument? Config { get; set; }

    public CollectorState? CollectorState { get; set; }
}
