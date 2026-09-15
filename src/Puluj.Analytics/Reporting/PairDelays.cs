using Puluj.Analytics.Contracts;
using Puluj.Analytics.Persistence;

namespace Puluj.Analytics.Reporting;

/// <summary>One copy of a pair as the pair endpoint reads it: enough to bucket the delays and to repeat the report's aggregates.</summary>
public sealed record PairCopyRow(double DelaySeconds, int Kind, bool IsPrimary, double Jaccard);

/// <summary>
/// Pure part of the pair endpoint: the delay histogram over fixed buckets (under a minute, 1–5, 5–15, 15–60 min,
/// 1–6 h, over 6 h) and the aggregates of one copier → original pair, computed the same way the report does
/// (primary edges only for counts and delays; `CountAll` includes the intermediate originals).
/// </summary>
public static class PairDelays
{
    /// <summary>Upper edges of the buckets in seconds; the last bucket has no upper edge.</summary>
    public static readonly IReadOnlyList<double> Edges = [60, 300, 900, 3600, 21600];

    public static int BucketIndex(double seconds)
    {
        var i = 0;
        while (i < Edges.Count && seconds >= Edges[i])
        {
            i++;
        }
        return i;
    }

    public static IReadOnlyList<DelayBucketDto> Histogram(IEnumerable<double> delaySeconds)
    {
        var counts = new int[Edges.Count + 1];
        foreach (var d in delaySeconds)
        {
            counts[BucketIndex(d)]++;
        }
        return counts.Select((count, i) => new DelayBucketDto(i == 0 ? 0 : Edges[i - 1], i == Edges.Count ? null : Edges[i], count)).ToList();
    }

    /// <summary>Median with linear interpolation between the two middle values, like PostgreSQL's percentile_cont(0.5).</summary>
    public static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    public static PairDetailsDto Summarize(int copierId, int originalId, int days, IReadOnlyList<PairCopyRow> rows, IReadOnlyList<RecentCopyDto> recent)
    {
        var primary = rows.Where(r => r.IsPrimary).ToList();
        var delays = primary.Select(r => r.DelaySeconds).ToList();
        return new PairDetailsDto(copierId, originalId, days,
            primary.Count, rows.Count,
            primary.Count(r => r.Kind == (int)CopyKind.Verbatim),
            primary.Count(r => r.Kind == (int)CopyKind.Forward),
            delays.Count == 0 ? null : Math.Round(delays.Average(), 1),
            Median(delays) is { } m ? Math.Round(m, 1) : null,
            delays.Count == 0 ? null : Math.Round(delays.Min(), 1),
            primary.Count == 0 ? null : Math.Round(primary.Average(r => r.Jaccard), 3),
            Histogram(delays), recent);
    }
}
