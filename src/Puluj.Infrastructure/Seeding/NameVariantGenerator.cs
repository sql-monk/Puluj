using System.Text;

namespace Puluj.Infrastructure.Seeding;

/// <summary>
/// Produces lower-cased matching stems for a settlement name. The parser matches each stem word as a token prefix,
/// so "бровар" covers "Бровари/Броварів/Броварах". Rules are deliberately simple; overrides live in regions.json / corpus tests.
/// </summary>
public static class NameVariantGenerator
{
    private static readonly char[] Vowels = ['а', 'е', 'є', 'и', 'і', 'ї', 'о', 'у', 'ю', 'я', 'ь'];

    public static IReadOnlyList<string> ForSettlement(string name, IEnumerable<string>? extraNames = null)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in new[] { name }.Concat(extraNames ?? []))
        {
            if (string.IsNullOrWhiteSpace(n))
            {
                continue;
            }
            foreach (var v in Stems(n))
            {
                set.Add(v);
            }
        }
        return set.ToList();
    }

    private static IEnumerable<string> Stems(string raw)
    {
        var name = Normalize(raw);
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Multi-word names: stem every word ("біла церква" -> "біл церкв"); shorter stems are safe there because
        // the parser requires all words to match.
        var minStem = words.Length > 1 ? 3 : 4;
        var stemmed = string.Join(' ', words.Select(w => StemWord(w, minStem)));
        yield return stemmed;
        if (stemmed != name)
        {
            yield return name;
        }

        // Ukrainian і/о and ї/є alternation in closed syllables: Київ->Києв-, Львів->Львов-, Харків->Харков-, Миколаїв->Миколаєв-.
        if (words.Length == 1)
        {
            var w = words[0];
            if (w == "київ")
            {
                yield return "києв";
            }
            else if (w.EndsWith("їв", StringComparison.Ordinal))
            {
                yield return w[..^2] + "єв";
            }
            else if (w.EndsWith("ів", StringComparison.Ordinal) && w.Length > 4)
            {
                yield return w[..^2] + "ов";
            }
            else if (w.EndsWith("іль", StringComparison.Ordinal))
            {
                yield return w[..^3] + "ол"; // Тернопіль -> Тернопол-
            }
            else if (w.EndsWith("ір", StringComparison.Ordinal) && w.Length > 4)
            {
                yield return w[..^2] + "ор"; // Яворів handled above; Самбір -> Самбор-
            }
        }
    }

    /// <summary>Drops a trailing vowel/soft sign so declined forms still match; keeps short names intact.</summary>
    public static string StemWord(string w, int minStem = 4)
    {
        if (w.Length <= minStem)
        {
            return w;
        }
        // Adjective-like endings (Хмельницький, Кам'янка-Бузька) -> cut to "-ськ"/"-цьк".
        foreach (var ending in new[] { "ський", "ська", "ське", "цький", "цька", "цьке" })
        {
            if (w.EndsWith(ending, StringComparison.Ordinal))
            {
                return w[..^(ending.Length - 3)];
            }
        }
        if (w.EndsWith("ий", StringComparison.Ordinal) || w.EndsWith("ій", StringComparison.Ordinal))
        {
            return w[..^2];
        }
        if (Vowels.Contains(w[^1]))
        {
            var stem = w[..^1];
            return stem.Length >= minStem ? stem : w;
        }
        return w;
    }

    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            sb.Append(ch switch
            {
                'ё' => 'е',
                'ъ' => '\'',
                '’' or '`' or 'ʼ' => '\'',
                '\u2011' or '‐' or '–' or '—' => '-',
                _ => ch,
            });
        }
        return string.Join(' ', sb.ToString().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }
}
