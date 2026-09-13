using Microsoft.EntityFrameworkCore;
using Puluj.Contracts;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Api.Services;

/// <summary>Read side: live snapshot, historical replay from revisions (spec §20), track details with provenance (spec §18).</summary>
public sealed class SnapshotService(IDbContextFactory<PulujDbContext> factory, DtoMapper mapper, TimeProvider clock)
{
    /// <summary>Tracks that ended earlier than this before `at` are not part of a snapshot.</summary>
    private static readonly TimeSpan RecentWindow = TimeSpan.FromHours(3);

    public async Task<SnapshotDto> LiveAsync(bool activeOnly, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var db = await factory.CreateDbContextAsync(ct);
        var since = now - RecentWindow;
        var tracks = await db.TargetTracks.AsNoTracking()
            .Where(t => activeOnly ? t.Status == TrackStatus.Active : t.LastSeenAt >= since || t.Status == TrackStatus.Active)
            .OrderByDescending(t => t.LastSeenAt)
            .ToListAsync(ct);
        var alerts = await db.AirAlerts.AsNoTracking()
            .Where(a => a.EndedAt == null)
            .OrderBy(a => a.StartedAt)
            .ToListAsync(ct);
        var ids = tracks.Select(t => t.TargetTrackId).ToList();
        var sources = await SourceIdsAsync(db, ids, null, ct);
        var fixes = await FixesAsync(db, ids, null, ct);
        var messages = await MessageIdsAsync(db, ids, null, ct);
        return new SnapshotDto(now, false, tracks.Select(t => mapper.Track(t, sources.GetValueOrDefault(t.TargetTrackId, []), fixes.GetValueOrDefault(t.TargetTrackId), messages.GetValueOrDefault(t.TargetTrackId))).ToList(), alerts.Select(mapper.Alert).ToList());
    }

    public async Task<SnapshotDto> AtAsync(DateTimeOffset at, bool activeOnly, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var since = at - RecentWindow;
        // Latest revision of every track as of `at`.
        var revisions = await db.TargetTrackRevisions
            .FromSqlInterpolated($"""
                SELECT DISTINCT ON (target_track_id) *
                FROM target_track_revisions
                WHERE revision_at <= {at} AND last_seen_at >= {since}
                ORDER BY target_track_id, revision_at DESC
                """)
            .AsNoTracking()
            .ToListAsync(ct);
        var visible = revisions.Where(r => !activeOnly || r.Status == TrackStatus.Active).ToList();
        var trackIds = visible.Select(r => r.TargetTrackId).ToList();
        var sources = await SourceIdsAsync(db, trackIds, at, ct);
        var fixes = await FixesAsync(db, trackIds, at, ct);
        var messages = await MessageIdsAsync(db, trackIds, at, ct);
        var alerts = await db.AirAlerts.AsNoTracking()
            .Where(a => a.StartedAt <= at && (a.EndedAt == null || a.EndedAt > at))
            .OrderBy(a => a.StartedAt)
            .ToListAsync(ct);
        return new SnapshotDto(at, true,
            visible.OrderByDescending(r => r.LastSeenAt).Select(r => mapper.Track(r, sources.GetValueOrDefault(r.TargetTrackId, []), fixes.GetValueOrDefault(r.TargetTrackId), messages.GetValueOrDefault(r.TargetTrackId))).ToList(),
            alerts.Select(mapper.Alert).ToList());
    }

    public async Task<TrackDto?> TrackAsync(long id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var t = await db.TargetTracks.AsNoTracking().FirstOrDefaultAsync(x => x.TargetTrackId == id, ct);
        return t is null ? null : mapper.Track(t, (await SourceIdsAsync(db, [id], null, ct)).GetValueOrDefault(id, []), (await FixesAsync(db, [id], null, ct)).GetValueOrDefault(id), (await MessageIdsAsync(db, [id], null, ct)).GetValueOrDefault(id));
    }

    /// <summary>How many of the newest raw messages travel with a track (for "neighbours by message").</summary>
    private const int MaxMessages = 6;

