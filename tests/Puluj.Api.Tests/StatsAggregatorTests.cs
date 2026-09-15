using Puluj.Api.Services;
using Puluj.Domain.Enums;

namespace Puluj.Api.Tests;

public class StatsAggregatorTests
{
    private static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso, null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime();

    private static ReferenceCache.PlaceInfo Place(int id, string name, PlaceLevel level, int? parent = null) =>
        new(id, name, level, parent, "UA", 30, 50, 10, 0);

    [Theory]
    [InlineData(1, StatsBucket.Hour)]
    [InlineData(72, StatsBucket.Hour)]
    [InlineData(73, StatsBucket.Day)]
    [InlineData(24 * 120, StatsBucket.Day)]
    [InlineData(24 * 121, StatsBucket.Week)]
    public void BucketFor_PicksTheUnitByLength(int hours, StatsBucket expected)
    {
        Assert.Equal(expected, StatsAggregator.BucketFor(TimeSpan.FromHours(hours)));
    }

    [Fact]
    public void BucketStarts_Hours_StartAtTheTruncatedHourAndEndBeforeTo()
    {
        var starts = StatsAggregator.BucketStarts(Utc("2026-09-14T10:17:00Z"), Utc("2026-09-14T13:00:00Z"), StatsBucket.Hour);
        Assert.Equal([Utc("2026-09-14T10:00:00Z"), Utc("2026-09-14T11:00:00Z"), Utc("2026-09-14T12:00:00Z")], starts);
    }

    [Fact]
    public void BucketStarts_Days_AreKyivMidnightsAndTheDstDayIsShorter()
    {
        // Ukraine switched to summer time on 2025-03-30 (03:00 → 04:00): that local day has 23 hours.
        var starts = StatsAggregator.BucketStarts(Utc("2025-03-28T22:30:00Z"), Utc("2025-03-31T21:00:00Z"), StatsBucket.Day);
        Assert.Equal(
        [
            Utc("2025-03-28T22:00:00Z"), // 2025-03-29 00:00 +02
            Utc("2025-03-29T22:00:00Z"), // 2025-03-30 00:00 +02
            Utc("2025-03-30T21:00:00Z"), // 2025-03-31 00:00 +03
        ], starts);
        Assert.Equal(TimeSpan.FromHours(23), starts[2] - starts[1]);
    }

    [Fact]
    public void BucketStarts_Weeks_StartOnMonday()
    {
        // 2026-09-15 is a Tuesday; the week starts Monday 2026-09-14 00:00 +03 = 2026-09-13 21:00Z.
        var starts = StatsAggregator.BucketStarts(Utc("2026-09-15T05:00:00Z"), Utc("2026-09-29T00:00:00Z"), StatsBucket.Week);
        Assert.Equal([Utc("2026-09-13T21:00:00Z"), Utc("2026-09-20T21:00:00Z"), Utc("2026-09-27T21:00:00Z")], starts);
    }

    [Fact]
    public void BucketIndex_FindsTheLastStartNotAfterTheInstant()
    {
        var starts = new List<DateTimeOffset> { Utc("2026-01-01T00:00:00Z"), Utc("2026-01-01T01:00:00Z"), Utc("2026-01-01T02:00:00Z") };
        Assert.Equal(0, StatsAggregator.BucketIndex(starts, Utc("2025-12-31T23:00:00Z"))); // before the first bucket
        Assert.Equal(0, StatsAggregator.BucketIndex(starts, Utc("2026-01-01T00:59:00Z")));
        Assert.Equal(1, StatsAggregator.BucketIndex(starts, Utc("2026-01-01T01:00:00Z")));
        Assert.Equal(2, StatsAggregator.BucketIndex(starts, Utc("2026-01-01T09:00:00Z"))); // after the last start
    }

