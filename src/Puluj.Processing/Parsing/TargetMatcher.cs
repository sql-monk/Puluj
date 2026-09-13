using Puluj.Domain.Enums;
using Puluj.Processing.Indexes;
using Puluj.Processing.Text;

namespace Puluj.Processing.Parsing;

/// <summary>Finds target aliases in a segment; overlapping matches resolve to the longest, then highest priority.</summary>
public sealed class TargetMatcher(TaxonomyIndex taxonomy)
{
    private static readonly string[] HedgeStems = ["ймовірн", "імовірн", "можлив", "схож", "припуска", "попередн", "не підтвердж", "вероятн", "возможн", "предположительн", "?"];

    public IReadOnlyList<TargetMention> Match(Segment segment, ParseContext ctx)
    {
        var tokens = segment.Tokens;
        var hedged = IsHedged(segment);
        var found = new List<(int Start, int Len, AliasEntry Alias, int MatchedChars)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            foreach (var alias in taxonomy.Aliases)
            {
                if (alias.SourceId is int sid && sid != ctx.SourceId)
                {
                    continue;
                }
                if (alias.Language != "*" && alias.Language != ctx.Language && alias.Language != "en")
                {
                    continue;
                }
                var n = StemMatch.MatchWords(alias.Words, tokens, i, alias.Exact);
                if (n > 0)
                {
                    found.Add((i, n, alias, alias.Words.Sum(w => w.Length)));
                }
            }
        }

        // Resolve overlaps: longer span wins, then more matched characters, then priority.
        var result = new List<TargetMention>();
        foreach (var m in found.OrderByDescending(f => f.Len).ThenByDescending(f => f.MatchedChars).ThenByDescending(f => f.Alias.Priority))
        {
            if (result.Any(r => Overlaps(r.TokenIndex, r.TokenCount, m.Start, m.Len)))
            {
                continue;
            }
            var targetRef = taxonomy.Resolve(m.Alias.Level, m.Alias.TargetId);
            if (targetRef is null)
            {
                continue;
            }
            result.Add(new TargetMention(targetRef, segment.Slice(m.Start, m.Start + m.Len), m.Alias.ImpliedConfidence, hedged, m.Start, m.Len));
        }
        return result.OrderBy(r => r.TokenIndex).ToList();
    }

    public static bool IsHedged(Segment segment) =>
        HedgeStems.Any(h => h == "?" ? segment.Text.Contains('?') : segment.Tokens.Any(t => t.Text.StartsWith(h, StringComparison.Ordinal)));

    private static bool Overlaps(int aStart, int aLen, int bStart, int bLen) => aStart < bStart + bLen && bStart < aStart + aLen;
}
