using Puluj.Api.Services;

namespace Puluj.Api.Tests;

public class StatsBucketsTests
{
    private static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso, null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime();

    [Theory]
    [InlineData(1, StatsBucket.Hour)]
    [InlineData(72, StatsBucket.Hour)]
    [InlineData(73, StatsBucket.Day)]
    [InlineData(24 * 120, StatsBucket.Day)]
    [InlineData(24 * 121, StatsBucket.Week)]
    public void BucketFor_PicksTheUnitByLength(int hours, StatsBucket expected)
    {
        Assert.Equal(expected, StatsBuckets.BucketFor(TimeSpan.FromHours(hours)));
    }

    [Fact]
    public void Clamp_EndNotAfterNowSpanAtMostAYearAtLeastAnHour()
    {
        var now = Utc("2026-09-15T10:07:42Z");
        // Defaults: the last 24 h up to now rounded down to the minute.
        Assert.Equal((Utc("2026-09-14T10:07:00Z"), Utc("2026-09-15T10:07:00Z")), StatsBuckets.Clamp(null, null, now));
        // A future end is pulled back to now.
        Assert.Equal((Utc("2026-09-15T00:00:00Z"), Utc("2026-09-15T10:07:00Z")), StatsBuckets.Clamp(Utc("2026-09-15T00:00:00Z"), Utc("2026-09-16T00:00:00Z"), now));
        // Too long: the start moves up.
        var (from, to) = StatsBuckets.Clamp(Utc("2020-01-01T00:00:00Z"), Utc("2026-01-01T00:00:00Z"), now);
        Assert.Equal(Utc("2026-01-01T00:00:00Z"), to);
        Assert.Equal(TimeSpan.FromDays(StatsBuckets.MaxDays), to - from);
        // Too short: at least an hour.
        Assert.Equal((Utc("2026-09-14T09:00:00Z"), Utc("2026-09-14T10:00:00Z")), StatsBuckets.Clamp(Utc("2026-09-14T09:50:00Z"), Utc("2026-09-14T10:00:00Z"), now));
        // Offsets are normalised to UTC.
        var local = StatsBuckets.Clamp(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.FromHours(3)), null, now);
        Assert.Equal(TimeSpan.Zero, local.From.Offset);
        Assert.Equal(Utc("2026-09-14T09:00:00Z"), local.From);
    }

    [Fact]
    public void Starts_Hours_StartAtTheTruncatedHourAndEndBeforeTo()
    {
        var starts = StatsBuckets.Starts(Utc("2026-09-14T10:17:00Z"), Utc("2026-09-14T13:00:00Z"), StatsBucket.Hour);
        Assert.Equal([Utc("2026-09-14T10:00:00Z"), Utc("2026-09-14T11:00:00Z"), Utc("2026-09-14T12:00:00Z")], starts);
    }

    [Fact]
    public void Starts_Days_AreKyivMidnightsAndTheDstDayIsShorter()
    {
        // Ukraine switched to summer time on 2025-03-30 (03:00 → 04:00): that local day has 23 hours.
        var starts = StatsBuckets.Starts(Utc("2025-03-28T22:30:00Z"), Utc("2025-03-31T21:00:00Z"), StatsBucket.Day);
        Assert.Equal(
        [
            Utc("2025-03-28T22:00:00Z"), // 2025-03-29 00:00 +02
            Utc("2025-03-29T22:00:00Z"), // 2025-03-30 00:00 +02
            Utc("2025-03-30T21:00:00Z"), // 2025-03-31 00:00 +03
        ], starts);
        Assert.Equal(TimeSpan.FromHours(23), starts[2] - starts[1]);
    }

    [Fact]
    public void Starts_Days_TheAutumnDstDayIsLonger()
    {
        // `to` is the third midnight, so exactly two days: a bucket starting at `to` would be empty.
        var starts = StatsBuckets.Starts(Utc("2025-10-25T21:00:00Z"), Utc("2025-10-27T22:00:00Z"), StatsBucket.Day);
        Assert.Equal([Utc("2025-10-25T21:00:00Z"), Utc("2025-10-26T22:00:00Z")], starts);
        Assert.Equal(TimeSpan.FromHours(25), starts[1] - starts[0]);
    }

    [Fact]
    public void Starts_Weeks_StartOnMonday()
    {
        // 2026-09-15 is a Tuesday; the week starts Monday 2026-09-14 00:00 +03 = 2026-09-13 21:00Z.
        var starts = StatsBuckets.Starts(Utc("2026-09-15T05:00:00Z"), Utc("2026-09-29T00:00:00Z"), StatsBucket.Week);
        Assert.Equal([Utc("2026-09-13T21:00:00Z"), Utc("2026-09-20T21:00:00Z"), Utc("2026-09-27T21:00:00Z")], starts);
    }

    [Fact]
    public void IndexOf_FindsTheLastStartNotAfterTheInstant()
    {
        var starts = new List<DateTimeOffset> { Utc("2026-01-01T00:00:00Z"), Utc("2026-01-01T01:00:00Z"), Utc("2026-01-01T02:00:00Z") };
        Assert.Equal(0, StatsBuckets.IndexOf(starts, Utc("2025-12-31T23:00:00Z"))); // before the first bucket
        Assert.Equal(0, StatsBuckets.IndexOf(starts, Utc("2026-01-01T00:59:00Z")));
        Assert.Equal(1, StatsBuckets.IndexOf(starts, Utc("2026-01-01T01:00:00Z")));
        Assert.Equal(2, StatsBuckets.IndexOf(starts, Utc("2026-01-01T09:00:00Z"))); // after the last start
    }

    [Fact]
    public void HourOfDayAndDayOf_AreKyivLocal()
    {
        Assert.Equal(1, StatsBuckets.HourOfDay(Utc("2026-09-14T22:30:00Z"))); // +03 in summer
        Assert.Equal(new DateOnly(2026, 9, 15), StatsBuckets.DayOf(Utc("2026-09-14T22:30:00Z")));
        Assert.Equal(0, StatsBuckets.HourOfDay(Utc("2026-01-14T22:30:00Z"))); // +02 in winter
    }
}