    [Fact]
    public void CategoryIndex_UnknownCodesFoldIntoTheLastSlot()
    {
        Assert.Equal(0, StatsAggregator.CategoryIndex("UAV"));
        Assert.Equal(1, StatsAggregator.CategoryIndex("MISSILE"));
        Assert.Equal(StatsAggregator.CategoryOrder.Length - 1, StatsAggregator.CategoryIndex("UNKNOWN"));
        Assert.Equal(StatsAggregator.CategoryOrder.Length - 1, StatsAggregator.CategoryIndex(null));
        Assert.Equal(StatsAggregator.CategoryOrder.Length - 1, StatsAggregator.CategoryIndex("SOMETHING_NEW"));
    }

    [Fact]
    public void ByRegion_FoldsPlacesIntoRegionsKeepsTopAndSumsTheRest()
    {
        var sumy = Place(1, "Сумська область", PlaceLevel.Region);
        var kyiv = Place(2, "Київська область", PlaceLevel.Region);
        var odesa = Place(3, "Одеська область", PlaceLevel.Region);
        var regions = new Dictionary<int, ReferenceCache.PlaceInfo> { [10] = sumy, [11] = sumy, [20] = kyiv, [30] = odesa };
        var rows = new List<(int?, long)> { (10, 5), (11, 7), (20, 4), (30, 1), (99, 100), (null, 3) };
        var result = StatsAggregator.ByRegion(rows, id => id is int i ? regions.GetValueOrDefault(i) : null, 2);
        Assert.Equal(3, result.Count);
        Assert.Equal((1, "Сумська область", 12), (result[0].Id, result[0].Name, result[0].Targets));
        Assert.Equal((2, "Київська область", 4), (result[1].Id, result[1].Name, result[1].Targets));
        Assert.Null(result[2].Id);
        Assert.Equal(StatsAggregator.OtherName, result[2].Name);
        Assert.Equal(1, result[2].Targets); // the unresolved 99 and the null place are not "other", they are simply not located
    }

    [Fact]
    public void Routes_FoldsPlacePairsIntoRegionPairs()
    {
        var sumy = Place(1, "Сумська", PlaceLevel.Region);
        var kyiv = Place(2, "Київська", PlaceLevel.Region);
        var regions = new Dictionary<int, ReferenceCache.PlaceInfo> { [10] = sumy, [11] = sumy, [20] = kyiv };
        var rows = new List<(int, int, long)> { (10, 20, 3), (11, 20, 2), (20, 10, 1), (10, 99, 50) };
        var result = StatsAggregator.Routes(rows, id => id is int i ? regions.GetValueOrDefault(i) : null, 10);
        Assert.Equal(2, result.Count);
        Assert.Equal((1, 2, 5), (result[0].FromId, result[0].ToId, result[0].Count));
        Assert.Equal((2, 1, 1), (result[1].FromId, result[1].ToId, result[1].Count));
    }

