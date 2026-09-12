using Puluj.Domain.Enums;
using Puluj.Processing.Indexes;
using Puluj.Processing.Text;

namespace Puluj.Processing.Parsing;

/// <summary>
/// Finds gazetteer places in a segment and assigns a role (origin / destination / current) from the preposition
/// in front of the name and the case ending of the name itself.
/// </summary>
public sealed class PlaceMatcher(GazetteerIndex gazetteer)
{
    private static readonly HashSet<string> OriginPreps = ["з", "із", "зі", "від", "из", "со", "от"];
    private static readonly HashSet<string> CurrentPreps = ["над", "поблизу", "біля", "неподалік", "повз", "районі", "околицях", "околиці", "територією", "території", "межах", "небі", "возле", "около", "вблизи"];
    private static readonly HashSet<string> TransitPreps = ["через", "повз"];
    private static readonly string[][] DestinationPhrases =
    [
        ["курсом", "на"], ["курс", "на"], ["курсом", "в"], ["курсом", "у"], ["в", "напрямку"], ["у", "напрямку"], ["напрямку"],
        ["в", "бік"], ["у", "бік"], ["в", "сторону"], ["прямує", "на"], ["прямують", "на"], ["рухається", "на"], ["рухаються", "на"],
        ["летить", "на"], ["летять", "на"], ["до"], ["в", "напрямку", "на"], ["у", "напрямку", "на"], ["тримає", "курс", "на"], ["тримають", "курс", "на"],
        ["курсом"], ["курс"],
    ];
    private static readonly string[] LocativeEndings = ["і", "ї", "ах", "ях", "ій", "ому", "ім", "ині", "щині", "ї"];
    private static readonly string[] AccusativeEndings = ["у", "ю", "щину", "ину"];

    public IReadOnlyList<PlaceMention> Match(Segment segment, ParseContext ctx, IReadOnlySet<int> contextRegions, IReadOnlySet<(int, int)> reservedSpans)
    {
        var tokens = segment.Tokens;
        var found = new List<(int Start, int Len, PlaceEntry Place, int Score)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (reservedSpans.Any(s => i >= s.Item1 && i < s.Item1 + s.Item2))
            {
                continue; // token is part of a threat alias ("Крим" inside nothing, but e.g. "Іскандер" is never a place)
            }
            if (IsUnitWord(tokens[i].Text))
            {
                continue; // "район" is not the village Ray ("рай"), "область" is not a place
            }
            foreach (var v in gazetteer.Candidates(tokens[i].Text))
            {
                var n = StemMatch.MatchWords(v.Words, tokens, i, exact: false);
                if (n == 0 && v.Words.Length == 1 && IsAdjectiveForm(v.Words[0], tokens, i))
                {
                    n = 1; // "Броварському районі", "Кременчуцький р-н"
                }
                if (n == 0)
                {
                    continue;
                }
                // Single short stems (e.g. "сум") are risky; require the token to be close to the stem length.
                if (v.Words.Length == 1 && v.Words[0].Length <= 4 && tokens[i].Text.Length - v.Words[0].Length > 2 && !IsAdjectiveForm(v.Words[0], tokens, i))
                {
                    continue; // ("Бучанський район" is still Буча: the adjective form is checked explicitly)
                }
                // "Дніпровський", "Подільський", "Поділ" exist far beyond Kyiv: a city district counts only when the
                // message, an earlier segment, or the source itself is about that city.
                if (CityOf(v.Place) is { } city && !CityContext(city, tokens, contextRegions, ctx))
                {
                    continue;
                }
                found.Add((i, n, v.Place, v.Words.Sum(w => w.Length) * 10 + LevelBonus(v.Place)));
            }
        }

