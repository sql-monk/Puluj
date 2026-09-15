using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Puluj.Contracts;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Api.Services;

/// <summary>
/// Read side of the statistics page: one payload per tab (targets, alerts, sources, recognition) for one period,
/// aggregated in SQL (grouped rows only, never the targets themselves) and folded with the reference cache. The result
/// is the same for every viewer, so it is cached per section and period for a couple of minutes; the client rounds
/// the period end so that the presets share one entry.
/// </summary>
public sealed class StatsService(IDbContextFactory<PulujDbContext> factory, ReferenceCache refs, IMemoryCache cache, TimeProvider clock)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);
    private const int TopRegions = 15;
    private const int MaxRoutes = 400;
    private const int TopDays = 10;
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
    private sealed record MessageBucketRow(DateTimeOffset BucketAt, long Processed, long WithTargets);

    public Task<StatsTargetsDto> TargetsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) => CachedAsync("targets", from, to, ComputeTargetsAsync, ct);
    public Task<StatsAlertsDto> AlertsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) => CachedAsync("alerts", from, to, ComputeAlertsAsync, ct);
    public Task<StatsSourcesDto> SourcesAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) => CachedAsync("sources", from, to, ComputeSourcesAsync, ct);
    public Task<StatsRecognitionDto> RecognitionAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) => CachedAsync("recognition", from, to, ComputeRecognitionAsync, ct);

    /// <summary>
    /// Clamps the period to the endpoint rules and serves the section from the cache (key `section|from|to`). One
    /// computation per key at a time; a cancelled request must not poison the cache, so the work runs to completion on
    /// its own and a failed one is evicted.
    /// </summary>
    private async Task<T> CachedAsync<T>(string section, DateTimeOffset? from, DateTimeOffset? to, Func<Period, CancellationToken, Task<T>> compute, CancellationToken ct)
    {
        var (start, end) = StatsBuckets.Clamp(from, to, clock.GetUtcNow());
        var key = $"stats|{section}|{start:O}|{end:O}";
        if (!cache.TryGetValue(key, out Task<T>? task) || task is null)
        {
            task = compute(Period.Of(start, end), CancellationToken.None);
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

    /// <summary>A clamped period with its bucket unit and starts — the part every section shares.</summary>
    private sealed record Period(DateTimeOffset From, DateTimeOffset To, StatsBucket Bucket, string Unit, List<DateTimeOffset> Starts)
    {
        public static Period Of(DateTimeOffset from, DateTimeOffset to)
        {
            var bucket = StatsBuckets.BucketFor(to - from);
            return new Period(from, to, bucket, StatsBuckets.UnitName(bucket), StatsBuckets.Starts(from, to, bucket));
        }

        public StatsPeriodDto Dto => new(From, To, Unit, Starts);

        public int[] Series<TRow>(IEnumerable<TRow> rows, Func<TRow, DateTimeOffset> at, Func<TRow, long> n)
        {
            var series = new int[Starts.Count];
            if (Starts.Count > 0)
            {
                foreach (var r in rows)
                {
                    series[StatsBuckets.IndexOf(Starts, at(r))] += (int)n(r);
                }
            }
            return series;
        }
    }

    // Facts: reported targets that are not a repeat of another source's fact. The same filter in every targets query.
    private async Task<StatsTargetsDto> ComputeTargetsAsync(Period p, CancellationToken ct)
    {
        await refs.Ready.WaitAsync(ct);
        var (from, to, unit) = (p.From, p.To, p.Unit);
        var categories = StatsFolds.CategoryOrder.Length;
        var categoryIndex = refs.Categories.Values.ToDictionary(c => c.TargetCategoryId, c => StatsFolds.CategoryIndex(c.Code));
        int IndexOfCategory(int? id) => id is int i && categoryIndex.TryGetValue(i, out var ix) ? ix : categories - 1;

        await using var db = await factory.CreateDbContextAsync(ct);
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
            WHERE observed_at >= {from} AND observed_at < {to} AND event_type = 1 AND duplicate_of_target_id IS NULL
            GROUP BY 1
            """).ToListAsync(ct);
        var routeRows = await db.Database.SqlQuery<RouteRow>($"""
            SELECT origin_place_id AS origin_place_id, destination_place_id AS destination_place_id, count(*) AS n
            FROM targets
            WHERE observed_at >= {from} AND observed_at < {to} AND event_type = 1 AND duplicate_of_target_id IS NULL
              AND origin_place_id IS NOT NULL AND destination_place_id IS NOT NULL
            GROUP BY 1, 2
            """).ToListAsync(ct);
        // extract() returns numeric on PG14+: cast, or the int columns of the row type will not bind.
        var hourRows = await db.Database.SqlQuery<HourRow>($"""
            SELECT CAST(extract(dow FROM observed_at AT TIME ZONE {Tz}) AS int) AS dow, CAST(extract(hour FROM observed_at AT TIME ZONE {Tz}) AS int) AS hour, count(*) AS n
            FROM targets
            WHERE observed_at >= {from} AND observed_at < {to} AND event_type = 1 AND duplicate_of_target_id IS NULL
            GROUP BY 1, 2
            """).ToListAsync(ct);

        var targetsPerBucket = new int[p.Starts.Count][];
        var tracksPerBucket = new int[p.Starts.Count][];
        for (var i = 0; i < p.Starts.Count; i++)
        {
            targetsPerBucket[i] = new int[categories];
            tracksPerBucket[i] = new int[categories];
        }
        if (p.Starts.Count > 0)
        {
            foreach (var r in targetBuckets)
            {
                targetsPerBucket[StatsBuckets.IndexOf(p.Starts, r.BucketAt)][IndexOfCategory(r.CategoryId)] += (int)r.N;
            }
            foreach (var r in trackBuckets)
            {
                tracksPerBucket[StatsBuckets.IndexOf(p.Starts, r.BucketAt)][IndexOfCategory(r.CategoryId)] += (int)r.N;
            }
        }

        // Classes: facts and tracks side by side; facts without a class fold into the category's "unclassified" row.
        var tracksByClass = trackClassRows.GroupBy(r => (r.ClassId, r.CategoryId)).ToDictionary(g => g.Key, g => g.Sum(r => r.N));
        var byClass = classRows
            .Select(r =>
            {
                var cls = r.ClassId is int cid ? refs.Classes.GetValueOrDefault(cid) : null;
                var cat = r.CategoryId is int catId ? refs.Categories.GetValueOrDefault(catId) : null;
                var code = cls?.Code ?? $"{cat?.Code ?? "UNKNOWN"}_UNCLASSIFIED";
                var name = cls?.Name ?? (cat is null ? "Невідома загроза" : $"{cat.Name} (без класу)");
                return new StatsClassDto(code, name, StatsFolds.CategoryOrder[IndexOfCategory(r.CategoryId)], (int)r.N,
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

        var (byRegion, unlocated) = StatsFolds.ByRegion(placeRows.Select(r => (r.PlaceId, r.N)), refs.RegionOf, TopRegions);
        return new StatsTargetsDto(p.Dto,
            (int)targetBuckets.Sum(r => r.N),
            (int)trackBuckets.Sum(r => r.N),
            classRows.Sum(r => r.Objects ?? 0),
            StatsFolds.CategoryOrder.Select(code => new StatsCategoryDto(code, refs.Categories.Values.FirstOrDefault(c => c.Code == code)?.Name ?? code)).ToList(),
            targetsPerBucket, tracksPerBucket, byClass, byRegion, (int)unlocated,
            StatsFolds.Routes(routeRows.Select(r => (r.OriginPlaceId, r.DestinationPlaceId, r.N)), refs.RegionOf, MaxRoutes),
            hourWeekday);
    }

    // One query brings the intervals (thousands of rows at most); everything else is AlertIntervals in memory.
    private async Task<StatsAlertsDto> ComputeAlertsAsync(Period p, CancellationToken ct)
    {
        await refs.Ready.WaitAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var alertRows = await db.Database.SqlQuery<AlertRow>($"""
            SELECT place_id AS place_id, started_at AS started_at, ended_at AS ended_at FROM air_alerts
            WHERE started_at < {p.To} AND (ended_at IS NULL OR ended_at > {p.From})
            """).ToListAsync(ct);
        var s = AlertIntervals.Summarize(alertRows.Select(a => new AlertInterval(a.PlaceId, a.StartedAt, a.EndedAt)), p.From, p.To, p.Starts, id => refs.Place(id));
        var topDays = s.Days.Where(d => d.Hours > 0).OrderByDescending(d => d.Hours).ThenBy(d => d.Day).Take(TopDays).ToList();
        return new StatsAlertsDto(p.Dto, s.Count, s.Hours, s.OpenAtEnd, s.DeclaredPerBucket, s.HoursPerBucket, s.ByRegion, s.Durations, s.DeclaredByHour, topDays);
    }

    // Messages by published_at. Lag is only meaningful for messages collected live: a history load receives years-old posts at once.
    private async Task<StatsSourcesDto> ComputeSourcesAsync(Period p, CancellationToken ct)
    {
        await refs.Ready.WaitAsync(ct);
        var (from, to, unit) = (p.From, p.To, p.Unit);
        await using var db = await factory.CreateDbContextAsync(ct);
        var sourceRows = await db.Database.SqlQuery<SourceRow>($"""
            SELECT r.source_id AS source_id, count(*) AS messages,
                   count(*) FILTER (WHERE r.processing_status = 1) AS processed,
                   count(*) FILTER (WHERE r.processing_status = 1 AND EXISTS (SELECT 1 FROM targets t WHERE t.raw_message_id = r.raw_message_id)) AS with_targets,
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

        var series = sourceBucketRows.GroupBy(r => r.SourceId).ToDictionary(g => g.Key, g => p.Series(g, r => r.BucketAt, r => r.N));
        var targetsBySource = sourceTargetRows.ToDictionary(r => r.SourceId, r => (int)r.N);
        var sources = sourceRows
            .Select(r =>
            {
                var s = refs.Sources.GetValueOrDefault(r.SourceId);
                return new StatsSourceDto(r.SourceId, s?.Code ?? $"#{r.SourceId}", s?.Name ?? $"#{r.SourceId}", (int)r.Messages, (int)r.Processed, (int)r.WithTargets,
                    targetsBySource.GetValueOrDefault(r.SourceId), r.MedianLag is { } lag ? Math.Round(lag) : null, series.GetValueOrDefault(r.SourceId) ?? new int[p.Starts.Count]);
            })
            .OrderByDescending(s => s.Messages)
            .ToList();
        return new StatsSourcesDto(p.Dto, sources.Sum(s => s.Messages), sources.Sum(s => s.Processed), sources.Sum(s => s.WithTargets), sources.Sum(s => s.Targets), sources);
    }

    private async Task<StatsRecognitionDto> ComputeRecognitionAsync(Period p, CancellationToken ct)
    {
        await refs.Ready.WaitAsync(ct);
        var (from, to, unit) = (p.From, p.To, p.Unit);
        await using var db = await factory.CreateDbContextAsync(ct);
        // Event types count every event (alerts, explosions…), the other slices only facts.
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
        var messageRows = await db.Database.SqlQuery<MessageBucketRow>($"""
            SELECT date_trunc({unit}, r.published_at, {Tz}) AS bucket_at,
                   count(*) FILTER (WHERE r.processing_status = 1) AS processed,
                   count(*) FILTER (WHERE r.processing_status = 1 AND EXISTS (SELECT 1 FROM targets t WHERE t.raw_message_id = r.raw_message_id)) AS with_targets
            FROM raw_messages r
            WHERE r.published_at >= {from} AND r.published_at < {to}
            GROUP BY 1
            """).ToListAsync(ct);

        return new StatsRecognitionDto(p.Dto,
            (int)methodRows.Sum(r => r.N),
            (int)messageRows.Sum(r => r.Processed),
            (int)messageRows.Sum(r => r.WithTargets),
            StatsFolds.Slices(eventRows.Select(r => (r.Value, r.N)), StatsFolds.EventTypeLabels),
            StatsFolds.Slices(methodRows.Select(r => (r.Value, r.N)), StatsFolds.MethodLabels),
            StatsFolds.Slices(confidenceRows.Select(r => (r.Value, r.N)), StatsFolds.ConfidenceLabels),
            StatsFolds.Slices(locationRows.Select(r => (r.Value, r.N)), StatsFolds.LocationKindLabels),
            p.Series(messageRows, r => r.BucketAt, r => r.Processed),
            p.Series(messageRows, r => r.BucketAt, r => r.WithTargets));
    }
}
