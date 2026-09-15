namespace Puluj.Api.Services;

public enum StatsBucket
{
    Hour,
    Day,
    Week,
}

/// <summary>
/// Period and bucket rules of the statistics endpoints, pure: the unit by span, the bucket starts in Kyiv time (the
/// same instants as `date_trunc(unit, ts, 'Europe/Kyiv')` in SQL, so a DST day is 23 or 25 hours long), the bucket an
/// instant belongs to, and the clamping of a requested period.
/// </summary>
public static class StatsBuckets
{
    public static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");

    public const int MaxDays = 366;

    public static StatsBucket BucketFor(TimeSpan span) =>
        span <= TimeSpan.FromDays(3) ? StatsBucket.Hour : span <= TimeSpan.FromDays(120) ? StatsBucket.Day : StatsBucket.Week;

    public static string UnitName(StatsBucket bucket) => bucket switch
    {
        StatsBucket.Hour => "hour",
        StatsBucket.Day => "day",
        _ => "week",
    };

    /// <summary>
    /// The endpoint contract: `to` not after now (rounded down to the minute), the span at most <see cref="MaxDays"/>
    /// and at least an hour; without `from` — the last 24 h. Both ends come back in UTC.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) Clamp(DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now)
    {
        var minute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Offset).ToUniversalTime();
        var end = to is { } t && t < minute ? t.ToUniversalTime() : minute;
        var start = from?.ToUniversalTime() ?? end.AddHours(-24);
        if (end - start > TimeSpan.FromDays(MaxDays))
        {
            start = end.AddDays(-MaxDays);
        }
        if (end - start < TimeSpan.FromHours(1))
        {
            start = end.AddHours(-1);
        }
        return (start, end);
    }

    /// <summary>
    /// Starts of every bucket that touches [from, to): the first is `from` truncated to the unit in Kyiv time, then one
    /// per unit until `to`. Days and weeks step on the local calendar; hours step in UTC, which is the same thing since
    /// Kyiv is a whole-hour offset.
    /// </summary>
    public static List<DateTimeOffset> Starts(DateTimeOffset from, DateTimeOffset to, StatsBucket bucket)
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
        var day = TimeZoneInfo.ConvertTime(from, Kyiv).Date;
        if (bucket == StatsBucket.Week)
        {
            day = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        }
        var step = bucket == StatsBucket.Week ? 7 : 1;
        for (var d = day; ; d = d.AddDays(step))
        {
            var start = LocalMidnight(d);
            if (start >= to)
            {
                break;
            }
            list.Add(start);
        }
        return list;
    }

    /// <summary>The UTC instant of a Kyiv-calendar midnight.</summary>
    public static DateTimeOffset LocalMidnight(DateTime day) =>
        new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified), Kyiv), TimeSpan.Zero);

    /// <summary>Index of the bucket an instant belongs to: the last start not after it (the first bucket for anything earlier).</summary>
    public static int IndexOf(IReadOnlyList<DateTimeOffset> starts, DateTimeOffset at)
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

    public static int HourOfDay(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Kyiv).Hour;

    public static DateOnly DayOf(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Kyiv).DateTime);
}
