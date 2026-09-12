using Puluj.Domain.Enums;
using Puluj.Processing.Text;

namespace Puluj.Processing.Parsing;

/// <summary>"жовтий рівень" / "червоний рівень" (Kyiv oblast administration and similar): the stated alert level.</summary>
public static class AlertLevelExtractor
{
    public static AirAlertLevel Extract(Segment segment)
    {
        var tokens = segment.Tokens;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!tokens[i].Text.StartsWith("рівн", StringComparison.Ordinal) && !tokens[i].Text.StartsWith("рівен", StringComparison.Ordinal))
            {
                continue;
            }
            // "жовтий рівень", "рівень: жовтий", "(жовтий рівень)".
            for (var j = Math.Max(0, i - 2); j <= Math.Min(tokens.Count - 1, i + 2); j++)
            {
                var t = tokens[j].Text;
                if (t.StartsWith("жовт", StringComparison.Ordinal))
                {
                    return AirAlertLevel.Yellow;
                }
                if (t.StartsWith("червон", StringComparison.Ordinal))
                {
                    return AirAlertLevel.Red;
                }
            }
        }
        return AirAlertLevel.Unknown;
    }
}
