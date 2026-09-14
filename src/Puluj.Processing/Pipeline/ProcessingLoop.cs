using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Ingestion;
using Puluj.Infrastructure.Messaging;
using Puluj.Infrastructure.Persistence;
using Puluj.Processing.Indexes;

namespace Puluj.Processing.Pipeline;

/// <summary>
/// Single consumer of the raw-message queue. The queue is fed by NOTIFY (RawMessageStored from any collector or the
/// admin panel, wherever they run) and by a sweep of RawMessages left Pending in the database (restart, crash,
/// retry, a reprocess, a history load, a lost notification) so nothing is lost when the signal is. The sweep hands them over
/// in publication order, oldest first, and keeps going without waiting while there is a backlog: a rebuild from the
/// raw messages then replays the situation as it unfolded, whichever channel each message came from. While a history
/// load is running (ReprocessService.PausedKey) the sweep waits, so the load's messages are not processed piecemeal.
/// </summary>
public sealed class ProcessingLoop(
    IRawMessageQueue queue,
    PgNotifyListener notifications,
    RawMessageProcessor processor,
    IndexProvider indexes,
    IDbContextFactory<PulujDbContext> factory,
    ReprocessService reprocess,
    IOptions<ProcessingOptions> options,
    ILogger<ProcessingLoop> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await indexes.Ready.WaitAsync(ct);
        logger.LogInformation("Processing loop started");
        var sweeper = SweepPendingAsync(ct);
        var listener = ListenAsync(ct);
        try
        {
            await foreach (var id in queue.DequeueAllAsync(ct))
            {
                await processor.ProcessAsync(id, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        await Task.WhenAll(sweeper, listener);
    }

    /// <summary>Queues every raw message announced over NOTIFY. An id queued twice (sweep + NOTIFY) is harmless: the processor skips non-Pending rows.</summary>
    private async Task ListenAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in notifications.ListenAsync(ct))
            {
                if (evt.Type == PulujEventType.RawMessageStored)
                {
                    await queue.EnqueueAsync(evt.Id, ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task SweepPendingAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(options.Value.PendingPollInterval);
        var pausedLogged = false;
        while (!ct.IsCancellationRequested)
        {
            var backlog = false;
            try
            {
                var paused = await reprocess.PausedAsync(ct);
                if (paused is not null)
                {
                    if (!pausedLogged)
                    {
                        logger.LogInformation("Processing paused: {Reason}", paused);
                    }
                    pausedLogged = true;
                }
                else
                {
                    if (pausedLogged)
                    {
                        logger.LogInformation("Processing resumed");
                    }
                    pausedLogged = false;
                    // Only hand over a new batch once the previous one is consumed: the same ids would otherwise be queued twice.
                    if (queue.Depth == 0)
                    {
                        List<long> ids;
                        await using (var db = await factory.CreateDbContextAsync(ct))
                        {
                            var max = options.Value.MaxAttempts;
                            ids = await db.RawMessages.AsNoTracking()
                                .Where(r => r.ProcessingStatus == ProcessingStatus.Pending && r.Attempts < max)
                                .OrderBy(r => r.PublishedAt).ThenBy(r => r.RawMessageId)
                                .Select(r => r.RawMessageId)
                                .Take(options.Value.PendingBatchSize)
                                .ToListAsync(ct);
                        }
                        foreach (var id in ids)
                        {
                            await queue.EnqueueAsync(id, ct);
                        }
                        if (ids.Count > 0)
                        {
                            logger.LogDebug("Sweeper queued {Count} pending raw message(s)", ids.Count);
                        }
                        backlog = ids.Count >= options.Value.PendingBatchSize;
                    }
                    else
                    {
                        backlog = true;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Pending sweep failed");
            }
            if (backlog)
            {
                // A backlog is drained as fast as the processor goes: look again as soon as the batch is consumed.
                try
                {
                    while (queue.Depth > 0)
                    {
                        await Task.Delay(100, ct);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                continue;
            }
            if (!await timer.WaitForNextTickAsync(ct))
            {
                return;
            }
        }
    }
}
