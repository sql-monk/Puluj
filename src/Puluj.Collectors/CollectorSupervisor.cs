using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Puluj.Collectors.AlertsInUa;
using Puluj.Collectors.Telegram;
using Puluj.Domain.Entities;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Collectors;

/// <summary>
/// Starts every registered collector in its own task and restarts it with exponential backoff when it throws.
/// A failing collector never affects the others (spec §29). When collector options change (admin UI writes
/// app_settings, IOptionsMonitor fires) or a source is toggled, all collectors are stopped and started again
/// with the fresh configuration — no process restart needed.
/// </summary>
public sealed class CollectorSupervisor(
    IEnumerable<ICollector> collectors,
    IDbContextFactory<PulujDbContext> factory,
    IOptionsMonitor<AlertsInUaOptions> alertsOptions,
    IOptionsMonitor<TelegramOptions> telegramOptions,
    ILogger<CollectorSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RestartDebounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SourceCheckInterval = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _restartSignal = new(0);

    /// <summary>Ask the supervisor to reload sources and restart collectors (used by option-change callbacks).</summary>
    public void RequestRestart() => _restartSignal.Release();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // IOptionsMonitor fires on every configuration reload; restart only when the values actually differ.
        var lastAlerts = Json(alertsOptions.CurrentValue);
        var lastTelegram = Json(telegramOptions.CurrentValue);
        using var sub1 = alertsOptions.OnChange(o =>
        {
            var j = Json(o);
            if (j != lastAlerts)
            {
                lastAlerts = j;
                RequestRestart();
            }
        });
        using var sub2 = telegramOptions.OnChange(o =>
        {
            var j = Json(o);
            if (j != lastTelegram)
            {
                lastTelegram = j;
                RequestRestart();
            }
        });

        var generation = 0;
        while (!ct.IsCancellationRequested)
        {
            generation++;
            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var sources = await LoadSourcesAsync(ct);
            var tasks = StartAll(sources, generation, runCts.Token);

            // Wait until someone asks for a restart or an enabled-source set changes in the database.
            await WaitForChangeAsync(sources, ct);
            if (ct.IsCancellationRequested)
            {
                break;
            }
            await DrainAsync(ct);
            logger.LogInformation("Collector configuration changed; restarting collectors (generation {Gen})", generation + 1);
            await runCts.CancelAsync();
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private List<Task> StartAll(List<Source> sources, int generation, CancellationToken ct)
    {
        var tasks = new List<Task>();
        foreach (var collector in collectors)
        {
            var mine = sources.Where(collector.Handles).ToList();
            if (mine.Count == 0)
            {
                logger.LogInformation("Collector {Name}: no enabled sources, idle", collector.Name);
                continue;
            }
            logger.LogInformation("Collector {Name} (gen {Gen}): {Sources}", collector.Name, generation, string.Join(", ", mine.Select(s => s.Code)));
            tasks.Add(SuperviseAsync(collector, mine, ct));
        }
        return tasks;
    }

    private async Task<List<Source>> LoadSourcesAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Sources.AsNoTracking().Where(s => s.Enabled).OrderByDescending(s => s.Priority).ToListAsync(ct);
    }

    private async Task WaitForChangeAsync(List<Source> current, CancellationToken ct)
    {
        var signature = Signature(current);
        while (!ct.IsCancellationRequested)
        {
            if (await _restartSignal.WaitAsync(SourceCheckInterval, ct))
            {
                return;
            }
            try
            {
                if (Signature(await LoadSourcesAsync(ct)) != signature)
                {
                    return;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not check sources for changes");
            }
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        // Several option keys are usually saved at once; collapse the burst into one restart.
        await Task.Delay(RestartDebounce, ct);
        while (_restartSignal.CurrentCount > 0)
        {
            await _restartSignal.WaitAsync(ct);
        }
    }

    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static string Signature(IEnumerable<Source> sources) =>
        string.Join("|", sources.OrderBy(s => s.SourceId).Select(s => $"{s.SourceId}:{s.Config?.RootElement.GetRawText()}:{s.PollingInterval}"));

    private async Task SuperviseAsync(ICollector collector, IReadOnlyList<Source> sources, CancellationToken ct)
    {
        var backoff = MinBackoff;
        while (!ct.IsCancellationRequested)
        {
            var started = DateTimeOffset.UtcNow;
            try
            {
                await collector.RunAsync(sources, ct);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                logger.LogWarning("Collector {Name} exited without error; restarting", collector.Name);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Collector {Name} crashed; restarting in {Backoff}", collector.Name, backoff);
            }
            // Reset the backoff after a healthy stretch so a flaky source does not stay at the maximum forever.
            if (DateTimeOffset.UtcNow - started > TimeSpan.FromMinutes(10))
            {
                backoff = MinBackoff;
            }
            try
            {
                await Task.Delay(backoff, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }
}
