using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Messaging;
using Puluj.Infrastructure.Persistence;
using Puluj.Processing.Indexes;
using Puluj.Processing.Pipeline;

namespace Puluj.Processing.Correlation;

/// <summary>
/// Deduplicator + Target Correlator (spec §4, §10). Runs inside the message transaction:
/// duplicates are linked to the original's track for provenance, new facts either continue the best-scoring
/// active track or open a new one; alert/target cancellations close tracks in the affected region.
/// </summary>
public sealed class CorrelationSink(
    IIndexes indexes,
    IOptionsMonitor<CorrelationOptions> options,
    TimeProvider clock,
    ILogger<CorrelationSink> logger) : ITargetSink
{
    private const int CandidateWindowMinutes = 120;
    /// <summary>Candidates scoring at least this (but below the attach threshold) are kept as "possible" links.</summary>
    private const double PossibleThreshold = 0.45;

    public async Task OnTargetsAsync(PulujDbContext db, IReadOnlyList<Target> targets, Source source, ICollection<PulujEvent> events, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // A message that lists several sightings lists several objects: no two facts from it may share a track.
        var usedTracks = new HashSet<long>();
        foreach (var o in targets.OrderBy(x => x.ObservedAt))
        {
            switch (o.EventType)
            {
                case EventType.TargetObserved when o.TargetCategoryId is not null:
                    await HandleTargetAsync(db, o, source, events, now, usedTracks, ct);
                    break;
                case EventType.AirRaidAlert or EventType.AlertCancelled:
                    var alert = await db.AirAlerts.AsNoTracking()
                        .FirstOrDefaultAsync(a => a.StartRawMessageId == o.RawMessageId || a.EndRawMessageId == o.RawMessageId, ct);
                    if (alert is not null)
                    {
                        events.Add(new PulujEvent(PulujEventType.AlertChanged, alert.AirAlertId, now));
                    }
                    if (o.EventType == EventType.AlertCancelled)
                    {
                        await CloseTracksInRegionAsync(db, o, "alert_cancelled", events, now, ct);
                    }
                    break;
                case EventType.TargetCancelled:
                    await CloseTracksInRegionAsync(db, o, "target_cancelled", events, now, ct);
                    break;
            }
            events.Add(new PulujEvent(PulujEventType.TargetCreated, o.TargetId, now));
        }
    }

    private async Task HandleTargetAsync(PulujDbContext db, Target o, Source source, ICollection<PulujEvent> events, DateTimeOffset now, HashSet<long> usedTracks, CancellationToken ct)
    {
        var duplicateOf = await FindDuplicateAsync(db, o, ct);
        if (duplicateOf is not null)
        {
            o.DuplicateOfTargetId = duplicateOf.TargetId;
            // A second, independent source confirming the same fact raises confidence of the original.
            if (duplicateOf.SourceId != o.SourceId && duplicateOf.Confidence < ConfidenceLevel.High)
            {
                duplicateOf.Confidence++;
            }
            var link = await db.TrackTargets.Include(l => l.Track)
                .FirstOrDefaultAsync(l => l.TargetId == duplicateOf.TargetId, ct);
            if (link?.Track is { } track)
            {
                db.TrackTargets.Add(new TrackTarget
                {
                    TargetTrackId = track.TargetTrackId,
                    TargetId = o.TargetId,
                    Sequence = track.TargetCount,
                    AssociationConfidence = link.AssociationConfidence,
                    AssociationReason = System.Text.Json.JsonDocument.Parse($"{{\"duplicateOf\":{duplicateOf.TargetId}}}"),
                });
                usedTracks.Add(track.TargetTrackId);
                AddLink(db, duplicateOf.TargetId, o.TargetId, TargetLinkKind.Duplicate, 1, now);
                await RecountSourcesAsync(db, track, o.SourceId, ct);
                track.TrackConfidence = TrackUpdater.ComputeTrackConfidence(track, await BestConfidenceAsync(db, track, o, ct));
                track.UpdatedAt = Later(track.UpdatedAt, o.ObservedAt);
                db.TargetTrackRevisions.Add(TrackUpdater.Revision(track, o.TargetId, track.UpdatedAt));
                events.Add(new PulujEvent(PulujEventType.TrackUpserted, track.TargetTrackId, now));
            }
            logger.LogDebug("Target {Id} duplicates {Original}", o.TargetId, duplicateOf.TargetId);
            return;
        }

        // A sighting that names neither a place nor a destination cannot be put on the map, and attaching it to
        // whichever track is freshest would only corrupt that track's provenance. It stays in the feed.
        var oAnchor = Correlator.AnchorOf(o, indexes.Gazetteer);
        if (oAnchor is null)
        {
            logger.LogDebug("Target {Id} has no spatial anchor; no track", o.TargetId);
            return;
        }
        var destination = o.DestinationPlaceId is int destId ? indexes.Gazetteer.Get(destId) : null;
        var approach = destination is null ? null : indexes.Gazetteer.ApproachBearingTo(destination.Centroid.Coordinate);

        // Candidate window is generous; the per-pair class profile (target class, else track class) drives the score.
        var since = o.ObservedAt.AddMinutes(-CandidateWindowMinutes);
        var until = o.ObservedAt.AddMinutes(CandidateWindowMinutes);
        var candidates = await db.TargetTracks
            .Where(t => t.Status == TrackStatus.Active && t.TargetCategoryId == o.TargetCategoryId && t.LastSeenAt >= since && t.LastSeenAt <= until)
            .ToListAsync(ct);

        TargetTrack? best = null;
        AssociationScore? bestScore = null;
        // Every scored candidate is remembered: the ones not chosen still become links (merge / split / possible).
        var scored = new List<(TargetTrack Track, AssociationScore Score, bool Blocked)>();
        foreach (var t in candidates.Where(t => Correlator.ClassCompatible(o, t)))
        {
            var profile = indexes.Taxonomy.ClassProfile(o.TargetClassId ?? t.TargetClassId);
            var score = Correlator.Score(o, t, profile, options.CurrentValue.SlackKm, oAnchor, Correlator.AnchorOf(t, indexes.Gazetteer));
            var blocked = usedTracks.Contains(t.TargetTrackId); // another fact of this message already continues it
            scored.Add((t, score, blocked));
            if (!blocked && score.Total >= options.CurrentValue.AttachThreshold && (bestScore is null || score.Total > bestScore.Total))
            {
                best = t;
                bestScore = score;
            }
        }
        var predecessorOfBest = best?.LastTargetId;

        if (best is null)
        {
            best = TrackUpdater.CreateTrack(o, now, destination, approach);
            best.DistinctSourceCount = 1;
            best.TrackConfidence = TrackUpdater.ComputeTrackConfidence(best, o.Confidence);
            db.TargetTracks.Add(best);
            db.TrackTargets.Add(new TrackTarget
            {
                Track = best,
                TargetId = o.TargetId,
                Sequence = 1,
                AssociationConfidence = 1,
            });
            db.TargetTrackRevisions.Add(TrackUpdater.Revision(best, o.TargetId, best.UpdatedAt));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Track {Track} opened from target {Obs}", best.TargetTrackId, o.TargetId);
        }
        else
        {
            TrackUpdater.Apply(best, o, now, isNewer: o.ObservedAt >= best.LastSeenAt, destination, approach);
            db.TrackTargets.Add(new TrackTarget
            {
                TargetTrackId = best.TargetTrackId,
                TargetId = o.TargetId,
                Sequence = best.TargetCount,
                AssociationConfidence = bestScore!.Total,
                AssociationReason = bestScore.ToJson(),
            });
            await RecountSourcesAsync(db, best, o.SourceId, ct);
            best.TrackConfidence = TrackUpdater.ComputeTrackConfidence(best, await BestConfidenceAsync(db, best, o, ct));
            db.TargetTrackRevisions.Add(TrackUpdater.Revision(best, o.TargetId, best.UpdatedAt));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Target {Obs} attached to track {Track} (score {Score:F2})", o.TargetId, best.TargetTrackId, bestScore.Total);
        }
        LinkCandidates(db, o, scored, best, predecessorOfBest, bestScore, now);
        await db.SaveChangesAsync(ct);
        usedTracks.Add(best.TargetTrackId);
        events.Add(new PulujEvent(PulujEventType.TrackUpserted, best.TargetTrackId, now));
    }

    /// <summary>Same class, same place, same event within the duplicate window, from another message.</summary>
    private async Task<Target?> FindDuplicateAsync(PulujDbContext db, Target o, CancellationToken ct)
    {
        var window = options.CurrentValue.DuplicateWindow;
        var from = o.ObservedAt - window;
        var to = o.ObservedAt + window;
        var query = db.Targets
            .Where(x => x.TargetId != o.TargetId && x.RawMessageId != o.RawMessageId
                        && x.DuplicateOfTargetId == null
                        && x.EventType == o.EventType
                        && x.ObservedAt >= from && x.ObservedAt <= to
                        && x.TargetCategoryId == o.TargetCategoryId
                        && (x.TargetClassId == o.TargetClassId || x.TargetClassId == null || o.TargetClassId == null));
        query = o.LocationPlaceId is int place
            ? query.Where(x => x.LocationPlaceId == place)
            : query.Where(x => x.LocationPlaceId == null && x.DestinationPlaceId == o.DestinationPlaceId);
        return await query.OrderBy(x => x.ObservedAt).FirstOrDefaultAsync(ct);
    }

    private async Task CloseTracksInRegionAsync(PulujDbContext db, Target o, string reason, ICollection<PulujEvent> events, DateTimeOffset now, CancellationToken ct)
    {
        if (o.LocationPlaceId is not int placeId)
        {
            return;
        }
        var region = indexes.Gazetteer.Get(placeId) is { } p ? indexes.Gazetteer.RegionOf(p) : null;
        if (region is null)
        {
            return;
        }
        var active = await db.TargetTracks.Where(t => t.Status == TrackStatus.Active && t.LastLocationPlaceId != null).ToListAsync(ct);
        foreach (var t in active)
        {
            var tp = indexes.Gazetteer.Get(t.LastLocationPlaceId!.Value);
            if (tp is null || indexes.Gazetteer.RegionOf(tp)?.PlaceId != region.PlaceId)
            {
                continue;
            }
            // Only close tracks of the same target kind when the cancellation names one ("відбій загрози БпЛА").
            if (o.TargetCategoryId is not null && o.TargetCategoryId != t.TargetCategoryId)
            {
                continue;
            }
            t.Status = TrackStatus.Cancelled;
            t.ClosedReason = reason;
            t.UpdatedAt = Later(t.UpdatedAt, o.ObservedAt);
            db.TargetTrackRevisions.Add(TrackUpdater.Revision(t, o.TargetId, t.UpdatedAt));
            events.Add(new PulujEvent(PulujEventType.TrackClosed, t.TargetTrackId, now));
            logger.LogInformation("Track {Track} closed ({Reason}) by target {Obs}", t.TargetTrackId, reason, o.TargetId);
        }
    }

    private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    /// <summary>
    /// Target-to-target links from one correlation pass. The chosen track's previous sighting continues into this one;
    /// other tracks that also scored high enough to attach merge into it; a track already continued by another fact of
    /// the same message splits into this one as well; weaker matches are kept as "possible".
    /// </summary>
    private void LinkCandidates(PulujDbContext db, Target o, List<(TargetTrack Track, AssociationScore Score, bool Blocked)> scored, TargetTrack? chosen, long? predecessorOfChosen, AssociationScore? chosenScore, DateTimeOffset now)
    {
        if (chosen is not null && predecessorOfChosen is long prev && chosenScore is not null)
        {
            AddLink(db, prev, o.TargetId, TargetLinkKind.Continuation, chosenScore.Total, now);
        }
        foreach (var (track, score, blocked) in scored)
        {
            if (track.LastTargetId is not long from || (chosen is not null && track.TargetTrackId == chosen.TargetTrackId))
            {
                continue;
            }
            if (score.Total >= options.CurrentValue.AttachThreshold)
            {
                // A track that could have taken this fact but did not: either it already continues into another fact
                // of this message (the group split), or the fact continues several tracks at once (they merged).
                AddLink(db, from, o.TargetId, blocked ? TargetLinkKind.Split : TargetLinkKind.Merge, score.Total, now);
            }
            else if (score.Total >= PossibleThreshold)
            {
                AddLink(db, from, o.TargetId, TargetLinkKind.Possible, score.Total, now);
            }
        }
    }

    private static void AddLink(PulujDbContext db, long from, long to, TargetLinkKind kind, double confidence, DateTimeOffset now)
    {
        if (from == to || db.TargetLinks.Local.Any(l => l.FromTargetId == from && l.ToTargetId == to))
        {
            return;
        }
        db.TargetLinks.Add(new TargetLink { FromTargetId = from, ToTargetId = to, Kind = kind, Confidence = Math.Round(confidence, 3), CreatedAt = now });
    }

    private static async Task RecountSourcesAsync(PulujDbContext db, TargetTrack track, int currentSourceId, CancellationToken ct)
    {
        var persisted = await db.TrackTargets
            .Where(l => l.TargetTrackId == track.TargetTrackId)
            .Select(l => l.Target!.SourceId)
            .Distinct()
            .ToListAsync(ct);
        track.DistinctSourceCount = persisted.Append(currentSourceId).Distinct().Count();
    }

    private static async Task<ConfidenceLevel> BestConfidenceAsync(PulujDbContext db, TargetTrack track, Target current, CancellationToken ct)
    {
        var levels = await db.TrackTargets
            .Where(l => l.TargetTrackId == track.TargetTrackId)
            .Select(l => (int)l.Target!.Confidence)
            .ToListAsync(ct);
        return (ConfidenceLevel)levels.Append((int)current.Confidence).Max();
    }
}