        var result = new List<PlaceMention>();
        foreach (var group in found.GroupBy(f => (f.Start, f.Len)).OrderByDescending(g => g.Key.Len).ThenByDescending(g => g.Max(f => f.Score)))
        {
            if (result.Any(r => Overlaps(r.TokenIndex, r.TokenCount, group.Key.Start, group.Key.Len)))
            {
                continue;
            }
            var best = group
                .OrderByDescending(f => f.Score / 10) // matched characters first
                .ThenByDescending(f => InContext(f.Place, contextRegions, ctx) ? 1 : 0)
                .ThenByDescending(f => f.Place.Level is PlaceLevel.Region or PlaceLevel.NamedArea ? 1 : 0)
                .ThenByDescending(f => f.Place.Population)
                .First();
            var nominative = group.Any(f => IsNominative(f.Place, tokens, best.Start, best.Len));
            var role = RoleFor(tokens, best.Start, best.Len, ctx.Language, nominative);
            result.Add(new PlaceMention(best.Place, role, segment.Slice(best.Start, best.Start + best.Len), best.Start, best.Len, best.Score, Quadrant(tokens, best.Start)));
        }
        return result.OrderBy(r => r.TokenIndex).ToList();
    }

    private static readonly (string Stem, double Deg)[] QuadrantStems =
    [
        ("північно-схід", 45), ("північно-захід", 315), ("південно-схід", 135), ("південно-захід", 225),
        ("півноч", 0), ("північн", 0), ("півдн", 180), ("південн", 180), ("схід", 90), ("сход", 90), ("східн", 90), ("захід", 270), ("заход", 270), ("західн", 270),
        ("центр", -1),
    ];

    /// <summary>"на півночі Київщини", "у східній частині області": a compass word right before the place (optionally + "частині").</summary>
    private static double? Quadrant(IReadOnlyList<Token> tokens, int start)
    {
        for (var i = Math.Max(0, start - 3); i < start; i++)
        {
            var t = tokens[i].Text;
            var between = tokens.Skip(i + 1).Take(start - i - 1).All(x => x.Text is "частині" or "частина" or "частин" or "області" or "обл");
            if (!between)
            {
                continue;
            }
            foreach (var (stem, deg) in QuadrantStems)
            {
                if (t.StartsWith(stem, StringComparison.Ordinal) && t.Length - stem.Length <= 4)
                {
                    return deg < 0 ? null : deg;
                }
            }
        }
        return null;
    }

    private static readonly string[] UnitStems = ["район", "р-н", "област", "обл", "громад", "місто", "місті", "село", "селищ", "смт", "вулиц", "масив"];

    private static bool IsUnitWord(string token) => UnitStems.Any(u => token.StartsWith(u, StringComparison.Ordinal) && token.Length - u.Length <= 3);

    /// <summary>The city-region (Kyiv) a district belongs to, or null for any other place.</summary>
    private PlaceEntry? CityOf(PlaceEntry place) =>
        place.Level == PlaceLevel.District && place.ParentId is int pid && gazetteer.Get(pid) is { Level: PlaceLevel.City, ParentId: null } city ? city : null;

    private bool CityContext(PlaceEntry city, IReadOnlyList<Token> tokens, IReadOnlySet<int> contextRegions, ParseContext ctx)
    {
        if (contextRegions.Contains(city.PlaceId) || ctx.HomeRegionPlaceId == city.PlaceId)
        {
            return true;
        }
        for (var i = 0; i < tokens.Count; i++)
        {
            if (gazetteer.Candidates(tokens[i].Text).Any(v => v.Place.PlaceId == city.PlaceId && StemMatch.MatchWords(v.Words, tokens, i, exact: false) > 0))
            {
                return true;
            }
        }
        return false;
    }

    private bool InContext(PlaceEntry place, IReadOnlySet<int> contextRegions, ParseContext ctx)
    {
        var region = gazetteer.RegionOf(place);
        if (region is null)
        {
            return false;
        }
        return contextRegions.Contains(region.PlaceId) || ctx.HomeRegionPlaceId == region.PlaceId;
    }

    /// <summary>"бровар" + "ському" followed by "район"/"р-н"/"громад": adjective derived from the settlement name.</summary>
    private static bool IsAdjectiveForm(string stem, IReadOnlyList<Token> tokens, int i)
    {
        var t = tokens[i].Text;
        if (!t.StartsWith(stem, StringComparison.Ordinal) || i + 1 >= tokens.Count)
        {
            return false;
        }
        var suffix = t[stem.Length..];
        var next = tokens[i + 1].Text;
        var nextIsUnit = next.StartsWith("район", StringComparison.Ordinal) || next is "р-н" || next.StartsWith("громад", StringComparison.Ordinal) || next.StartsWith("тг", StringComparison.Ordinal);
        return nextIsUnit && suffix.Length is >= 3 and <= 9 && (suffix.Contains("ськ") || suffix.Contains("цьк") || suffix.Contains("зьк"));
    }

    /// <summary>True when the matched tokens equal the place name itself (no case ending), e.g. "Кременчук" but not "Кременчуці".</summary>
    private static bool IsNominative(PlaceEntry place, IReadOnlyList<Token> tokens, int start, int len)
    {
        var text = string.Join(' ', tokens.Skip(start).Take(len).Select(t => t.Text));
        return text == Infrastructure.Seeding.NameVariantGenerator.Normalize(place.Name);
    }

    private static int LevelBonus(PlaceEntry p) => p.Level switch
    {
        PlaceLevel.Region => 5,
        PlaceLevel.NamedArea => 5,
        PlaceLevel.City => 4,
        PlaceLevel.District => 3,
        PlaceLevel.Town => 2,
        _ => 1,
    };

    private static PlaceRole RoleFor(IReadOnlyList<Token> tokens, int start, int len, string language, bool nominative)
    {
        var prev = start > 0 ? tokens[start - 1].Text : "";
        var prev2 = start > 1 ? tokens[start - 2].Text : "";
        var prev3 = start > 2 ? tokens[start - 3].Text : "";
        var name = tokens[start + len - 1].Text;

        foreach (var phrase in DestinationPhrases)
        {
            var window = phrase.Length switch { 1 => new[] { prev }, 2 => [prev2, prev], _ => [prev3, prev2, prev] };
            if (window.SequenceEqual(phrase))
            {
                return PlaceRole.Destination;
            }
        }
        if (OriginPreps.Contains(prev) || (prev == "с" && language == "ru") || (prev == "боку" && prev2 == "з"))
        {
            return PlaceRole.Origin;
        }
        if (TransitPreps.Contains(prev))
        {
            return PlaceRole.Transit;
        }
        if (CurrentPreps.Contains(prev) || CurrentPreps.Contains(prev2))
        {
            return PlaceRole.Current;
        }
        if (prev is "на" or "в" or "у")
        {
            if (LocativeEndings.Any(e => name.EndsWith(e, StringComparison.Ordinal)))
            {
                return PlaceRole.Current; // "на Київщині", "у Харкові"
            }
            if (AccusativeEndings.Any(e => name.EndsWith(e, StringComparison.Ordinal)) || nominative)
            {
                return PlaceRole.Destination; // "на Київщину", "на Кременчук" (accusative = nominative for masculine)
            }
        }
        return PlaceRole.Current;
    }

    private static bool Overlaps(int aStart, int aLen, int bStart, int bLen) => aStart < bStart + bLen && bStart < aStart + aLen;
}
