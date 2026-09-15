using Puluj.Analytics.Text;

namespace Puluj.Analytics.Tests;

public class TextNormalizerTests
{
    [Fact]
    public void Strips_links_mentions_emoji_and_case()
    {
        var text = "🏍 Реактивний БпЛА курсом на Ковель! https://t.me/kpszsu/1 @kpszsu #тривога\n➡ Підписатися\n\n";
        Assert.Equal("реактивний бпла курсом на ковель", TextNormalizer.Canonical(text));
    }

    [Fact]
    public void Same_content_different_decoration_is_equal()
    {
        var a = TextNormalizer.Canonical("Шахеди на Чернігівщині, курсом на Київщину.");
        var b = TextNormalizer.Canonical("🛵 ШАХЕДИ на Чернігівщині — курсом на Київщину 🔴");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Empty_and_null_are_empty()
    {
        Assert.Equal("", TextNormalizer.Canonical(null));
        Assert.Equal("", TextNormalizer.Canonical("   \n 🔴 "));
    }

    [Fact]
    public void Keeps_subscribe_sentence_that_is_content()
    {
        // A long line is content even if it mentions subscribing.
        var line = "підписатися на канал можна за посиланням нижче щоб отримувати сповіщення про тривоги";
        Assert.Equal(line, TextNormalizer.Canonical(line));
    }
}

public class ShinglerTests
{
    [Fact]
    public void Identical_texts_have_jaccard_one()
    {
        var a = Shingler.Shingles("реактивний бпла курсом на ковель");
        var b = Shingler.Shingles("реактивний бпла курсом на ковель");
        Assert.Equal(1, Shingler.Jaccard(a, b));
        Assert.Equal(1, Shingler.Containment(a, b));
    }

    [Fact]
    public void Small_edit_keeps_high_similarity_and_different_texts_low()
    {
        var a = Shingler.Shingles("групи ударних бпла на сумщині в районі боромля лебедин західним курсом");
        var b = Shingler.Shingles("групи ударних бпла на сумщині в районі боромля лебедин та недригайлів західним курсом");
        var c = Shingler.Shingles("пуски керованих авіаційних бомб ворожою тактичною авіацією на південь харківщини");
        Assert.InRange(Shingler.Jaccard(a, b), 0.6, 0.95);
        Assert.True(Shingler.Containment(a, b) > 0.9);
        Assert.True(Shingler.Jaccard(a, c) < 0.1);
    }

    [Fact]
    public void Short_text_gets_one_shingle()
    {
        Assert.Single(Shingler.Shingles("бпл"));
        Assert.Empty(Shingler.Shingles(""));
    }
}

public class MinHasherTests
{
    private static HashSet<ulong> Set(int from, int count) => new(Enumerable.Range(from, count).Select(i => (ulong)i * 2654435761UL));

    [Fact]
    public void Estimate_tracks_exact_jaccard()
    {
        var a = Set(0, 200);
        var b = Set(100, 200); // overlap 100 of 300 → J = 1/3
        var sa = MinHasher.Signature(a)!;
        var sb = MinHasher.Signature(b)!;
        Assert.InRange(MinHasher.Estimate(sa, sb), 0.18, 0.5);
        Assert.Equal(1, MinHasher.Estimate(sa, MinHasher.Signature(Set(0, 200))!));
    }

    [Fact]
    public void Signature_is_deterministic_and_round_trips()
    {
        var sig = MinHasher.Signature(Set(5, 50))!;
        Assert.Equal(sig, MinHasher.Signature(Set(5, 50)));
        Assert.Equal(sig, MinHasher.FromBytes(MinHasher.ToBytes(sig)));
        Assert.Null(MinHasher.Signature([]));
    }

    [Fact]
    public void Bands_match_for_similar_texts_only()
    {
        var a = TextFingerprint.Of("Групи ударних БпЛА на Сумщині в р-ні н.п. Боромля, Лебедин західним курсом на Полтавщину", 40);
        var b = TextFingerprint.Of("🛵 Групи ударних БпЛА на Сумщині в р-ні н.п. Боромля, Лебедин та Недригайлів західним курсом на Полтавщину та Черкащину", 40);
        var c = TextFingerprint.Of("Пуски керованих авіаційних бомб ворожою тактичною авіацією на південь Харківщини.", 40);
        Assert.True(a.Indexed && b.Indexed && c.Indexed);
        Assert.Equal(MinHasher.Bands, a.Bands!.Intersect(TextFingerprint.Of(a.Canonical, 40).Bands!).Count());
        Assert.NotEmpty(a.Bands!.Intersect(b.Bands!));
        Assert.Empty(a.Bands!.Intersect(c.Bands!));
    }

    [Fact]
    public void Band_keys_differ_by_band_index()
    {
        var sig = new uint[MinHasher.HashCount]; // all equal values: without the band index every key would collide
        var keys = MinHasher.BandKeys(sig);
        Assert.Equal(MinHasher.Bands, keys.Distinct().Count());
    }

    [Fact]
    public void Short_text_is_not_indexed()
    {
        var fp = TextFingerprint.Of("Відбій тривоги", 40);
        Assert.False(fp.Indexed);
        Assert.NotEmpty(fp.Shingles);
    }
}
