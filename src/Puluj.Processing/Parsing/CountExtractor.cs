using Puluj.Processing.Text;

namespace Puluj.Processing.Parsing;

/// <summary>Object count near a target mention: "5 БпЛА", "5х шахедів", "група", "декілька", "два".</summary>
public static class CountExtractor
{
    private static readonly Dictionary<string, int> Words = new()
    {
        ["один"] = 1, ["одна"] = 1, ["пара"] = 2, ["два"] = 2, ["дві"] = 2, ["три"] = 3, ["чотири"] = 4,
        ["п'ять"] = 5, ["шість"] = 6, ["сім"] = 7, ["вісім"] = 8, ["дев'ять"] = 9, ["десять"] = 10,
        ["две"] = 2, ["пять"] = 5, ["шесть"] = 6, ["семь"] = 7, ["восемь"] = 8, ["девять"] = 9,
    };

    private static readonly (string Stem, int Value)[] ApproxStems =
    [
        ("одиничн", 1), ("одиночн", 1), ("груп", 3), ("декільк", 3), ("кільк", 3), ("нескольк", 3), ("масован", 10), ("рій", 5),
    ];

    private static readonly HashSet<string> ApproxMarkers = ["до", "близько", "понад", "орієнтовно", "~", "около"];

    public static (int? Count, bool Approximate) Extract(Segment segment, int targetTokenIndex)
    {
        var tokens = segment.Tokens;
        for (var i = Math.Max(0, targetTokenIndex - 3); i < targetTokenIndex; i++)
        {
            var text = tokens[i].Text;
            var t = text.TrimEnd('х', 'x');
            if (int.TryParse(t, out var n) && n is > 0 and < 1000)
            {
                var approx = i > 0 && ApproxMarkers.Contains(tokens[i - 1].Text);
                return (n, approx);
            }
            if (Words.TryGetValue(text, out var w))
            {
                return (w, false);
            }
            foreach (var (stem, value) in ApproxStems)
            {
                if (text.StartsWith(stem, StringComparison.Ordinal))
                {
                    return (value, true);
                }
            }
        }
        // "шахеди х5" / "БпЛА (5 од.)" after the mention
        for (var i = targetTokenIndex + 1; i < Math.Min(tokens.Count, targetTokenIndex + 4); i++)
        {
            var t = tokens[i].Text.TrimStart('х', 'x');
            var unitFollows = i + 1 < tokens.Count && tokens[i + 1].Text is "од" or "шт" or "одиниць" or "одиниці";
            if (int.TryParse(t, out var n) && n is > 0 and < 1000 && (t != tokens[i].Text || unitFollows))
            {
                return (n, false);
            }
        }
        return (null, false);
    }
}
