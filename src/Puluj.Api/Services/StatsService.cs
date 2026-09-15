using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Puluj.Contracts;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Api.Services;

/// <summary>
/// Read side of the statistics page: every section of <see cref="StatsDto"/> for one period, aggregated in SQL
/// (grouped rows only, never the targets themselves) and folded with the reference cache. The result is the same for
/// every viewer, so it is cached per period for a couple of minutes; the client rounds the period end so that the
/// presets share one entry.
/// </summary>
public sealed class StatsService(IDbContextFactory<PulujDbContext> factory, ReferenceCache refs, IMemoryCache cache, TimeProvider clock)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);
    private const int TopRegions = 15;
    private const int MaxRoutes = 400;
    private const string Tz = "Europe/Kyiv";

    // Unmapped query types follow the snake_case naming convention: the SQL columns are aliased to match.
    private sealed record BucketCategoryRow(DateTimeOffset BucketAt, int? CategoryId, long N);
    private sealed record ClassRow(int? ClassId, int? CategoryId, long N, long? Objects);
    private sealed record PlaceRow(int? PlaceId, long N);
    private sealed record RouteRow(int OriginPlaceId, int DestinationPlaceId, long N);
    private sealed record HourRow(int Dow, int Hour, long N);
    private sealed record ValueRow(int Value, long N);
    private sealed record AlertRow(int PlaceId, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);
    private sealed record SourceRow(int SourceId, long Messages, long Processed, long WithTargets, double? MedianLag);
    private sealed record SourceBucketRow(int SourceId, DateTimeOffset BucketAt, long N);
    private sealed record SourceTargetsRow(int SourceId, long N);

    /// <summary>Clamps the period to the rules of the endpoint (end not in the future, at most MaxDays, at least an hour) and returns the stats, cached.</summary>
    public Task<StatsDto> GetAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        now = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        var end = to is { } t && t < now ? t : now;
        var start = from ?? end.AddHours(-24);
        if (end - start > TimeSpan.FromDays(StatsAggregator.MaxDays))
        {
            start = end.AddDays(-StatsAggregator.MaxDays);
        }
        if (end - start < TimeSpan.FromHours(1))
        {
            start = end.AddHours(-1);
        }
        start = start.ToUniversalTime();
        end = end.ToUniversalTime();
        var key = $"stats|{start:O}|{end:O}";
        return CachedAsync(key, start, end, ct);
    }

    /// <summary>One computation per period at a time; a cancelled request must not poison the cache, so the work runs to completion on its own.</summary>
    private async Task<StatsDto> CachedAsync(string key, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (!cache.TryGetValue(key, out Task<StatsDto>? task) || task is null)
        {
            task = ComputeAsync(from, to, CancellationToken.None);
            _ = cache.Set(key, task, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = CacheTtl });
        }
        try
        {
            return await task.WaitAsync(ct);
        }
        catch (Exception) when (task.IsFaulted)
        {
            cache.Remove(key);
            throw;
        }
    }

    private async Task<StatsDto> ComputeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await refs.Ready.WaitAsync(ct);
        var bucket = StatsAggregator.BucketFor(to - from);
        var unit = StatsAggregator.UnitName(bucket);
        var starts = StatsAggregator.BucketStarts(from, to, bucket);
        var categories = StatsAggregator.CategoryOrder.Length;
        var categoryIndex = refs.Categories.Values.ToDictionary(c => c.TargetCategoryId, c => StatsAggregator.CategoryIndex(c.Code));
        int IndexOfCategory(int? id) => id is int i && categoryIndex.TryGetValue(i, out var ix) ? ix : categories - 1;

        await using var db = await factory.CreateDbContextAsync(ct);

        // Facts: reported targets that are not a repeat of another source's fact.
        var targetBuckets = await db.Database.SqlQuery<BucketCategoryRow>($"""
            SELECT date_trunc({unit}, observed_at, {Tz}) AS bucket_at, target_category_id AS category_id, count(*) AS n
            FROM targets
            WHERE observed_at >= {from} AND observed_at < {to} AND event_type = 1 AND duplicate_of_target_id IS NULL
            GROUP BY 1, 2
            """).ToListAsync(ct);
        var trackBuckets = await db.Database.SqlQuery<BucketCategoryRow>($"""
            SELECT date_trunc({unit}, first_seen_at, {Tz}) AS bucket_at, target_category_id AS category_id, count(*) AS n
            FROM target_tracks
            WHERE first_seen_at >= {from} AND first_seen_at < {to}
            GROUP BY 1, 2
            """).ToListAsync(ct);
        var classRows = await db.Database.SqlQuery<ClassRow>($"""
            SELECT target_class_id AS class_id, target_category_id AS category_id, count(*) AS n, sum(object_count) AS objects
            FROM targets
            WHERE observed_at >= {from} AND observed_at < {to} AND event_type = 1 AND duplicate_of_target_id IS NULL
            GROUP BY 1, 2
            """).ToListAsync(ct);
        var trackClassRows = await db.Database.SqlQuery<ClassRow>($"""
            SELECT target_class_id AS class_id, target_category_id AS category_id, count(*) AS n, NULL::bigint AS objects
            FROM target_tracks
            WHERE first_seen_at >= {from} AND first_seen_at < {to}
            GROUP BY 1, 2
            """).ToListAsync(ct);
        var placeRows = await db.Database.SqlQuery<PlaceRow>($"""
            SELECT location_place_id AS place_id, count(*) AS n
            FROM targets
            WHERE observed_at >= {from} AND observed_at < {to} AND event_type = 1 AND duplicate_of_target_id IS NULL AND location_place_id IS NOT NULL
            GROUP BY 1
            """).ToListAsync(ct);
        var routeRows = await db.Database.SqlQuery<RouteRow>($"""
            SELECT origin_place_id AS origin_place_id, destination_place_id AS destination_place_id, count(*) AS n
            FROM targets
            WHERE observed_at >= {from} AND observed_at < {to} AND event_type = 1 AND duplicate_of_target_id IS NULL
              AND origin_place_id IS NOT NULL AND destination_place_id IS NOT NULL
            GROUP BY 1, 2
            """).ToListAsync(ct);
        var hourRows = await db.Database.SqlQuery<HourRow>($"""
            SELECT CAST(extract(dow FROM observed_at AT TIME ZONE {Tz}) AS int) AS dow, CAST(extract(hour FROM observed_at AT TIME ZONE {Tz}) AS int) AS hour, count(*) AS n
            FROM targets
            WHERE observed_at >= {from} AND observed_at < {to} AND event_type = 1 AND duplicate_of_target_id IS NULL
            GROUP BY 1, 2
            """).ToListAsync(ct);
        var eventRows = await db.Database.SqlQuery<ValueRow>($"""
            SELECT event_type AS value, count(*) AS n FROM targets
            WHERE observed_at >= {from} AND observed_at < {to} AND duplicate_of_target_id IS NULL
            GROUP BY 1
            """).ToListAsync(ct);
        var methodRows = await db.Database.SqlQuery<ValueRow>($"""
            SELECT identification_method AS value, count(*) AS n FROM targets
            WHERE observed_at >= {from} AND observed_at < {to} AND event_type = 1 AND duplicate_of_target_id IS NULL
            GROUP BY 1
            """).ToListAsync(ct);
        var confidenceRows = await db.Database.SqlQuery<ValueRow>($"""
            SELECT confidence AS value, count(*) AS n FROM targets
            WHERE observed_at >= {from} AND observed_at < {to} AND event_type = 1 AND duplicate_of_target_id IS NULL
            GROUP BY 1
            """).ToListAsync(ct);
        var locationRows = await db.Database.SqlQuery<ValueRow>($"""
            SELECT location_kind AS value, count(*) AS n FROM targets
            WHERE observed_at >= {from} AND observed_at < {to} AND event_type = 1 AND duplicate_of_target_id IS NULL
            GROUP BY 1
            """).ToListAsync(ct);
        var alertRows = await db.Database.SqlQuery<AlertRow>($"""
            SELECT place_id AS place_id, started_at AS started_at, ended_at AS ended_at FROM air_alerts
            WHERE started_at < {to} AND (ended_at IS NULL OR ended_at > {from})
            """).ToListAsync(ct);
        // Lag is only meaningful for messages collected live: a history load receives years-old posts at once.
        var sourceRows = await db.Database.SqlQuery<SourceRow>($"""
            SELECT r.source_id AS source_id, count(*) AS messages,
                   count(*) FILTER (WHERE r.processing_status = 1) AS processed,
                   count(*) FILTER (WHERE EXISTS (SELECT 1 FROM targets t WHERE t.raw_message_id = r.raw_message_id)) AS with_targets,
                   percentile_cont(0.5) WITHIN GROUP (ORDER BY CAST(extract(epoch FROM r.received_at - r.published_at) AS float8))
                       FILTER (WHERE r.received_at >= r.published_at AND r.received_at - r.published_at < interval '6 hours') AS median_lag
            FROM raw_messages r
            WHERE r.published_at >= {from} AND r.published_at < {to}
            GROUP BY 1
            """).ToListAsync(ct);
        var sourceBucketRows = await db.Database.SqlQuery<SourceBucketRow>($"""
            SELECT source_id AS source_id, date_trunc({unit}, published_at, {Tz}) AS bucket_at, count(*) AS n
            FROM raw_messages
            WHERE published_at >= {from} AND published_at < {to}
            GROUP BY 1, 2
            """).ToListAsync(ct);
        var sourceTargetRows = await db.Database.SqlQuery<SourceTargetsRow>($"""
            SELECT source_id AS source_id, count(*) AS n FROM targets
            WHERE observed_at >= {from} AND observed_at < {to} AND event_type = 1 AND duplicate_of_target_id IS NULL
            GROUP BY 1
            """).ToListAsync(ct);

        // Timeline: one row per bucket, categories in the fixed order.
        var targetsPerBucket = new int[starts.Count][];
        var tracksPerBucket = new int[starts.Count][];
        for (var i = 0; i < starts.Count; i++)
        {
            targetsPerBucket[i] = new int[categories];
            tracksPerBucket[i] = new int[categories];
        }
        if (starts.Count > 0)
        {
            foreach (var r in targetBuckets)
            {
                targetsPerBucket[StatsAggregator.BucketIndex(starts, r.BucketAt)][IndexOfCategory(r.CategoryId)] += (int)r.N;
            }
            foreach (var r in trackBuckets)
            {
                tracksPerBucket[StatsAggregator.BucketIndex(starts, r.BucketAt)][IndexOfCategory(r.CategoryId)] += (int)r.N;
            }
        }
        var alerts = StatsAggregator.Alerts(alertRows.Select(a => new AlertInterval(a.PlaceId, a.StartedAt, a.EndedAt)), from, to, starts, id => refs.Place(id));
        var timeline = starts.Select((at, i) => new StatsBucketDto(at, targetsPerBucket[i], tracksPerBucket[i], alerts.CountPerBucket[i], alerts.HoursPerBucket[i])).ToList();

        // Classes: facts and tracks side by side; facts without a class fold into the category's "unclassified" row.
        var tracksByClass = trackClassRows.GroupBy(r => (r.ClassId, r.CategoryId)).ToDictionary(g => g.Key, g => g.Sum(r => r.N));
        var byClass = classRows
            .Select(r =>
            {
                var cls = r.ClassId is int cid ? refs.Classes.GetValueOrDefault(cid) : null;
                var cat = r.CategoryId is int catId ? refs.Categories.GetValueOrDefault(catId) : null;
                var code = cls?.Code ?? $"{cat?.Code ?? "UNKNOWN"}_UNCLASSIFIED";
                var name = cls?.Name ?? (cat is null ? "Невідома загроза" : $"{cat.Name} (без класу)");
                return new StatsClassDto(code, name, StatsAggregator.CategoryOrder[IndexOfCategory(r.CategoryId)], (int)r.N,
                    (int)tracksByClass.GetValueOrDefault((r.ClassId, r.CategoryId)), r.Objects ?? 0);
            })
            .OrderByDescending(c => c.Targets)
            .ToList();

        var hourWeekday = new int[7][];
        for (var d = 0; d < 7; d++)
        {
            hourWeekday[d] = new int[24];
        }
        foreach (var r in hourRows)
        {
            // Postgres: 0 = Sunday; the client reads Monday first.
            hourWeekday[(r.Dow + 6) % 7][r.Hour] += (int)r.N;
        }

        var sourceSeries = sourceBucketRows.GroupBy(r => r.SourceId).ToDictionary(g => g.Key, g =>
        {
            var series = new int[starts.Count];
            if (starts.Count > 0)
            {
                foreach (var r in g)
                {
                    series[StatsAggregator.BucketIndex(starts, r.BucketAt)] += (int)r.N;
                }
            }
            return series;
        });
        var targetsBySource = sourceTargetRows.ToDictionary(r => r.SourceId, r => (int)r.N);
        var sources = sourceRows
            .Select(r =>
            {
                var s = refs.Sources.GetValueOrDefault(r.SourceId);
                return new StatsSourceDto(r.SourceId, s?.Code ?? $"#{r.SourceId}", s?.Name ?? $"#{r.SourceId}", (int)r.Messages, (int)r.Processed, (int)r.WithTargets,
                    targetsBySource.GetValueOrDefault(r.SourceId), r.MedianLag is { } lag ? Math.Round(lag) : null, sourceSeries.GetValueOrDefault(r.SourceId) ?? new int[starts.Count]);
            })
            .OrderByDescending(s => s.Messages)
            .ToList();

        var totals = new StatsTotalsDto(
            (int)targetBuckets.Sum(r => r.N),
            (int)trackBuckets.Sum(r => r.N),
            classRows.Sum(r => r.Objects ?? 0),
            alerts.Count,
            alerts.Hours,
            sources.Sum(s => s.Messages),
            sources.Sum(s => s.Processed),
            sources.Sum(s => s.WithTargets),
            sources.Count);

        return new StatsDto(from, to, unit, starts, totals,
            StatsAggregator.CategoryOrder.Select(code => new StatsCategoryDto(code, refs.Categories.Values.FirstOrDefault(c => c.Code == code)?.Name ?? code)).ToList(),
            timeline, byClass,
            StatsAggregator.ByRegion(placeRows.Select(r => (r.PlaceId, r.N)), refs.RegionOf, TopRegions),
            StatsAggregator.Routes(routeRows.Select(r => (r.OriginPlaceId, r.DestinationPlaceId, r.N)), refs.RegionOf, MaxRoutes),
            hourWeekday,
            StatsAggregator.Slices(eventRows.Select(r => (r.Value, r.N)), StatsAggregator.EventTypeLabels),
            StatsAggregator.Slices(methodRows.Select(r => (r.Value, r.N)), StatsAggregator.MethodLabels),
            StatsAggregator.Slices(confidenceRows.Select(r => (r.Value, r.N)), StatsAggregator.ConfidenceLabels),
            StatsAggregator.Slices(locationRows.Select(r => (r.Value, r.N)), StatsAggregator.LocationKindLabels),
            alerts.ByRegion, alerts.Durations, sources);
    }
}
