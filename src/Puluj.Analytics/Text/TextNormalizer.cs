using System.Text;
using System.Text.RegularExpressions;

namespace Puluj.Analytics.Text;

/// <summary>
/// Turns a post into its canonical text for comparison: NFC, lower case, no links / mentions / hashtags, no emoji or
/// punctuation, one space between words, lines that carried nothing but decoration (channel signatures, emoji rows)
/// dropped. Two posts that differ only in these are the same content.
/// </summary>
public static partial class TextNormalizer
{
    [GeneratedRegex(@"(https?://|www\.|t\.me/)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Links();

    [GeneratedRegex(@"[@#]\w+")]
    private static partial Regex MentionsAndTags();

    public static string Canonical(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }
        var text = raw.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var sb = new StringBuilder(text.Length);
        foreach (var line in text.Split('\n'))
        {
            var cleaned = MentionsAndTags().Replace(Links().Replace(line, " "), " ");
            var start = sb.Length;
            var pendingSpace = false;
            foreach (var rune in cleaned.EnumerateRunes())
            {
                if (Rune.IsLetterOrDigit(rune))
                {
                    if (pendingSpace && sb.Length > start)
                    {
                        sb.Append(' ');
                    }
                    pendingSpace = false;
                    sb.Append(rune.ToString());
                }
                else
                {
                    pendingSpace = true;
                }
            }
            if (sb.Length > start && IsSignature(sb, start))
            {
                sb.Length = start; // "➡ Підписатися", "Subscribe": the channel's footer, not content
            }
            if (sb.Length > start)
            {
                sb.Append(' ');
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>A short line built around "підписатися" / "subscribe": a channel footer.</summary>
    private static bool IsSignature(StringBuilder sb, int start)
    {
        var line = sb.ToString(start, sb.Length - start);
        if (line.Length > 40)
        {
            return false;
        }
        return line.Contains("підпис", StringComparison.Ordinal) || line.Contains("subscribe", StringComparison.Ordinal) || line.Contains("подпис", StringComparison.Ordinal);
    }
}