    [Fact]
    public void Alerts_ClipsToThePeriodSplitsHoursByBucketAndKeepsOnlyRegionLevel()
    {
        var from = Utc("2026-09-14T00:00:00Z");
        var to = Utc("2026-09-14T04:00:00Z");
        var starts = StatsAggregator.BucketStarts(from, to, StatsBucket.Hour);
        var places = new Dictionary<int, ReferenceCache.PlaceInfo>
        {
            [1] = Place(1, "Сумська область", PlaceLevel.Region),
            [2] = Place(2, "Київ", PlaceLevel.City),
            [3] = Place(3, "Конотопська громада", PlaceLevel.Hromada, 1),
        };
        var intervals = new[]
        {
            // Started before the period, ended inside: 1 h inside (00:00–01:00), whole duration 2 h.
            new AlertInterval(1, Utc("2026-09-13T23:00:00Z"), Utc("2026-09-14T01:00:00Z")),
            // Inside, spans two buckets: 01:30–02:30.
            new AlertInterval(2, Utc("2026-09-14T01:30:00Z"), Utc("2026-09-14T02:30:00Z")),
            // Still open: clipped at `to`, 03:00–04:00, not in the duration histogram.
            new AlertInterval(1, Utc("2026-09-14T03:00:00Z"), null),
            // Hromada level: ignored.
            new AlertInterval(3, Utc("2026-09-14T00:00:00Z"), Utc("2026-09-14T03:00:00Z")),
            // Ended before the period: ignored.
            new AlertInterval(1, Utc("2026-09-13T20:00:00Z"), Utc("2026-09-13T21:00:00Z")),
        };
        var stats = StatsAggregator.Alerts(intervals, from, to, starts, id => places.GetValueOrDefault(id));

        Assert.Equal(3, stats.Count);
        Assert.Equal(3.0, stats.Hours);
        Assert.Equal([0, 1, 0, 1], stats.CountPerBucket); // only starts inside the period count
        Assert.Equal([1.0, 0.5, 0.5, 1.0], stats.HoursPerBucket);
        Assert.Equal(2, stats.ByRegion.Count);
        Assert.Equal(("Сумська область", 2, 2.0), (stats.ByRegion[0].Name, stats.ByRegion[0].Count, stats.ByRegion[0].Hours));
        Assert.Equal(("Київ", 1, 1.0), (stats.ByRegion[1].Name, stats.ByRegion[1].Count, stats.ByRegion[1].Hours));
        // Bins are left-closed: the 60 min alert is "1–2 h", the 120 min one "2–4 h"; the open alert has no duration yet.
        Assert.Equal(1, stats.Durations.Single(d => d.Key == "1to2h").Count);
        Assert.Equal(1, stats.Durations.Single(d => d.Key == "2to4h").Count);
        Assert.Equal(2, stats.Durations.Sum(d => d.Count));
    }

    [Fact]
    public void Alerts_DurationBinsAreLeftClosed()
    {
        var from = Utc("2026-09-14T00:00:00Z");
        var to = Utc("2026-09-15T00:00:00Z");
        var places = new Dictionary<int, ReferenceCache.PlaceInfo> { [1] = Place(1, "Сумська область", PlaceLevel.Region) };
        var intervals = new[]
        {
            new AlertInterval(1, Utc("2026-09-14T00:00:00Z"), Utc("2026-09-14T00:29:00Z")),
            new AlertInterval(1, Utc("2026-09-14T01:00:00Z"), Utc("2026-09-14T01:30:00Z")),
            new AlertInterval(1, Utc("2026-09-14T02:00:00Z"), Utc("2026-09-14T03:00:00Z")),
            new AlertInterval(1, Utc("2026-09-14T04:00:00Z"), Utc("2026-09-14T13:00:00Z")),
        };
        var stats = StatsAggregator.Alerts(intervals, from, to, StatsAggregator.BucketStarts(from, to, StatsBucket.Day), id => places.GetValueOrDefault(id));
        Assert.Equal([1, 1, 1, 0, 0, 1], stats.Durations.Select(d => d.Count));
    }

    [Fact]
    public void Slices_KeepEnumOrderDropZerosAndLabelValues()
    {
        var rows = new List<(int, long)> { ((int)EventType.ExplosionReport, 2), ((int)EventType.TargetObserved, 10), (999, 1) };
        var slices = StatsAggregator.Slices(rows, StatsAggregator.EventTypeLabels);
        Assert.Equal(["TargetObserved", "ExplosionReport"], slices.Select(s => s.Key));
        Assert.Equal("вибухи", slices[1].Label);
        Assert.Equal(10, slices[0].Count);
    }

    [Fact]
    public void IsRegionLevel_AcceptsOblastsAndParentlessCities()
    {
        Assert.True(StatsAggregator.IsRegionLevel(Place(1, "Сумська область", PlaceLevel.Region)));
        Assert.True(StatsAggregator.IsRegionLevel(Place(2, "Київ", PlaceLevel.City)));
        Assert.False(StatsAggregator.IsRegionLevel(Place(3, "Суми", PlaceLevel.City, 1)));
        Assert.False(StatsAggregator.IsRegionLevel(Place(4, "Громада", PlaceLevel.Hromada, 1)));
        Assert.False(StatsAggregator.IsRegionLevel(null));
    }
}
