using Puluj.Processing.Pipeline;

namespace Puluj.Processing.Tests.Pipeline;

public class ProcessingStatsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 15, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Percentiles_are_nearest_rank_over_the_recorded_samples()
    {
        var clock = new FakeClock(T0);
        var stats = new ProcessingStats(clock);
        foreach (var ms in new[] { 50, 10, 40, 20, 30, 60, 70, 80, 90, 100 })
        {
            stats.Record("parse", ms);
        }
        var parse = stats.Snapshot(2).Parse;
        Assert.Equal(10, parse.Samples);
        Assert.Equal(55, parse.MeanMs);
        Assert.Equal(50, parse.P50Ms); // rank ceil(0.5 * 10) = 5th of 10..100
        Assert.Equal(90, parse.P90Ms); // rank ceil(0.9 * 10) = 9th
        Assert.Equal(100, parse.MaxMs);

        stats.Record("total", 7);
        var single = stats.Snapshot(2).Total;
        Assert.Equal((1, 7.0, 7.0, 7.0, 7.0), (single.Samples, single.MeanMs, single.P50Ms, single.P90Ms, single.MaxMs));
        Assert.Equal(0, stats.Snapshot(2).Lock.Samples); // untouched stage: empty, not an error
    }

    [Fact]
    public void Timings_and_throughput_only_count_the_last_five_minutes()
    {
        var clock = new FakeClock(T0);
        var stats = new ProcessingStats(clock);
        stats.Record("store", 500);
        stats.Outcome("processed", 1);
        clock.Now = T0.AddMinutes(4);
        stats.Record("store", 20);
        stats.Outcome("processed", 2);
        stats.Outcome("processed", 3);
        clock.Now = T0.AddMinutes(4).AddSeconds(30);
        stats.Outcome("processed", 4);
        Assert.Equal(4, stats.Snapshot(1).Processed);

        clock.Now = T0.AddMinutes(5).AddSeconds(29);
        var snapshot = stats.Snapshot(1);
        Assert.Equal(1, snapshot.Store.Samples); // the 500 ms sample from T0 fell out of the window
        Assert.Equal(20, snapshot.Store.MaxMs);
        Assert.Equal(4, snapshot.Processed); // the counter is for the process lifetime
        Assert.Equal(1.0, snapshot.PerMinute1); // one processing in the last 60 s (T0+4:30, 59 s ago)
        Assert.Equal(3 / 5.0, snapshot.PerMinute5); // three within the last five minutes
        Assert.Equal(T0.AddMinutes(4).AddSeconds(30), snapshot.LastProcessedAt);
        Assert.Equal(4, snapshot.LastRawMessageId);
    }

    [Fact]
    public void Outcomes_are_counted_separately_and_only_success_moves_throughput()
    {
        var stats = new ProcessingStats(new FakeClock(T0));
        stats.Outcome("processed", 1);
        stats.Outcome("skipped", 2);
        stats.Outcome("failed", 3);
        stats.Outcome("retried", 4);
        stats.Outcome("retried", 5);
        stats.Outcome("retried_transient", 6);
        stats.Outcome("released", 7); // not an outcome of processing: ignored
        var s = stats.Snapshot(3);
        Assert.Equal((3, 1L, 1L, 1L, 2L, 1L), (s.Concurrency, s.Processed, s.Skipped, s.Failed, s.Retried, s.RetriedTransient));
        Assert.Equal(1.0, s.PerMinute1);
        Assert.Equal(1, s.LastRawMessageId);
    }

    [Fact]
    public void Claims_are_listed_oldest_first_until_released()
    {
        var clock = new FakeClock(T0);
        var stats = new ProcessingStats(clock);
        stats.Claimed(10);
        clock.Now = T0.AddSeconds(1);
        stats.Claimed(11);
        clock.Now = T0.AddSeconds(2);
        stats.Claimed(9);
        Assert.Equal([10L, 11L, 9L], stats.Snapshot(1).Claims.Select(c => c.RawMessageId));
        Assert.Equal(T0.AddSeconds(1), stats.Snapshot(1).Claims[1].Since);

        stats.Released(11);
        stats.Released(11); // twice is harmless
        Assert.Equal([10L, 9L], stats.Snapshot(1).Claims.Select(c => c.RawMessageId));
        stats.Released(10);
        stats.Released(9);
        Assert.Empty(stats.Snapshot(1).Claims);
    }

    [Fact]
    public void Ring_keeps_the_newest_samples_when_full()
    {
        var stats = new ProcessingStats(new FakeClock(T0));
        for (var i = 1; i <= ProcessingStats.Capacity + 100; i++)
        {
            stats.Record("lock", i);
        }
        var lockStage = stats.Snapshot(1).Lock;
        Assert.Equal(ProcessingStats.Capacity, lockStage.Samples);
        Assert.Equal(ProcessingStats.Capacity + 100, lockStage.MaxMs);
        Assert.Equal(101 + ProcessingStats.Capacity / 2 - 1, lockStage.P50Ms); // samples 101..2100
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
