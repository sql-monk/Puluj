using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Ingestion;
using Puluj.Infrastructure.Persistence;
using Puluj.Processing.Indexes;

namespace Puluj.Processing.Pipeline;

/// <summary>
/// Single consumer of the raw-message queue. Also sweeps RawMessages left Pending in the database
/// (restart, crash, retry) so nothing is lost when the in-process signal is.
/// </summary>
public sealed class ProcessingLoop(
    IRawMessageQueue queue,
    RawMessageProcessor processor,
    IndexProvider indexes,
    IDbContextFactory<PulujDbContext> factory,
    IOptions<ProcessingOptions> options,
    ILogger<ProcessingLoop> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await indexes.Ready.WaitAsync(ct);
        logger.LogInformation("Processing loop started");
        var sweeper = SweepPendingAsync(ct);
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
        await sweeper;
    }

    private async Task SweepPendingAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(options.Value.PendingPollInterval);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                List<long> ids;
                await using (var db = await factory.CreateDbContextAsync(ct))
                {
                    var max = options.Value.MaxAttempts;
                    ids = await db.RawMessages.AsNoTracking()
                        .Where(r => r.ProcessingStatus == ProcessingStatus.Pending && r.Attempts < max)
                        .OrderBy(r => r.RawMessageId)
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
                    logger.LogDebug("Sweeper re-queued {Count} pending raw message(s)", ids.Count);
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
            if (!await timer.WaitForNextTickAsync(ct))
            {
                return;
            }
        }
    }
}
