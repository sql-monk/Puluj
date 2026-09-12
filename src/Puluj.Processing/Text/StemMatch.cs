namespace Puluj.Processing.Text;

/// <summary>Prefix ("stem") matching used by both alias and gazetteer lookups.</summary>
public static class StemMatch
{
    /// <summary>Longest inflectional suffix a token may carry beyond the stem.</summary>
    public const int MaxSuffix = 4;

    public static bool Matches(string stem, string token, bool exact)
    {
        if (exact)
        {
            if (token == stem)
            {
                return true;
            }
            // Designations keep short letter suffixes: "ту-95" matches "ту-95мс", "міг-31" matches "міг-31к".
            return stem.Any(char.IsDigit) && token.Length - stem.Length is > 0 and <= 2
                   && token.StartsWith(stem, StringComparison.Ordinal) && token[stem.Length..].All(char.IsLetter);
        }
        if (!token.StartsWith(stem, StringComparison.Ordinal))
        {
            return false;
        }
        var extra = token.Length - stem.Length;
        if (extra == 0)
        {
            return true;
        }
        if (extra > MaxSuffix)
        {
            return false;
        }
        // Only letters (or a hyphen + letters, "іскандер-м") may follow a stem: "х-101" must not match "х-1010".
        for (var i = stem.Length; i < token.Length; i++)
        {
            if (!char.IsLetter(token[i]) && !(token[i] == '-' && i == stem.Length && i + 1 < token.Length))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Tries to match a multi-word stem sequence starting at token index i. Returns matched token count or 0.</summary>
    public static int MatchWords(string[] stems, IReadOnlyList<Token> tokens, int i, bool exact)
    {
        if (i + stems.Length > tokens.Count)
        {
            return 0;
        }
        for (var k = 0; k < stems.Length; k++)
        {
            if (!Matches(stems[k], tokens[i + k].Text, exact))
            {
                return 0;
            }
        }
        return stems.Length;
    }
}
