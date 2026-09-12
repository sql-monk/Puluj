using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Infrastructure.Seeding;

/// <summary>Upserts sources from data/sources.json by Code. Never touches CollectorState.</summary>
public sealed class SourceSeeder(SeedFiles files, IOptions<SeedOptions> options, ILogger<SourceSeeder> logger) : ISeeder
{
    public int Order => 20;

    public async Task SeedAsync(PulujDbContext db, CancellationToken ct)
    {
        if (!options.Value.SeedSources)
        {
            return;
        }
        var data = await files.ReadAsync<SourcesFile>("sources.json", ct);
        if (data is null)
        {
            logger.LogWarning("sources.json not found under {Root}; skipping", files.Root);
            return;
        }

        var existing = await db.Sources.ToDictionaryAsync(s => s.Code, ct);
        foreach (var s in data.Sources)
        {
            if (!existing.TryGetValue(s.Code, out var e))
            {
                // Enabled is set only on creation: the admin UI owns that flag afterwards.
                e = new Source { Code = s.Code, Name = s.Name, Enabled = s.Enabled ?? true };
                db.Sources.Add(e);
                existing[s.Code] = e;
            }
            e.Name = s.Name;
            e.Type = Enum.Parse<SourceType>(s.Type, true);
            e.Url = s.Url;
            e.TrustLevel = s.TrustLevel ?? 0.5;
            e.Priority = s.Priority ?? 0;
            e.PollingInterval = s.PollingInterval is null ? null : TimeSpan.Parse(s.PollingInterval);
            e.Config = s.Config is null ? null : JsonDocument.Parse(s.Config.Value.GetRawText());
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Sources: {Count}", data.Sources.Count);
    }

    private sealed record SourcesFile(List<SourceDto> Sources);
    private sealed record SourceDto(string Code, string Name, string Type, string? Url, double? TrustLevel, int? Priority, bool? Enabled, string? PollingInterval, JsonElement? Config);
}
