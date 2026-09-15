using Puluj.Contracts;
using Puluj.Domain.Enums;

namespace Puluj.Api.Services;

public enum StatsBucket
{
    Hour,
    Day,
    Week,
}

/// <summary>Air-raid alert interval as stored: place, start, end (null while open).</summary>
public sealed record AlertInterval(int PlaceId, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

/// <summary>Everything derived from the alert intervals of a period (see <see cref="StatsAggregator.Alerts"/>).</summary>
public sealed record AlertStats(
    IReadOnlyList<StatsAlertRegionDto> ByRegion,
    IReadOnlyList<StatsSliceDto> Durations,
    int[] CountPerBucket,
    double[] HoursPerBucket,
    int Count,
    double Hours);

/// <summary>
/// The pure half of the statistics page: bucketing in Kyiv time (the same way as `date_trunc(unit, ts, 'Europe/Kyiv')`),
/// folding places into regions, top-N with an "other" row, and everything about alerts (clipping to the period,
/// duration histogram, hours per bucket). No database here, so it is unit-tested on its own.
/// </summary>
public static class StatsAggregator
{
    public static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");

    /// <summary>The order categories take in every per-category array — the client's palette is keyed the same way.</summary>
    public static readonly string[] CategoryOrder = ["UAV", "MISSILE", "GUIDED_BOMB", "AIRCRAFT", "UNKNOWN"];

    public const int MaxDays = 366;
    public const string OtherName = "Інші";

    public static StatsBucket BucketFor(TimeSpan span) =>
        span <= TimeSpan.FromDays(3) ? StatsBucket.Hour : span <= TimeSpan.FromDays(120) ? StatsBucket.Day : StatsBucket.Week;

    public static string UnitName(StatsBucket bucket) => bucket switch
    {
        StatsBucket.Hour => "hour",
        StatsBucket.Day => "day",
        _ => "week",
    };

    /// <summary>
    /// Starts of every bucket that touches [from, to): the first is `from` truncated to the unit in Kyiv time, then one
    /// per unit until `to`. Days and weeks step on the local calendar (a DST day is 23 or 25 hours long, as in Postgres);
    /// hours step in UTC, which is the same thing since Kyiv is a whole-hour offset.
    /// </summary>
    public static List<DateTimeOffset> BucketStarts(DateTimeOffset from, DateTimeOffset to, StatsBucket bucket)
    {
        var list = new List<DateTimeOffset>();
        if (bucket == StatsBucket.Hour)
        {
            var utc = from.UtcDateTime;
            var cur = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
            for (; cur < to; cur = cur.AddHours(1))
            {
                list.Add(cur);
            }
            return list;
        }
        var local = TimeZoneInfo.ConvertTime(from, Kyiv);
        var day = local.Date;
        if (bucket == StatsBucket.Week)
        {
            day = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        }
        var step = bucket == StatsBucket.Week ? 7 : 1;
        for (var d = day; ; d = d.AddDays(step))
        {
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(d, DateTimeKind.Unspecified), Kyiv), TimeSpan.Zero);
            if (start >= to)
            {
                break;
            }
            list.Add(start);
        }
        return list;
    }

    /// <summary>Index of the bucket an instant belongs to: the last start not after it (the first bucket for anything earlier).</summary>
    public static int BucketIndex(IReadOnlyList<DateTimeOffset> starts, DateTimeOffset at)
    {
        int lo = 0, hi = starts.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (starts[mid] <= at)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return lo;
    }

    /// <summary>Position of a category code in <see cref="CategoryOrder"/>; anything unknown (or null) counts as UNKNOWN.</summary>
    public static int CategoryIndex(string? code)
    {
        var i = Array.IndexOf(CategoryOrder, code ?? "");
        return i < 0 ? CategoryOrder.Length - 1 : i;
    }

    /// <summary>Counts per place folded into their region, the top rows kept and the rest summed into one "other" row.</summary>
    public static List<StatsRegionDto> ByRegion(IEnumerable<(int? PlaceId, long Count)> rows, Func<int?, ReferenceCache.PlaceInfo?> regionOf, int top)
    {
        var byRegion = new Dictionary<int, (string Name, long Count)>();
        foreach (var (placeId, count) in rows)
        {
            if (regionOf(placeId) is not { } region)
            {
                continue;
            }
            byRegion[region.Id] = (region.Name, byRegion.GetValueOrDefault(region.Id).Count + count);
        }
        var ordered = byRegion.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Value.Name).ToList();
        var result = ordered.Take(top).Select(kv => new StatsRegionDto(kv.Key, kv.Value.Name, (int)kv.Value.Count)).ToList();
        var rest = ordered.Skip(top).Sum(kv => kv.Value.Count);
        if (rest > 0)
        {
            result.Add(new StatsRegionDto(null, OtherName, (int)rest));
        }
        return result;
    }

    /// <summary>Origin → destination pairs folded into region pairs, most frequent first, at most `cap` rows.</summary>
    public static List<StatsRouteDto> Routes(IEnumerable<(int OriginPlaceId, int DestinationPlaceId, long Count)> rows, Func<int?, ReferenceCache.PlaceInfo?> regionOf, int cap)
    {
        var pairs = new Dictionary<(int, int), (string From, string To, long Count)>();
        foreach (var (origin, destination, count) in rows)
        {
            if (regionOf(origin) is not { } a || regionOf(destination) is not { } b)
            {
                continue;
            }
            var key = (a.Id, b.Id);
            pairs[key] = (a.Name, b.Name, pairs.GetValueOrDefault(key).Count + count);
        }
        return pairs.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Value.From).ThenBy(kv => kv.Value.To)
            .Take(cap)
            .Select(kv => new StatsRouteDto(kv.Key.Item1, kv.Value.From, kv.Key.Item2, kv.Value.To, (int)kv.Value.Count))
            .ToList();
    }

    /// <summary>Only alerts declared for a whole oblast (or Kyiv city) are comparable between regions and summable in hours.</summary>
    public static bool IsRegionLevel(ReferenceCache.PlaceInfo? place) =>
        place is not null && (place.Level == PlaceLevel.Region || (place.Level == PlaceLevel.City && place.ParentId is null));

    public static readonly (string Key, string Label, double MaxMinutes)[] DurationBins =
    [
        ("lt30", "до 30 хв", 30),
        ("30to60", "30–60 хв", 60),
        ("1to2h", "1–2 год", 120),
        ("2to4h", "2–4 год", 240),
        ("4to8h", "4–8 год", 480),
        ("gt8h", "понад 8 год", double.PositiveInfinity),
    ];

    /// <summary>
    /// Region-level alerts of a period: per region (count and hours inside [from, to)), the duration histogram of the
    /// ended ones (whole duration, not clipped), and per bucket how many were declared and how many hours were under
    /// alert (an interval is split at bucket borders).
    /// </summary>
    public static AlertStats Alerts(IEnumerable<AlertInterval> intervals, DateTimeOffset from, DateTimeOffset to, IReadOnlyList<DateTimeOffset> buckets, Func<int, ReferenceCache.PlaceInfo?> place)
    {
        var byRegion = new Dictionary<int, (string Name, int Count, double Hours)>();
        var durations = new int[DurationBins.Length];
        var countPerBucket = new int[buckets.Count];
        var hoursPerBucket = new double[buckets.Count];
        var count = 0;
        var hours = 0.0;
        foreach (var a in intervals)
        {
            var p = place(a.PlaceId);
            if (!IsRegionLevel(p))
            {
                continue;
            }
            var start = a.StartedAt > from ? a.StartedAt : from;
            var end = a.EndedAt is { } e && e < to ? e : to;
            if (end <= start)
            {
                continue;
            }
            var h = (end - start).TotalHours;
            count++;
            hours += h;
            var cur = byRegion.GetValueOrDefault(p!.Id);
            byRegion[p.Id] = (p.Name, cur.Count + 1, cur.Hours + h);
            if (a.EndedAt is { } ended)
            {
                var minutes = (ended - a.StartedAt).TotalMinutes;
                durations[Array.FindIndex(DurationBins, b => minutes < b.MaxMinutes)]++;
            }
            if (buckets.Count == 0)
            {
                continue;
            }
            if (a.StartedAt >= from && a.StartedAt < to)
            {
                countPerBucket[BucketIndex(buckets, a.StartedAt)]++;
            }
            var i = BucketIndex(buckets, start);
            for (var t = start; t < end && i < buckets.Count; i++)
            {
                var next = i + 1 < buckets.Count ? buckets[i + 1] : to;
                var stop = next < end ? next : end;
                hoursPerBucket[i] += (stop - t).TotalHours;
                t = stop;
            }
        }
        var regions = byRegion.OrderByDescending(kv => kv.Value.Hours).ThenBy(kv => kv.Value.Name)
            .Select(kv => new StatsAlertRegionDto(kv.Key, kv.Value.Name, kv.Value.Count, Math.Round(kv.Value.Hours, 2)))
            .ToList();
        var bins = DurationBins.Select((b, i) => new StatsSliceDto(b.Key, b.Label, durations[i])).ToList();
        return new AlertStats(regions, bins, countPerBucket, hoursPerBucket.Select(x => Math.Round(x, 2)).ToArray(), count, Math.Round(hours, 2));
    }

    public static readonly IReadOnlyDictionary<EventType, string> EventTypeLabels = new Dictionary<EventType, string>
    {
        [EventType.TargetObserved] = "ціль зафіксовано",
        [EventType.AirRaidAlert] = "повітряна тривога",
        [EventType.AlertCancelled] = "відбій тривоги",
        [EventType.TargetCancelled] = "ціль минула",
        [EventType.ExplosionReport] = "вибухи",
        [EventType.AirDefenseActivity] = "робота ППО",
        [EventType.Unknown] = "невідомо",
    };

    public static readonly IReadOnlyDictionary<IdentificationMethod, string> MethodLabels = new Dictionary<IdentificationMethod, string>
    {
        [IdentificationMethod.Structured] = "структуроване джерело",
        [IdentificationMethod.Rule] = "правила",
        [IdentificationMethod.Llm] = "мовна модель",
        [IdentificationMethod.Manual] = "вручну",
    };

    public static readonly IReadOnlyDictionary<ConfidenceLevel, string> ConfidenceLabels = new Dictionary<ConfidenceLevel, string>
    {
        [ConfidenceLevel.Unknown] = "невідомо",
        [ConfidenceLevel.Low] = "низька",
        [ConfidenceLevel.Medium] = "середня",
        [ConfidenceLevel.High] = "висока",
        [ConfidenceLevel.Confirmed] = "підтверджено",
    };

    public static readonly IReadOnlyDictionary<LocationKind, string> LocationKindLabels = new Dictionary<LocationKind, string>
    {
        [LocationKind.Unknown] = "без локації",
        [LocationKind.DirectionOnly] = "лише напрямок",
        [LocationKind.Region] = "область",
        [LocationKind.District] = "район",
        [LocationKind.City] = "населений пункт",
        [LocationKind.Area] = "акваторія / зона",
        [LocationKind.Point] = "точка",
    };

    /// <summary>Counts per enum value as slices, every value of the enum present (zero when absent), in enum order.</summary>
    public static List<StatsSliceDto> Slices<T>(IEnumerable<(int Value, long Count)> rows, IReadOnlyDictionary<T, string> labels) where T : struct, Enum
    {
        var counts = rows.GroupBy(r => r.Value).ToDictionary(g => g.Key, g => g.Sum(r => r.Count));
        return Enum.GetValues<T>()
            .Select(v => new StatsSliceDto(v.ToString(), labels.GetValueOrDefault(v, v.ToString()), (int)counts.GetValueOrDefault(Convert.ToInt32(v))))
            .Where(s => s.Count > 0)
            .ToList();
    }
}
