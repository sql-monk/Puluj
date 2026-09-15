using Puluj.Analytics.Persistence;
using Puluj.Analytics.Reporting;

namespace Puluj.Analytics.Tests;

public class PairDelaysTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(59.9, 0)]
    [InlineData(60, 1)]
    [InlineData(299, 1)]
    [InlineData(300, 2)]
    [InlineData(900, 3)]
    [InlineData(3599, 3)]
    [InlineData(3600, 4)]
    [InlineData(21599, 4)]
    [InlineData(21600, 5)]
    [InlineData(1_000_000, 5)]
    [InlineData(-5, 0)]
    public void Bucket_edges_are_inclusive_at_the_lower_end(double seconds, int expected)
    {
        Assert.Equal(expected, PairDelays.BucketIndex(seconds));
    }

    [Fact]
    public void Histogram_has_six_fixed_buckets_with_an_open_last_one()
    {
        var h = PairDelays.Histogram([10, 70, 70, 400, 1000, 5000, 30000]);
        Assert.Equal(6, h.Count);
        Assert.Equal([1, 2, 1, 1, 1, 1], h.Select(b => b.Count));
        Assert.Equal((0, 60d), (h[0].FromSeconds, h[0].ToSeconds));
        Assert.Equal((3600, 21600d), (h[4].FromSeconds, h[4].ToSeconds));
        Assert.Equal(21600, h[5].FromSeconds);
        Assert.Null(h[5].ToSeconds);
        Assert.All(PairDelays.Histogram([]), b => Assert.Equal(0, b.Count));
    }

    [Fact]
    public void Median_interpolates_like_percentile_cont()
    {
        Assert.Null(PairDelays.Median([]));
        Assert.Equal(5, PairDelays.Median([5]));
        Assert.Equal(20, PairDelays.Median([30, 10, 20]));
        Assert.Equal(25, PairDelays.Median([40, 10, 30, 20]));
    }

    [Fact]
    public void Summary_counts_primary_edges_only_except_count_all()
    {
        PairCopyRow[] rows =
        [
            new(30, (int)CopyKind.Verbatim, true, 0.95),
            new(120, (int)CopyKind.Near, true, 0.75),
            new(600, (int)CopyKind.Forward, true, 1.0),
            new(45, (int)CopyKind.Verbatim, false, 0.99), // intermediate original: counted in CountAll only
        ];
        var d = PairDelays.Summarize(7, 2, 14, rows, []);
        Assert.Equal((7, 2, 14), (d.CopierId, d.OriginalId, d.Days));
        Assert.Equal((3, 4, 1, 1), (d.Count, d.CountAll, d.Verbatim, d.Forwards));
        Assert.Equal(250, d.AvgDelaySeconds);
        Assert.Equal(120, d.MedianDelaySeconds);
        Assert.Equal(30, d.MinDelaySeconds);
        Assert.Equal(0.9, d.AvgJaccard);
        Assert.Equal([1, 1, 1, 0, 0, 0], d.Delays.Select(b => b.Count));
        Assert.Empty(d.Recent);
    }

    [Fact]
    public void Summary_of_an_empty_pair_has_no_aggregates()
    {
        var d = PairDelays.Summarize(1, 2, 7, [], []);
        Assert.Equal((0, 0), (d.Count, d.CountAll));
        Assert.Null(d.AvgDelaySeconds);
        Assert.Null(d.MedianDelaySeconds);
        Assert.Null(d.MinDelaySeconds);
        Assert.Null(d.AvgJaccard);
        Assert.Equal(6, d.Delays.Count);
    }
}
