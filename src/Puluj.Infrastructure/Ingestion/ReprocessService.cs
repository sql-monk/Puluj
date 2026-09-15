using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Puluj.Infrastructure.Persistence;
using Puluj.Infrastructure.Settings;

namespace Puluj.Infrastructure.Ingestion;

/// <summary>
/// Rebuilds everything derived from the raw messages: drops targets, tracks, revisions, links, alerts, processing
/// errors and source statistics, and puts every raw message back to Pending. The processors then re-run the
/// pipeline over all of them in publication order (see ProcessingLoop), so the result is what live processing would
/// have produced had the messages arrived in that order. The raw messages themselves are never touched.
/// Plain DELETEs (not TRUNCATE) so the admin role, which has no TRUNCATE privilege, can run it too.
/// Safe next to running processors: the raw rows are reset first (waiting for the messages being processed to commit,
/// after which they are reset too), then the store lock is taken and the derived data deleted. A message claimed in
/// between finds its row Pending under the row lock and stores its targets after the delete: that is the rebuild.
/// Lock order raw rows -> AdvisoryLocks.Store is the processor's, so there is no cycle.
/// </summary>
public sealed class ReprocessService(IDbContextFactory<PulujDbContext> factory, SettingsStore settings, ILogger<ReprocessService> logger)
{
    /// <summary>Runtime status key: non-empty while processing must not pick up pending messages (a history load in progress).</summary>
    public const string PausedKey = "Processing:Paused";

    /// <summary>Children before parents (no reliance on cascades). Literal statements: nothing here comes from input.</summary>
    private static readonly (string Table, string Statement)[] Deletes =
    [
        ("target_links", "DELETE FROM target_links"),
        ("track_targets", "DELETE FROM track_targets"),
        ("target_track_revisions", "DELETE FROM target_track_revisions"),
        ("target_tracks", "DELETE FROM target_tracks"),
        ("targets", "DELETE FROM targets"),
        ("air_alerts", "DELETE FROM air_alerts"),
        ("processing_errors", "DELETE FROM processing_errors"),
        ("source_daily_stats", "DELETE FROM source_daily_stats"),
        ("source_copies", "DELETE FROM source_copies"),
    ];

    public async Task<int> ResetAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Failed ones too: a parser fix is one of the reasons to reprocess. Before the deletes: see the class summary.
        var reset = await db.Database.ExecuteSqlRawAsync(
            "UPDATE raw_messages SET processing_status = 0, attempts = 0, processed_at = NULL, claimed_by = NULL, claimed_at = NULL WHERE processing_status <> 0", ct);
        logger.LogInformation("Reprocess: {Rows} raw message(s) reset to Pending", reset);
        await db.Database.ExecuteSqlInterpolatedAsync(AdvisoryLocks.Take(AdvisoryLocks.Store), ct);
        foreach (var (table, statement) in Deletes)
        {
            var n = await db.Database.ExecuteSqlRawAsync(statement, ct);
            logger.LogInformation("Reprocess: cleared {Table} ({Rows} rows)", table, n);
        }
        await tx.CommitAsync(ct);
        var pending = await db.RawMessages.CountAsync(r => r.ProcessingStatus == Puluj.Domain.Enums.ProcessingStatus.Pending, ct);
        logger.LogInformation("Reprocess: {Count} raw message(s) queued for processing in publication order", pending);
        return pending;
    }

    /// <summary>Holds every processor instance: pending messages stay in the database until <see cref="ResumeAsync"/>.</summary>
    public Task PauseAsync(string reason, CancellationToken ct) => settings.SetStatusAsync(PausedKey, reason, ct);

    public Task ResumeAsync(CancellationToken ct) => settings.SetStatusAsync(PausedKey, null, ct);

    public async Task<string?> PausedAsync(CancellationToken ct)
    {
        var v = await settings.GetAsync($"Runtime:{PausedKey}", ct);
        return string.IsNullOrEmpty(v) ? null : v;
    }
}
