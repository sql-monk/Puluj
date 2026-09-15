namespace Puluj.Analytics.Text;

/// <summary>
/// Character n-grams of the canonical text, hashed with FNV-1a 64. Character shingles rather than word ones: the
/// median post is 58 characters, word bigrams would give half a dozen elements and a Jaccard that swings on one
/// changed word; 4-grams survive inflection and small edits.
/// </summary>
public static class Shingler
{
    public const int DefaultSize = 4;

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static HashSet<ulong> Shingles(string canonical, int size = DefaultSize)
    {
        var set = new HashSet<ulong>();
        if (canonical.Length < size)
        {
            if (canonical.Length > 0)
            {
                set.Add(Hash(canonical.AsSpan()));
            }
            return set;
        }
        for (var i = 0; i + size <= canonical.Length; i++)
        {
            set.Add(Hash(canonical.AsSpan(i, size)));
        }
        return set;
    }

    public static ulong Hash(ReadOnlySpan<char> chars)
    {
        var h = FnvOffset;
        foreach (var c in chars)
        {
            h = (h ^ (byte)c) * FnvPrime;
            h = (h ^ (byte)(c >> 8)) * FnvPrime;
        }
        return h;
    }

    /// <summary>|A∩B| / |A∪B|; 0 for two empty sets.</summary>
    public static double Jaccard(HashSet<ulong> a, HashSet<ulong> b)
    {
        var inter = Intersection(a, b);
        var union = a.Count + b.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }

    /// <summary>|A∩B| / min(|A|,|B|): 1 when the smaller text is entirely inside the larger one.</summary>
    public static double Containment(HashSet<ulong> a, HashSet<ulong> b)
    {
        var min = Math.Min(a.Count, b.Count);
        return min == 0 ? 0 : (double)Intersection(a, b) / min;
    }

    private static int Intersection(HashSet<ulong> a, HashSet<ulong> b)
    {
        var (small, large) = a.Count <= b.Count ? (a, b) : (b, a);
        var n = 0;
        foreach (var x in small)
        {
            if (large.Contains(x))
            {
                n++;
            }
        }
        return n;
    }
}
