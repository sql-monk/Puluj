using Microsoft.Extensions.Options;
using Puluj.Analytics.Analysis;
using Puluj.Analytics.Persistence;

namespace Puluj.Analytics.Tests;

public class CopyDetectorTests
{
    private static readonly CopyDetector Detector = new(Options.Create(new AnalyticsOptions()));
    private static readonly DateTimeOffset T0 = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    private static MessageFingerprint Message(long id, int source, string key, DateTimeOffset at, int? forwardedFrom = null) =>
        new() { RawMessageId = id, SourceId = source, PostKey = key, PublishedAt = at, ForwardedSourceId = forwardedFrom };

    private static Candidate Cand(long id, int source, string key, DateTimeOffset at, int? forwardedFrom = null) =>
        new(id, source, key, at.UtcDateTime, [], forwardedFrom);

    [Fact]
    public void Post_key_strips_edit_suffix()
    {
        Assert.Equal(("87675", true), new RawRow(1, 7, "87675:e1789433701", default, default, null, null, null).Post());
        Assert.Equal(("87675", false), new RawRow(1, 7, "87675", default, default, null, null, null).Post());
        Assert.Equal(("12:start", false), new RawRow(1, 1, "12:start", default, default, null, null, null).Post());
    }

    [Fact]
    public void Forwarded_channel_id_is_parsed_from_channel_form_only()
    {
        Assert.Equal(1463721328, RawMessageReader.ForwardedChannelId("channel 1463721328"));
        Assert.Null(RawMessageReader.ForwardedChannelId("Приймальня єРадару"));
        Assert.Null(RawMessageReader.ForwardedChannelId(null));
    }

    [Fact]
    public void Earlier_post_is_the_original_whichever_side_was_indexed_first()
    {
        var newer = Message(20, 2, "20", T0.AddMinutes(5));
        var older = Cand(10, 3, "10", T0);
        var pair = Detector.Pair(newer, new Match(older, 0.95, 1), T0.AddHours(1));
        Assert.Equal((3, "10", 2, "20"), (pair.OriginalSourceId, pair.OriginalPostKey, pair.CopySourceId, pair.CopyPostKey));
        Assert.Equal(300, pair.DelaySeconds);
        Assert.Equal(CopyKind.Verbatim, pair.Kind);

        // The history load brings the original in after the copy was indexed: the new message becomes the original.
        var late = Message(30, 3, "30", T0.AddMinutes(-10));
        var indexedCopy = Cand(20, 2, "20", T0.AddMinutes(5));
        var reversed = Detector.Pair(late, new Match(indexedCopy, 0.7, 0.8), T0.AddHours(1));
        Assert.Equal((3, "30", 2, "20"), (reversed.OriginalSourceId, reversed.OriginalPostKey, reversed.CopySourceId, reversed.CopyPostKey));
        Assert.Equal(900, reversed.DelaySeconds);
        Assert.Equal(CopyKind.Near, reversed.Kind);
    }

    [Fact]
    public void Same_instant_orders_by_id()
    {
        var a = Message(20, 2, "20", T0);
        var b = Cand(10, 3, "10", T0);
        var pair = Detector.Pair(a, new Match(b, 1, 1), T0);
        Assert.Equal(3, pair.OriginalSourceId);
        Assert.Equal(0, pair.DelaySeconds);
    }

    [Fact]
    public void Forward_from_the_original_source_is_a_forward()
    {
        var copy = Message(20, 2, "20", T0.AddMinutes(1), forwardedFrom: 3);
        var original = Cand(10, 3, "10", T0);
        Assert.Equal(CopyKind.Forward, Detector.Pair(copy, new Match(original, 0.5, 0.6), T0).Kind);
        // Forwarded from some other source: not a forward of this original.
        var other = Message(21, 2, "21", T0.AddMinutes(1), forwardedFrom: 6);
        Assert.Equal(CopyKind.Near, Detector.Pair(other, new Match(original, 0.5, 0.6), T0).Kind);
    }

    [Fact]
    public void Containment_only_counts_for_long_texts()
    {
        Assert.True(Detector.Accepts(0.75, 0.5, 40));
        Assert.False(Detector.Accepts(0.62, 0.87, 60)); // "Ракета з акваторії Чорного моря у напрямку Одещини" vs "… Миколаївщини"
        Assert.False(Detector.Accepts(0.3, 0.9, 40)); // "Київщина: БпЛА курсом на Обухів" inside a list post
        Assert.True(Detector.Accepts(0.3, 0.9, 120));
        Assert.False(Detector.Accepts(0.3, 0.8, 120));
    }

    [Fact]
    public void Candidates_rank_by_shared_bands_then_by_time_order()
    {
        var mine = new long[] { 1, 2, 3, 4 };
        var far = new Candidate(1, 3, "1", T0.UtcDateTime, [1, 2, 3, 9], null);
        var near = new Candidate(2, 3, "2", T0.UtcDateTime, [1, 8, 9, 10], null);
        var near2 = new Candidate(3, 3, "3", T0.UtcDateTime, [1, 7, 9, 10], null);
        var ranked = CopyDetector.RankBySharedBands([near, near2, far], mine).ToList();
        Assert.Equal([far, near, near2], ranked);
    }
}
