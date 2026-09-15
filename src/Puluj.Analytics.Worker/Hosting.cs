using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Puluj.Analytics;
using Puluj.Analytics.Analysis;
using Puluj.Analytics.Persistence;

namespace Puluj.Analytics.Worker;

/// <summary>Applies the `analytics` schema migrations before the loop starts. Only this service ever migrates the schema, so a plain advisory lock is enough against a restart race.</summary>
public sealed class AnalyticsInitializer(IDbContextFactory<AnalyticsDbContext> factory, ILogger<AnalyticsInitializer> logger) : IHostedService
{
    private const long LockKey = 0x414E414C594D; // "ANALYM"

    public async Task StartAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({LockKey})", ct);
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} analytics migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
                await db.Database.MigrateAsync(ct);
            }
        }
        finally
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({LockKey})", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Runs the analysis on a timer: drain the backlog, sleep `Analytics:Interval`, repeat. Consecutive failures are counted for /health.</summary>
public sealed class AnalysisLoop(AnalysisRunner runner, IOptions<AnalyticsOptions> options, TimeProvider clock, ILogger<AnalysisLoop> logger) : BackgroundService
{
    public int ConsecutiveFailures { get; private set; }
    public DateTimeOffset? LastRunAt { get; private set; }
    public string? LastError { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Analytics loop started (every {Interval})", options.Value.Interval);
        using var timer = new PeriodicTimer(options.Value.Interval);
        do
        {
            try
            {
                await runner.RunOnceAsync(ct);
                ConsecutiveFailures = 0;
                LastError = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                ConsecutiveFailures++;
                LastError = ex.Message;
                logger.LogError(ex, "Analytics run failed ({Failures} in a row)", ConsecutiveFailures);
            }
            LastRunAt = clock.GetUtcNow();
        } while (await timer.WaitForNextTickAsync(ct));
    }
}

/// <summary>
/// Writes `Runtime:Worker:{Name}:Heartbeat` into `app_settings` every 30 s — the same key family the Worker instances
/// use, so the admin panel lists this service next to them without knowing anything about it. Removed on a clean stop.
/// </summary>
public sealed class AnalyticsHeartbeat(IDbContextFactory<AnalyticsDbContext> factory, IOptions<AnalyticsOptions> options, TimeProvider clock, ILogger<AnalyticsHeartbeat> logger) : BackgroundService
{
    private string Key => $"Runtime:Worker:{options.Value.Name}:Heartbeat";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                var now = clock.GetUtcNow();
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO app_settings (key, value, is_secret, updated_at) VALUES ({Key}, {now.ToString("O")}, false, {now})
                    ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = EXCLUDED.updated_at
                    """, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Heartbeat write failed");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var db = await factory.CreateDbContextAsync(cts.Token);
            await db.Database.ExecuteSqlAsync($"DELETE FROM app_settings WHERE key = {Key}", cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Heartbeat removal failed");
        }
    }
}
