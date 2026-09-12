using System.Text;

namespace Puluj.Processing.Text;

/// <summary>Splits normalized text into word tokens. Hyphens, apostrophes and slashes inside a word are kept ("х-101/555", "кам'янець").</summary>
public static class Tokenizer
{
    public static IReadOnlyList<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var sb = new StringBuilder();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var ch = i < text.Length ? text[i] : ' ';
            var inner = i < text.Length && i + 1 < text.Length && sb.Length > 0
                        && (ch is '-' or '\'' or '/' or '.') && IsWordChar(text[i + 1]);
            if (IsWordChar(ch) || inner)
            {
                if (start < 0)
                {
                    start = i;
                }
                sb.Append(ch);
            }
            else if (sb.Length > 0)
            {
                tokens.Add(new Token(sb.ToString(), start, i));
                sb.Clear();
                start = -1;
            }
        }
        return tokens;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);
}
