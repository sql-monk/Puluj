namespace Puluj.Analytics.Text;

/// <summary>Everything the index keeps about one text: canonical form, its shingles, and (when long enough) the MinHash signature with its LSH band keys.</summary>
public sealed record TextFingerprint(string Canonical, HashSet<ulong> Shingles, uint[]? Signature, long[]? Bands)
{
    public bool Indexed => Signature is not null;

    /// <summary>Texts shorter than <paramref name="minLength"/> canonical characters get shingles (for exact comparison) but no signature (they are never candidates).</summary>
    public static TextFingerprint Of(string? raw, int minLength)
    {
        var canonical = TextNormalizer.Canonical(raw);
        var shingles = Shingler.Shingles(canonical);
        if (canonical.Length < minLength)
        {
            return new TextFingerprint(canonical, shingles, null, null);
        }
        var signature = MinHasher.Signature(shingles);
        return new TextFingerprint(canonical, shingles, signature, signature is null ? null : MinHasher.BandKeys(signature));
    }
}
