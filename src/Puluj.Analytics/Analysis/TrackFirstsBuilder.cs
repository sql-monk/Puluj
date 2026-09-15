using Microsoft.EntityFrameworkCore;
using Puluj.Analytics.Persistence;

namespace Puluj.Analytics.Analysis;

/// <summary>
/// Rebuilds `track_firsts` for the last N days from the pipeline's tracks: for every track, the source of its earliest
/// target is "first"; every source in the track takes part; the others lag by the gap between their earliest target
/// and the track's first. Days are Kyiv calendar days of the track's first observation. Rebuilt as a whole (delete +
/// insert) so a reprocess that renumbers the tracks cannot leave stale rows behind.
/// </summary>
public static class TrackFirstsBuilder
{
    public static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");

    public static async Task<int> RebuildAsync(AnalyticsDbContext db, int days, DateTimeOffset now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Kyiv).DateTime);
        var sinceDay = today.AddDays(-(days - 1));
        var sinceUtc = TimeZoneInfo.ConvertTimeToUtc(sinceDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Kyiv);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"DELETE FROM analytics.track_firsts WHERE day >= {sinceDay}", ct);
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO analytics.track_firsts (day, source_id, target_category_id, category_code, firsts, participations, lag_seconds_sum, lag_count)
            WITH per_source AS (
                SELECT tt.target_track_id, tt.target_category_id, t.source_id, min(t.observed_at) AS first_observed
                FROM target_tracks tt
                JOIN track_targets x ON x.target_track_id = tt.target_track_id
                JOIN targets t ON t.target_id = x.target_id
                WHERE tt.first_seen_at >= {sinceUtc}
                GROUP BY 1, 2, 3
            ), ranked AS (
                SELECT p.*,
                       row_number() OVER (PARTITION BY target_track_id ORDER BY first_observed, source_id) AS rn,
                       min(first_observed) OVER (PARTITION BY target_track_id) AS track_first
                FROM per_source p
            )
            SELECT (r.track_first AT TIME ZONE 'Europe/Kyiv')::date AS day, r.source_id, r.target_category_id, c.code,
                   count(*) FILTER (WHERE r.rn = 1)::int,
                   count(*)::int,
                   coalesce(sum(extract(epoch FROM (r.first_observed - r.track_first))) FILTER (WHERE r.rn > 1), 0),
                   count(*) FILTER (WHERE r.rn > 1)::int
            FROM ranked r
            JOIN target_categories c ON c.target_category_id = r.target_category_id
            WHERE (r.track_first AT TIME ZONE 'Europe/Kyiv')::date >= {sinceDay}
            GROUP BY 1, 2, 3, 4
            """, ct);
        await tx.CommitAsync(ct);
        return inserted;
    }
}
