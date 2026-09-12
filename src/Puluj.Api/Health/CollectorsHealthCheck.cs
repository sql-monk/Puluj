using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Api.Health;

/// <summary>
/// Degraded when a source that a collector actually serves has not succeeded within 3x its polling interval (spec §29 watchdog).
/// Sources without a CollectorState row were never picked up (collector disabled / no credentials) and report "idle" instead.
/// </summary>
public sealed class CollectorsHealthCheck(IDbContextFactory<PulujDbContext> factory, TimeProvider clock) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        var sources = await db.Sources.AsNoTracking()
            .Where(s => s.Enabled)
            .Select(s => new { s.Code, s.PollingInterval, State = s.CollectorState })
            .ToListAsync(ct);

        var data = new Dictionary<string, object>();
        var stale = new List<string>();
        foreach (var s in sources)
        {
            var interval = s.PollingInterval ?? TimeSpan.FromMinutes(5);
            var last = s.State?.LastSuccessAt;
            var isIdle = s.State is null;
            var isStale = !isIdle && (last is null || now - last.Value > interval * 3);
            data[s.Code] = new
            {
                lastSuccessAt = last,
                lastMessageAt = s.State?.LastMessageAt,
                consecutiveFailures = s.State?.ConsecutiveFailures ?? 0,
                lastError = s.State?.LastError,
                status = isIdle ? "idle" : isStale ? "stale" : "ok",
            };
            if (isStale)
            {
                stale.Add(s.Code);
            }
        }

        return stale.Count == 0
            ? HealthCheckResult.Healthy("All collectors fresh", data)
            : HealthCheckResult.Degraded($"Stale collectors: {string.Join(", ", stale)}", data: data);
    }
}
