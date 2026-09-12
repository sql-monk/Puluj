using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Infrastructure.Seeding;

/// <summary>Upserts sources from data/sources.json by Code. Never touches CollectorState.</summary>
public sealed class SourceSeeder(SeedFiles files, IOptions<SeedOptions> options, IConfiguration config, ILogger<SourceSeeder> logger) : ISeeder
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

        // The database owns the sources: the file only introduces codes that are not there yet, so whatever the
        // admin UI changed (name, trust, channel, polling, token) survives every restart.
        var existing = await db.Sources.Select(s => s.Code).ToHashSetAsync(ct);
        var added = 0;
        foreach (var s in data.Sources.Where(s => !existing.Contains(s.Code)))
        {
            db.Sources.Add(new Source
            {
                Code = s.Code,
                Name = s.Name,
                Enabled = s.Enabled ?? true,
                Type = Enum.Parse<SourceType>(s.Type, true),
                Url = s.Url,
                TrustLevel = s.TrustLevel ?? 0.5,
                Priority = s.Priority ?? 0,
                PollingInterval = s.PollingInterval is null ? null : TimeSpan.Parse(s.PollingInterval),
                Config = s.Config is null ? null : JsonDocument.Parse(s.Config.Value.GetRawText()),
            });
            added++;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Sources: {Count} in file, {Added} new", data.Sources.Count, added);
        await AdoptConfiguredTokenAsync(db, ct);
    }

    /// <summary>
    /// One-time move of a token given through configuration / env into the source row, so the settings page shows
    /// and owns it. Configuration stays a fallback for fresh deployments that ship the token in .env.
    /// </summary>
    private async Task AdoptConfiguredTokenAsync(PulujDbContext db, CancellationToken ct)
    {
        var token = config["Collectors:AlertsInUa:Token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }
        var alerts = await db.Sources.FirstOrDefaultAsync(s => s.Code == "alerts_in_ua", ct);
        if (alerts is null || !string.IsNullOrEmpty(alerts.Secret("token")))
        {
            return;
        }
        alerts.Secrets = JsonDocument.Parse(JsonSerializer.Serialize(new { token = token.Trim() }));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("alerts.in.ua token adopted from configuration into the source row");
    }

    private sealed record SourcesFile(List<SourceDto> Sources);
    private sealed record SourceDto(string Code, string Name, string Type, string? Url, double? TrustLevel, int? Priority, bool? Enabled, string? PollingInterval, JsonElement? Config);
}