    private static async Task<Dictionary<long, List<long>>> MessageIdsAsync(PulujDbContext db, List<long> trackIds, DateTimeOffset? at, CancellationToken ct)
    {
        if (trackIds.Count == 0)
        {
            return [];
        }
        var rows = await db.TrackTargets.AsNoTracking()
            .Where(l => trackIds.Contains(l.TargetTrackId) && (at == null || l.Target!.ObservedAt <= at))
            .Select(l => new { l.TargetTrackId, l.Target!.RawMessageId, l.Target.ObservedAt })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.TargetTrackId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.ObservedAt).Select(r => r.RawMessageId).Distinct().Take(MaxMessages).ToList());
    }

    /// <summary>How many earlier positions travel with a track (the current one included).</summary>
    private const int MaxFixes = 4;

    /// <summary>The last MaxFixes distinct reported positions of a track, oldest first; repeats of the same place collapse.</summary>
    private List<FixDto> Fixes(IEnumerable<Target> targets)
    {
        var fixes = new List<FixDto>();
        foreach (var o in targets.Where(o => o.DuplicateOfTargetId == null && o.EventType == EventType.TargetObserved).OrderBy(o => o.ObservedAt))
        {
            var fix = mapper.Fix(o);
            if (fix is null)
            {
                continue;
            }
            if (fixes.Count > 0 && fixes[^1].PlaceName == fix.PlaceName && fixes[^1].Approach == fix.Approach)
            {
                fixes[^1] = fix; // the same place again: keep the newer time
                continue;
            }
            fixes.Add(fix);
        }
        return fixes.Count > MaxFixes ? fixes.GetRange(fixes.Count - MaxFixes, MaxFixes) : fixes;
    }

    private async Task<Dictionary<long, List<FixDto>>> FixesAsync(PulujDbContext db, List<long> trackIds, DateTimeOffset? at, CancellationToken ct)
    {
        if (trackIds.Count == 0)
        {
            return [];
        }
        var rows = await db.TrackTargets.AsNoTracking()
            .Where(l => trackIds.Contains(l.TargetTrackId) && (at == null || l.Target!.ObservedAt <= at))
            .Select(l => new { l.TargetTrackId, Target = l.Target! })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.TargetTrackId).ToDictionary(g => g.Key, g => Fixes(g.Select(r => r.Target)));
    }

    /// <summary>Distinct sources behind each track (optionally as of an instant), for the per-source filter and the badge.</summary>
    private static async Task<Dictionary<long, int[]>> SourceIdsAsync(PulujDbContext db, List<long> trackIds, DateTimeOffset? at, CancellationToken ct)
    {
        if (trackIds.Count == 0)
        {
            return [];
        }
        var rows = await db.TrackTargets.AsNoTracking()
            .Where(l => trackIds.Contains(l.TargetTrackId) && (at == null || l.Target!.ObservedAt <= at))
            .Select(l => new { l.TargetTrackId, l.Target!.SourceId })
            .Distinct()
            .ToListAsync(ct);
        return rows.GroupBy(r => r.TargetTrackId).ToDictionary(g => g.Key, g => g.Select(r => r.SourceId).OrderBy(x => x).ToArray());
    }

    public async Task<TrackDetailsDto?> TrackDetailsAsync(long id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var t = await db.TargetTracks.AsNoTracking().FirstOrDefaultAsync(x => x.TargetTrackId == id, ct);
        if (t is null)
        {
            return null;
        }
        var links = await db.TrackTargets.AsNoTracking()
            .Where(l => l.TargetTrackId == id)
            .Include(l => l.Target!).ThenInclude(o => o.RawMessage)
            .OrderBy(l => l.Target!.ObservedAt).ThenBy(l => l.TargetId)
            .ToListAsync(ct);
        var sourceIds = links.Select(l => l.Target!.SourceId).Distinct().OrderBy(x => x).ToArray();
        var messageIds = links.OrderByDescending(l => l.Target!.ObservedAt).Select(l => l.Target!.RawMessageId).Distinct().Take(MaxMessages).ToList();
        var targetIds = links.Select(l => l.TargetId).ToList();
        var targetLinks = await db.TargetLinks.AsNoTracking()
            .Where(l => targetIds.Contains(l.FromTargetId) || targetIds.Contains(l.ToTargetId))
            .ToListAsync(ct);
        IReadOnlyList<TargetLinkDto> LinksOf(long targetId) => targetLinks
            .Where(l => l.FromTargetId == targetId || l.ToTargetId == targetId)
            .Select(l => l.FromTargetId == targetId
                ? new TargetLinkDto(l.ToTargetId, l.Kind.ToString(), l.Confidence, "to")
                : new TargetLinkDto(l.FromTargetId, l.Kind.ToString(), l.Confidence, "from"))
            .ToList();
        return new TrackDetailsDto(mapper.Track(t, sourceIds, Fixes(links.Select(l => l.Target!)), messageIds), links.Select(l => mapper.Target(l.Target!, l.AssociationConfidence, id, LinksOf(l.TargetId))).ToList());
    }

    /// <summary>Newest targets first (feed panel). Duplicates are kept — they are provenance too.</summary>
    public async Task<IReadOnlyList<TargetDto>> RecentTargetsAsync(DateTimeOffset since, DateTimeOffset? until, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Targets.AsNoTracking()
            .Include(o => o.RawMessage)
            .Where(o => o.ObservedAt >= since && (until == null || o.ObservedAt <= until))
            .OrderByDescending(o => o.ObservedAt).ThenByDescending(o => o.TargetId)
            .Take(Math.Clamp(limit, 1, 5000))
            .ToListAsync(ct);
        var trackOf = await TrackOfAsync(db, rows.Select(o => o.TargetId).ToList(), ct);
        return rows.Select(o => mapper.Target(o, null, trackOf.TryGetValue(o.TargetId, out var t) ? t : null)).ToList();
    }

    /// <summary>Which track each target belongs to (the feed highlights the lines behind the selected target).</summary>
    private static async Task<Dictionary<long, long>> TrackOfAsync(PulujDbContext db, List<long> targetIds, CancellationToken ct)
    {
        if (targetIds.Count == 0)
        {
            return [];
        }
        return await db.TrackTargets.AsNoTracking()
            .Where(l => targetIds.Contains(l.TargetId))
            .GroupBy(l => l.TargetId)
            .Select(g => new { g.Key, TrackId = g.Min(l => l.TargetTrackId) })
            .ToDictionaryAsync(x => x.Key, x => x.TrackId, ct);
    }

    /// <summary>Alerts of a place over the last `hours`, ended ones included, newest first: the region window's history.</summary>
    public async Task<IReadOnlyList<AlertDto>> AlertHistoryAsync(int placeId, double hours, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var since = clock.GetUtcNow().AddHours(-Math.Clamp(hours, 1, 24 * 14));
        var rows = await db.AirAlerts.AsNoTracking()
            .Where(a => a.PlaceId == placeId && (a.EndedAt == null || a.EndedAt >= since))
            .OrderByDescending(a => a.StartedAt)
            .Take(200)
            .ToListAsync(ct);
        return rows.Select(mapper.Alert).ToList();
    }

    public async Task<TargetDto?> TargetAsync(long id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var o = await db.Targets.AsNoTracking().Include(x => x.RawMessage).FirstOrDefaultAsync(x => x.TargetId == id, ct);
        if (o is null)
        {
            return null;
        }
        var trackOf = await TrackOfAsync(db, [id], ct);
        return mapper.Target(o, null, trackOf.TryGetValue(id, out var t) ? t : null);
    }

    public async Task<AlertDto?> AlertAsync(long id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var a = await db.AirAlerts.AsNoTracking().FirstOrDefaultAsync(x => x.AirAlertId == id, ct);
        return a is null ? null : mapper.Alert(a);
    }

    public async Task<IReadOnlyList<TimelineBucketDto>> TimelineAsync(DateTimeOffset from, DateTimeOffset to, int bucketMinutes, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var bucket = TimeSpan.FromMinutes(Math.Clamp(bucketMinutes, 1, 24 * 60));
        var targets = await db.Targets.AsNoTracking()
            .Where(o => o.ObservedAt >= from && o.ObservedAt < to && o.DuplicateOfTargetId == null)
            .Select(o => o.ObservedAt)
            .ToListAsync(ct);
        var opened = await db.TargetTracks.AsNoTracking()
            .Where(t => t.FirstSeenAt >= from && t.FirstSeenAt < to)
            .Select(t => t.FirstSeenAt)
            .ToListAsync(ct);
        var alerts = await db.AirAlerts.AsNoTracking()
            .Where(a => a.StartedAt < to && (a.EndedAt == null || a.EndedAt >= from))
            .Select(a => new { a.StartedAt, a.EndedAt })
            .ToListAsync(ct);

        var buckets = new List<TimelineBucketDto>();
        for (var start = from; start < to; start += bucket)
        {
            var end = start + bucket;
            buckets.Add(new TimelineBucketDto(start,
                targets.Count(x => x >= start && x < end),
                opened.Count(x => x >= start && x < end),
                alerts.Count(a => a.StartedAt < end && (a.EndedAt == null || a.EndedAt >= start))));
        }
        return buckets;
    }
}
