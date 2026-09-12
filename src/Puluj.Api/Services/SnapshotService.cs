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
        var tracks = await db.ThreatTracks.AsNoTracking()
            .Where(t => activeOnly ? t.Status == TrackStatus.Active : t.LastSeenAt >= since || t.Status == TrackStatus.Active)
            .OrderByDescending(t => t.LastSeenAt)
            .ToListAsync(ct);
        var alerts = await db.AirAlerts.AsNoTracking()
            .Where(a => a.EndedAt == null)
            .OrderBy(a => a.StartedAt)
            .ToListAsync(ct);
        return new SnapshotDto(now, false, tracks.Select(mapper.Track).ToList(), alerts.Select(mapper.Alert).ToList());
    }

    public async Task<SnapshotDto> AtAsync(DateTimeOffset at, bool activeOnly, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var since = at - RecentWindow;
        // Latest revision of every track as of `at`.
        var revisions = await db.ThreatTrackRevisions
            .FromSqlInterpolated($"""
                SELECT DISTINCT ON (threat_track_id) *
                FROM threat_track_revisions
                WHERE revision_at <= {at} AND last_seen_at >= {since}
                ORDER BY threat_track_id, revision_at DESC
                """)
            .AsNoTracking()
            .ToListAsync(ct);
        var visible = revisions.Where(r => !activeOnly || r.Status == TrackStatus.Active).ToList();
        var trackIds = visible.Select(r => r.ThreatTrackId).ToList();
        var sourceCounts = await db.ThreatTrackObservations
            .Where(l => trackIds.Contains(l.ThreatTrackId) && l.Observation!.ObservedAt <= at)
            .GroupBy(l => l.ThreatTrackId)
            .Select(g => new { TrackId = g.Key, Count = g.Select(l => l.Observation!.SourceId).Distinct().Count() })
            .ToDictionaryAsync(x => x.TrackId, x => x.Count, ct);
        var alerts = await db.AirAlerts.AsNoTracking()
            .Where(a => a.StartedAt <= at && (a.EndedAt == null || a.EndedAt > at))
            .OrderBy(a => a.StartedAt)
            .ToListAsync(ct);
        return new SnapshotDto(at, true,
            visible.OrderByDescending(r => r.LastSeenAt).Select(r => mapper.Track(r, sourceCounts.GetValueOrDefault(r.ThreatTrackId, 1))).ToList(),
            alerts.Select(mapper.Alert).ToList());
    }

    public async Task<TrackDto?> TrackAsync(long id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var t = await db.ThreatTracks.AsNoTracking().FirstOrDefaultAsync(x => x.ThreatTrackId == id, ct);
        return t is null ? null : mapper.Track(t);
    }

    public async Task<TrackDetailsDto?> TrackDetailsAsync(long id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var t = await db.ThreatTracks.AsNoTracking().FirstOrDefaultAsync(x => x.ThreatTrackId == id, ct);
        if (t is null)
        {
            return null;
        }
        var links = await db.ThreatTrackObservations.AsNoTracking()
            .Where(l => l.ThreatTrackId == id)
            .Include(l => l.Observation!).ThenInclude(o => o.RawMessage)
            .OrderBy(l => l.Observation!.ObservedAt).ThenBy(l => l.ObservationId)
            .ToListAsync(ct);
        return new TrackDetailsDto(mapper.Track(t), links.Select(l => mapper.Observation(l.Observation!, l.AssociationConfidence)).ToList());
    }

    /// <summary>Newest observations first (feed panel). Duplicates are kept — they are provenance too.</summary>
    public async Task<IReadOnlyList<ObservationDto>> RecentObservationsAsync(DateTimeOffset since, DateTimeOffset? until, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Observations.AsNoTracking()
            .Include(o => o.RawMessage)
            .Where(o => o.ObservedAt >= since && (until == null || o.ObservedAt <= until))
            .OrderByDescending(o => o.ObservedAt).ThenByDescending(o => o.ObservationId)
            .Take(Math.Clamp(limit, 1, 5000))
            .ToListAsync(ct);
        return rows.Select(o => mapper.Observation(o, null)).ToList();
    }

    public async Task<ObservationDto?> ObservationAsync(long id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var o = await db.Observations.AsNoTracking().Include(x => x.RawMessage).FirstOrDefaultAsync(x => x.ObservationId == id, ct);
        return o is null ? null : mapper.Observation(o, null);
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
        var observations = await db.Observations.AsNoTracking()
            .Where(o => o.ObservedAt >= from && o.ObservedAt < to && o.DuplicateOfObservationId == null)
            .Select(o => o.ObservedAt)
            .ToListAsync(ct);
        var opened = await db.ThreatTracks.AsNoTracking()
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
                observations.Count(x => x >= start && x < end),
                opened.Count(x => x >= start && x < end),
                alerts.Count(a => a.StartedAt < end && (a.EndedAt == null || a.EndedAt >= start))));
        }
        return buckets;
    }
}
