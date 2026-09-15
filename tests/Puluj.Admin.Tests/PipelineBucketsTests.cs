namespace Puluj.Admin.Tests;

public class PipelineBucketsTests
{
    [Fact]
    public void A_day_is_24_hour_buckets_ending_with_the_current_hour()
    {
        var now = new DateTimeOffset(2026, 9, 15, 10, 7, 42, TimeSpan.Zero);

        var (from, unit, starts) = PipelineBuckets.Period(24, now);

        Assert.Equal("hour", unit);
        Assert.Equal(24, starts.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 11, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(from, starts[0]);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero), starts[^1]);
    }

    [Fact]
    public void A_week_is_7_kyiv_days_ending_today()
    {
        // 23:30 UTC on the 14th is already the 15th in Kyiv (UTC+3 in September).
        var now = new DateTimeOffset(2026, 9, 14, 23, 30, 0, TimeSpan.Zero);

        var (from, unit, starts) = PipelineBuckets.Period(168, now);

        Assert.Equal("day", unit);
        Assert.Equal(7, starts.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 21, 0, 0, TimeSpan.Zero), from); // Kyiv midnight of the 9th
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 21, 0, 0, TimeSpan.Zero), starts[^1]); // Kyiv midnight of the 15th
    }

    [Fact]
    public void A_month_is_30_days_and_steps_over_the_dst_change_on_the_local_calendar()
    {
        // Clocks go back on 2026-10-25 in Ukraine: that day is 25 hours long.
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        var (_, unit, starts) = PipelineBuckets.Period(720, now);

        Assert.Equal("day", unit);
        Assert.Equal(30, starts.Count);
        var oct25 = starts.Single(s => TimeZoneInfo.ConvertTime(s, PipelineBuckets.Kyiv).Day == 25 && TimeZoneInfo.ConvertTime(s, PipelineBuckets.Kyiv).Month == 10);
        var next = starts[starts.IndexOf(oct25) + 1];
        Assert.Equal(TimeSpan.FromHours(25), next - oct25);
        Assert.All(starts, s => Assert.Equal(TimeSpan.Zero, TimeZoneInfo.ConvertTime(s, PipelineBuckets.Kyiv).TimeOfDay));
    }

    [Fact]
    public void Index_finds_the_bucket_an_instant_falls_into()
    {
        var now = new DateTimeOffset(2026, 9, 15, 10, 7, 42, TimeSpan.Zero);
        var (_, _, starts) = PipelineBuckets.Period(24, now);

        Assert.Equal(0, PipelineBuckets.Index(starts, starts[0]));
        Assert.Equal(0, PipelineBuckets.Index(starts, starts[0].AddMinutes(59)));
        Assert.Equal(1, PipelineBuckets.Index(starts, starts[0].AddHours(1)));
        Assert.Equal(23, PipelineBuckets.Index(starts, now));
        Assert.Equal(0, PipelineBuckets.Index(starts, starts[0].AddDays(-1))); // before the period: first bucket, never out of range
    }

    [Theory]
    [InlineData(24, "hour")]
    [InlineData(168, "day")]
    [InlineData(720, "day")]
    public void Unit_is_hours_for_a_day_and_days_beyond(int hours, string unit)
    {
        Assert.Equal(unit, PipelineBuckets.Unit(hours));
    }
}
