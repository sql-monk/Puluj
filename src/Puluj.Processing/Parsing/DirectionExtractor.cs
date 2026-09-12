using Puluj.Domain.Enums;
using Puluj.Processing.Text;

namespace Puluj.Processing.Parsing;

/// <summary>Compass directions from words ("південно-західному", "пд-зх", "SW") — reported direction, never computed.</summary>
public static class DirectionExtractor
{
    private static readonly (string[] Stems, double Deg, string Label)[] Compass =
    [
        (["північно-схід", "пн-сх", "пнсх", "ne", "северо-восто"], 45, "NE"),
        (["північно-захід", "пн-зх", "пнзх", "nw", "северо-запад"], 315, "NW"),
        (["південно-схід", "пд-сх", "пдсх", "se", "юго-восто"], 135, "SE"),
        (["південно-захід", "пд-зх", "пдзх", "sw", "юго-запад"], 225, "SW"),
        (["північ", "півноч", "пн", "n", "север", "north"], 0, "N"),
        (["південь", "південн", "півдн", "пд", "s", "юг", "южн", "south"], 180, "S"),
        (["схід", "сход", "сх", "e", "восто", "east"], 90, "E"),
        (["захід", "заход", "західн", "зх", "w", "запад", "west"], 270, "W"),
    ];

    private static readonly HashSet<string> Cues = ["курсом", "курс", "напрямку", "напрямок", "рухається", "рухаються", "прямує", "прямують", "летить", "летять", "на", "в", "у", "направлении"];

    public static DirectionMention? Extract(Segment segment)
    {
        var tokens = segment.Tokens;
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i].Text;
            foreach (var (stems, deg, label) in Compass)
            {
                foreach (var stem in stems)
                {
                    var isShort = stem.Length <= 2 || stem is "пн-сх" or "пн-зх" or "пд-сх" or "пд-зх";
                    var matches = isShort ? t == stem : t.StartsWith(stem, StringComparison.Ordinal) && t.Length - stem.Length <= 4;
                    if (!matches)
                    {
                        continue;
                    }
                    // Short forms ("n", "пн") need a cue word nearby, otherwise "в" or "n" is noise.
                    if (isShort && !HasCue(tokens, i))
                    {
                        continue;
                    }
                    // Locative "на півночі Київщини" / "на сході області" qualifies a place, it is not a heading.
                    var prevTok = i > 0 ? tokens[i - 1].Text : "";
                    if (t.EndsWith('і') && prevTok is not ("з" or "із" or "зі"))
                    {
                        continue;
                    }
                    var resolvedDeg = deg;
                    var resolvedLabel = label;
                    // Two-word compass points: "північного сходу", "південний захід".
                    if (deg is 0 or 180 && i + 1 < tokens.Count && Secondary(tokens[i + 1].Text) is { } ew)
                    {
                        resolvedDeg = deg == 0 ? (ew == 90 ? 45 : 315) : (ew == 90 ? 135 : 225);
                        resolvedLabel = (deg == 0 ? "N" : "S") + (ew == 90 ? "E" : "W");
                    }
                    // "з півночі" = coming from the north -> heading south.
                    var fromSide = i > 0 && tokens[i - 1].Text is "з" or "із" or "зі" or "from";
                    var degrees = fromSide ? (resolvedDeg + 180) % 360 : resolvedDeg;
                    return new DirectionMention(degrees, DirectionKind.Compass, fromSide ? $"з {resolvedLabel}" : resolvedLabel);
                }
            }
        }
        return null;
    }

    private static double? Secondary(string token)
    {
        foreach (var stem in new[] { "схід", "сход", "восто", "east" })
        {
            if (token.StartsWith(stem, StringComparison.Ordinal) && token.Length - stem.Length <= 4)
            {
                return 90;
            }
        }
        foreach (var stem in new[] { "захід", "заход", "запад", "west" })
        {
            if (token.StartsWith(stem, StringComparison.Ordinal) && token.Length - stem.Length <= 4)
            {
                return 270;
            }
        }
        return null;
    }

    private static bool HasCue(IReadOnlyList<Token> tokens, int i)
    {
        for (var k = Math.Max(0, i - 3); k < i; k++)
        {
            if (Cues.Contains(tokens[k].Text) || tokens[k].Text.EndsWith(':'))
            {
                return true;
            }
        }
        return false;
    }
}
