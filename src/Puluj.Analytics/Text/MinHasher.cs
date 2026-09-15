using System.Buffers.Binary;

namespace Puluj.Analytics.Text;

/// <summary>
/// MinHash signature (64 universal hash functions over the shingle set) and its LSH bands (16 bands × 4 values):
/// two texts with Jaccard J share a band with probability J⁴, at least one of 16 with 1−(1−J⁴)¹⁶ — 0.64 at J = 0.5,
/// 0.99 at J = 0.7, 0.12 at J = 0.3 (those are then rejected by the exact check). Deterministic: the coefficients
/// come from a fixed seed, so fingerprints stored by one version match the ones computed by the next.
/// </summary>
public static class MinHasher
{
    public const int HashCount = 64;
    public const int Bands = 16;
    public const int RowsPerBand = HashCount / Bands;
    public const int SignatureBytes = HashCount * sizeof(uint);

    private const ulong MersennePrime = (1UL << 61) - 1;
    private static readonly ulong[] A = new ulong[HashCount];
    private static readonly ulong[] B = new ulong[HashCount];

    static MinHasher()
    {
        // xorshift64* from a fixed seed; a must be non-zero modulo p.
        var state = 0x9E3779B97F4A7C15UL;
        ulong Next()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 2685821657736338717UL;
        }
        for (var i = 0; i < HashCount; i++)
        {
            A[i] = Next() % (MersennePrime - 1) + 1;
            B[i] = Next() % MersennePrime;
        }
    }

    /// <summary>Signature of a non-empty shingle set; null for an empty one.</summary>
    public static uint[]? Signature(IReadOnlyCollection<ulong> shingles)
    {
        if (shingles.Count == 0)
        {
            return null;
        }
        var sig = new uint[HashCount];
        Array.Fill(sig, uint.MaxValue);
        foreach (var shingle in shingles)
        {
            var x = shingle % MersennePrime;
            for (var i = 0; i < HashCount; i++)
            {
                var h = (uint)(((UInt128)A[i] * x + B[i]) % MersennePrime);
                if (h < sig[i])
                {
                    sig[i] = h;
                }
            }
        }
        return sig;
    }

    /// <summary>One key per band: the band index is part of the hash so equal values in different bands never collide.</summary>
    public static long[] BandKeys(uint[] signature)
    {
        var keys = new long[Bands];
        Span<char> buf = stackalloc char[(RowsPerBand + 1) * 2];
        for (var b = 0; b < Bands; b++)
        {
            buf[0] = (char)b;
            buf[1] = (char)(b >> 16);
            for (var r = 0; r < RowsPerBand; r++)
            {
                var v = signature[b * RowsPerBand + r];
                buf[2 + r * 2] = (char)v;
                buf[3 + r * 2] = (char)(v >> 16);
            }
            keys[b] = unchecked((long)Shingler.Hash(buf));
        }
        return keys;
    }

    /// <summary>Estimated Jaccard: share of equal positions.</summary>
    public static double Estimate(uint[] a, uint[] b)
    {
        var equal = 0;
        for (var i = 0; i < HashCount; i++)
        {
            if (a[i] == b[i])
            {
                equal++;
            }
        }
        return (double)equal / HashCount;
    }

    public static byte[] ToBytes(uint[] signature)
    {
        var bytes = new byte[SignatureBytes];
        for (var i = 0; i < HashCount; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * sizeof(uint)), signature[i]);
        }
        return bytes;
    }

    public static uint[] FromBytes(ReadOnlySpan<byte> bytes)
    {
        var sig = new uint[HashCount];
        for (var i = 0; i < HashCount; i++)
        {
            sig[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(i * sizeof(uint)));
        }
        return sig;
    }
}
