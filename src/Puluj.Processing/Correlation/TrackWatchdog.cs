using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Messaging;
using Puluj.Infrastructure.Persistence;
using Puluj.Processing.Indexes;

namespace Puluj.Processing.Correlation;

/// <summary>Closes tracks that have not been updated for 2x their class correlation window (spec §12 fading ends in closure).</summary>
public sealed class TrackWatchdog(
    IDbContextFactory<PulujDbContext> factory,
    IndexProvider indexes,
    INotifyPublisher notifier,
    IOptionsMonitor<CorrelationOptions> options,
    TimeProvider clock,
    ILogger<TrackWatchdog> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await indexes.Ready.WaitAsync(ct);
        using var timer = new PeriodicTimer(options.CurrentValue.WatchdogInterval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await SweepAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Track watchdog sweep failed");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var db = await factory.CreateDbContextAsync(ct);
        var active = await db.TargetTracks.Where(t => t.Status == TrackStatus.Active).ToListAsync(ct);
        var closed = new List<long>();
        foreach (var t in active)
        {
            var window = indexes.Taxonomy.ClassProfile(t.TargetClassId)?.CorrelationWindowMinutes ?? 30;
            if (now - t.LastSeenAt < TimeSpan.FromMinutes(window * options.CurrentValue.CloseAfterWindows))
            {
                continue;
            }
            t.Status = TrackStatus.Closed;
            t.ClosedReason = "timeout";
            t.UpdatedAt = now;
            db.TargetTrackRevisions.Add(TrackUpdater.Revision(t, null, now));
            closed.Add(t.TargetTrackId);
        }
        // Free-text alerts rarely get an explicit "відбій" for every raion: expire them after a few hours.
        var staleBefore = now - Structured.TextAlertSink.MaxAge;
        var expired = await db.AirAlerts
            .Where(a => a.EndedAt == null && a.SourceAlertId.StartsWith(Structured.TextAlertSink.KeyPrefix) && a.StartedAt < staleBefore)
            .ToListAsync(ct);
        foreach (var a in expired)
        {
            a.EndedAt = now;
        }
        if (closed.Count == 0 && expired.Count == 0)
        {
            return;
        }
        await db.SaveChangesAsync(ct);
        foreach (var id in closed)
        {
            await notifier.PublishAsync(new PulujEvent(PulujEventType.TrackClosed, id, now), ct);
        }
        foreach (var a in expired)
        {
            await notifier.PublishAsync(new PulujEvent(PulujEventType.AlertChanged, a.AirAlertId, now), ct);
        }
        logger.LogInformation("Watchdog closed {Count} stale track(s), expired {Alerts} text alert(s)", closed.Count, expired.Count);
    }
}
