using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Puluj.Analytics.Persistence;
using Puluj.Analytics.Text;

namespace Puluj.Analytics.Analysis;

/// <summary>A row of the `messages` index that shares at least one LSH band with the message under test.</summary>
public sealed record Candidate(long RawMessageId, int SourceId, string PostKey, DateTime PublishedAt, long[] Bands, int? ForwardedSourceId);

/// <summary>What the exact check decided about one candidate.</summary>
public sealed record Match(Candidate Other, double Jaccard, double Containment);

/// <summary>
/// Finds the posts of other sources that share the content of a message: LSH candidates within the pair window (both
/// directions in time — a late-arriving history row can be the original of a post indexed earlier), the exact
/// Jaccard / containment on recomputed shingles, then the direction (earlier published = original) and the kind
/// (forward / verbatim / near). Pure decisions are static so the tests need no database.
/// </summary>
public sealed class CopyDetector(IOptions<AnalyticsOptions> options)
{
    private AnalyticsOptions O => options.Value;

    public async Task<List<Match>> FindAsync(AnalyticsDbContext db, MessageFingerprint message, TextFingerprint fingerprint, CancellationToken ct)
    {
        if (fingerprint.Bands is null)
        {
            return [];
        }
        var t = message.PublishedAt.UtcDateTime;
        var from = t - O.PairWindow;
        var to = t + O.PairWindow;
        var candidates = await db.Database.SqlQuery<Candidate>($"""
            SELECT raw_message_id, source_id, post_key, published_at, bands, forwarded_source_id
            FROM analytics.messages
            WHERE bands && {fingerprint.Bands} AND source_id <> {message.SourceId}
              AND published_at BETWEEN {from} AND {to}
            ORDER BY abs(extract(epoch FROM (published_at - {t}))), raw_message_id
            LIMIT {O.CandidateScan}
            """).ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return [];
        }
        var top = RankBySharedBands(candidates, fingerprint.Bands).Take(O.CandidateLimit).ToList();
        var texts = (await RawMessageReader.TextsAsync(db, top.Select(c => c.RawMessageId).ToArray(), ct)).ToDictionary(x => x.RawMessageId, x => x.Text);
        var matches = new List<Match>();
        foreach (var c in top)
        {
            if (!texts.TryGetValue(c.RawMessageId, out var text))
            {
                continue;
            }
            var canonical = TextNormalizer.Canonical(text);
            var other = Shingler.Shingles(canonical);
            var jaccard = Shingler.Jaccard(fingerprint.Shingles, other);
            var containment = Shingler.Containment(fingerprint.Shingles, other);
            if (Accepts(jaccard, containment, Math.Min(fingerprint.Canonical.Length, canonical.Length)))
            {
                matches.Add(new Match(c, jaccard, containment));
            }
        }
        return matches;
    }

    /// <summary>Jaccard above the threshold, or — for texts long enough not to be a template — the shorter one contained in the longer.</summary>
    public bool Accepts(double jaccard, double containment, int shorterLength) =>
        jaccard >= O.JaccardThreshold || (containment >= O.ContainmentThreshold && shorterLength >= O.ContainmentMinLength);

    /// <summary>Most bands in common first (ties: nearest in time, which is the order the query returned).</summary>
    public static IEnumerable<Candidate> RankBySharedBands(IReadOnlyList<Candidate> candidates, long[] bands)
    {
        var mine = new HashSet<long>(bands);
        return candidates
            .Select((c, i) => (c, shared: c.Bands.Count(mine.Contains), i))
            .OrderByDescending(x => x.shared).ThenBy(x => x.i)
            .Select(x => x.c);
    }

    /// <summary>The pair as it is stored: the earlier post is the original. Same instant — the smaller id was stored first.</summary>
    public MessageCopy Pair(MessageFingerprint message, Match match, DateTimeOffset now)
    {
        var other = match.Other;
        var otherPublished = new DateTimeOffset(DateTime.SpecifyKind(other.PublishedAt, DateTimeKind.Utc));
        var messageIsOriginal = message.PublishedAt < otherPublished || (message.PublishedAt == otherPublished && message.RawMessageId < other.RawMessageId);
        var (copySource, copyKey, copyId, copyAt, copyForward) = messageIsOriginal
            ? (other.SourceId, other.PostKey, other.RawMessageId, otherPublished, other.ForwardedSourceId)
            : (message.SourceId, message.PostKey, message.RawMessageId, message.PublishedAt, message.ForwardedSourceId);
        var (origSource, origKey, origId, origAt) = messageIsOriginal
            ? (message.SourceId, message.PostKey, message.RawMessageId, message.PublishedAt)
            : (other.SourceId, other.PostKey, other.RawMessageId, otherPublished);
        return new MessageCopy
        {
            CopySourceId = copySource,
            CopyPostKey = copyKey,
            OriginalSourceId = origSource,
            OriginalPostKey = origKey,
            CopyRawMessageId = copyId,
            OriginalRawMessageId = origId,
            CopyPublishedAt = copyAt,
            OriginalPublishedAt = origAt,
            DelaySeconds = (copyAt - origAt).TotalSeconds,
            Jaccard = (float)match.Jaccard,
            Containment = (float)match.Containment,
            Kind = KindOf(match.Jaccard, copyForward, origSource),
            IsPrimary = false,
            FoundAt = now,
        };
    }

    public CopyKind KindOf(double jaccard, int? copyForwardedSourceId, int originalSourceId) =>
        copyForwardedSourceId == originalSourceId ? CopyKind.Forward
        : jaccard >= O.VerbatimThreshold ? CopyKind.Verbatim
        : CopyKind.Near;
}
