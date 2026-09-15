using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Processing.Pipeline;

/// <summary>
/// Hands raw messages out to processor instances through the database: a claim flips a Pending row to InProgress with
/// the claimant's name in one statement (FOR UPDATE SKIP LOCKED), so two workers never take the same row, wherever they
/// run. A claim whose instance died is returned by the lease sweep. Raw NpgsqlCommands: EF's SqlQuery wraps its text in
/// a subquery, where UPDATE ... RETURNING is not allowed.
/// </summary>
public sealed class RawMessageClaims(
    IDbContextFactory<PulujDbContext> factory,
    IOptions<ProcessingOptions> options,
    TimeProvider clock,
    ILogger<RawMessageClaims> logger)
{
    private const int InProgress = (int)ProcessingStatus.InProgress;
    private const int Pending = (int)ProcessingStatus.Pending;
    private const int Failed = (int)ProcessingStatus.Failed;

    /// <summary>Takes the oldest-published Pending message nobody holds; null when there is none.</summary>
    public async Task<long?> ClaimOldestAsync(string instance, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var conn = await OpenAsync(db, ct);
        await using var cmd = new NpgsqlCommand(
            $"""
            WITH next AS (
                SELECT raw_message_id FROM raw_messages
                WHERE processing_status = {Pending} AND attempts < @max
                ORDER BY published_at, raw_message_id
                LIMIT 1 FOR UPDATE SKIP LOCKED)
            UPDATE raw_messages r SET processing_status = {InProgress}, claimed_by = @name, claimed_at = now()
            FROM next WHERE r.raw_message_id = next.raw_message_id
            RETURNING r.raw_message_id
            """, conn);
        cmd.Parameters.AddWithValue("max", options.Value.MaxAttempts);
        cmd.Parameters.AddWithValue("name", instance);
        return await cmd.ExecuteScalarAsync(ct) as long?;
    }

    /// <summary>Takes one announced message if it is still Pending (every instance hears the NOTIFY; one wins).</summary>
    public async Task<bool> ClaimAsync(long rawMessageId, string instance, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var conn = await OpenAsync(db, ct);
        await using var cmd = new NpgsqlCommand(
            $"""
            UPDATE raw_messages SET processing_status = {InProgress}, claimed_by = @name, claimed_at = now()
            WHERE raw_message_id = @id AND processing_status = {Pending} AND attempts < @max
            """, conn);
        cmd.Parameters.AddWithValue("id", rawMessageId);
        cmd.Parameters.AddWithValue("max", options.Value.MaxAttempts);
        cmd.Parameters.AddWithValue("name", instance);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>Gives a claimed but unprocessed message back (shutdown): no attempt counted, no wait for the lease.</summary>
    public async Task ReleaseAsync(long rawMessageId, string instance)
    {
        await using var db = await factory.CreateDbContextAsync();
        var conn = await OpenAsync(db, CancellationToken.None);
        await using var cmd = new NpgsqlCommand(
            $"""
            UPDATE raw_messages SET processing_status = {Pending}, claimed_by = NULL, claimed_at = NULL
            WHERE raw_message_id = @id AND processing_status = {InProgress} AND claimed_by = @name
            """, conn);
        cmd.Parameters.AddWithValue("id", rawMessageId);
        cmd.Parameters.AddWithValue("name", instance);
        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    /// <summary>
    /// Returns claims older than the lease to Pending, counting the attempt (a message that keeps killing its processor
    /// ends up Failed, not in an endless loop). A row whose processor is still alive is locked by its transaction, so the
    /// update waits and then sees it Processed. Returns how many rows were touched.
    /// </summary>
    public async Task<int> ReclaimExpiredAsync(CancellationToken ct)
    {
        var max = options.Value.MaxAttempts;
        var failed = new List<(long Id, int SourceId)>();
        var returned = 0;
        await using var db = await factory.CreateDbContextAsync(ct);
        var conn = await OpenAsync(db, ct);
        await using (var cmd = new NpgsqlCommand(
            $"""
            UPDATE raw_messages SET
                attempts = attempts + 1,
                processing_status = CASE WHEN attempts + 1 >= @max THEN {Failed} ELSE {Pending} END,
                processed_at = CASE WHEN attempts + 1 >= @max THEN now() ELSE NULL END,
                claimed_by = NULL, claimed_at = NULL
            WHERE processing_status = {InProgress} AND claimed_at < now() - @lease
            RETURNING raw_message_id, source_id, processing_status
            """, conn))
        {
            cmd.Parameters.AddWithValue("max", max);
            cmd.Parameters.Add(new NpgsqlParameter("lease", NpgsqlDbType.Interval) { Value = options.Value.ClaimLease });
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.GetInt32(2) == Failed)
                {
                    failed.Add((reader.GetInt64(0), reader.GetInt32(1)));
                }
                else
                {
                    returned++;
                }
            }
        }
        if (failed.Count > 0)
        {
            var now = clock.GetUtcNow();
            db.ProcessingErrors.AddRange(failed.Select(f => new ProcessingError
            {
                RawMessageId = f.Id,
                SourceId = f.SourceId,
                Stage = "lease",
                Message = $"Claim expired for the {max}. time (lease {options.Value.ClaimLease}); the processor died on this message",
                OccurredAt = now,
            }));
            await db.SaveChangesAsync(ct);
        }
        if (returned + failed.Count > 0)
        {
            logger.LogWarning("Expired claims: {Returned} returned to Pending, {Failed} failed", returned, failed.Count);
        }
        return returned + failed.Count;
    }

    private static async Task<NpgsqlConnection> OpenAsync(PulujDbContext db, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        return conn;
    }
}
