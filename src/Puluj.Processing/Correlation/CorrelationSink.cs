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
/// Deduplicator + Threat Correlator (spec §4, §10). Runs inside the message transaction:
/// duplicates are linked to the original's track for provenance, new facts either continue the best-scoring
/// active track or open a new one; alert/threat cancellations close tracks in the affected region.
/// </summary>
public sealed class CorrelationSink(
    IIndexes indexes,
    IOptionsMonitor<CorrelationOptions> options,
    TimeProvider clock,
    ILogger<CorrelationSink> logger) : IObservationSink
{
    private const int CandidateWindowMinutes = 120;

    public async Task OnObservationsAsync(PulujDbContext db, IReadOnlyList<Observation> observations, Source source, ICollection<PulujEvent> events, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        foreach (var o in observations.OrderBy(x => x.ObservedAt))
        {
            switch (o.EventType)
            {
                case EventType.ThreatObserved when o.ThreatCategoryId is not null:
                    await HandleThreatAsync(db, o, source, events, now, ct);
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
                case EventType.ThreatCancelled:
                    await CloseTracksInRegionAsync(db, o, "threat_cancelled", events, now, ct);
                    break;
            }
            events.Add(new PulujEvent(PulujEventType.ObservationCreated, o.ObservationId, now));
        }
    }

    private async Task HandleThreatAsync(PulujDbContext db, Observation o, Source source, ICollection<PulujEvent> events, DateTimeOffset now, CancellationToken ct)
    {
        var duplicateOf = await FindDuplicateAsync(db, o, ct);
        if (duplicateOf is not null)
        {
            o.DuplicateOfObservationId = duplicateOf.ObservationId;
            // A second, independent source confirming the same fact raises confidence of the original.
            if (duplicateOf.SourceId != o.SourceId && duplicateOf.ObservationConfidence < ConfidenceLevel.High)
            {
                duplicateOf.ObservationConfidence++;
            }
            var link = await db.ThreatTrackObservations.Include(l => l.Track)
                .FirstOrDefaultAsync(l => l.ObservationId == duplicateOf.ObservationId, ct);
            if (link?.Track is { } track)
            {
                db.ThreatTrackObservations.Add(new ThreatTrackObservation
                {
                    ThreatTrackId = track.ThreatTrackId,
                    ObservationId = o.ObservationId,
                    Sequence = track.ObservationCount,
                    AssociationConfidence = link.AssociationConfidence,
                    AssociationReason = System.Text.Json.JsonDocument.Parse($"{{\"duplicateOf\":{duplicateOf.ObservationId}}}"),
                });
                await RecountSourcesAsync(db, track, o.SourceId, ct);
                track.TrackConfidence = TrackUpdater.ComputeTrackConfidence(track, await BestObservationConfidenceAsync(db, track, o, ct));
                track.UpdatedAt = Later(track.UpdatedAt, o.ObservedAt);
                db.ThreatTrackRevisions.Add(TrackUpdater.Revision(track, o.ObservationId, track.UpdatedAt));
                events.Add(new PulujEvent(PulujEventType.TrackUpserted, track.ThreatTrackId, now));
            }
            logger.LogDebug("Observation {Id} duplicates {Original}", o.ObservationId, duplicateOf.ObservationId);
            return;
        }

        // Candidate window is generous; the per-pair class profile (observation class, else track class) drives the score.
        var since = o.ObservedAt.AddMinutes(-CandidateWindowMinutes);
        var until = o.ObservedAt.AddMinutes(CandidateWindowMinutes);
        var candidates = await db.ThreatTracks
            .Where(t => t.Status == TrackStatus.Active && t.ThreatCategoryId == o.ThreatCategoryId && t.LastSeenAt >= since && t.LastSeenAt <= until)
            .ToListAsync(ct);

        ThreatTrack? best = null;
        AssociationScore? bestScore = null;
        foreach (var t in candidates.Where(t => Correlator.ClassCompatible(o, t)))
        {
            var profile = indexes.Taxonomy.ClassProfile(o.ThreatClassId ?? t.ThreatClassId);
            var score = Correlator.Score(o, t, profile, options.CurrentValue.SlackKm);
            if (score.Total >= options.CurrentValue.AttachThreshold && (bestScore is null || score.Total > bestScore.Total))
            {
                best = t;
                bestScore = score;
            }
        }

        if (best is null)
        {
            best = TrackUpdater.CreateTrack(o, now);
            best.DistinctSourceCount = 1;
            best.TrackConfidence = TrackUpdater.ComputeTrackConfidence(best, o.ObservationConfidence);
            db.ThreatTracks.Add(best);
            db.ThreatTrackObservations.Add(new ThreatTrackObservation
            {
                Track = best,
                ObservationId = o.ObservationId,
                Sequence = 1,
                AssociationConfidence = 1,
            });
            db.ThreatTrackRevisions.Add(TrackUpdater.Revision(best, o.ObservationId, best.UpdatedAt));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Track {Track} opened from observation {Obs}", best.ThreatTrackId, o.ObservationId);
        }
        else
        {
            TrackUpdater.Apply(best, o, now, isNewer: o.ObservedAt >= best.LastSeenAt);
            db.ThreatTrackObservations.Add(new ThreatTrackObservation
            {
                ThreatTrackId = best.ThreatTrackId,
                ObservationId = o.ObservationId,
                Sequence = best.ObservationCount,
                AssociationConfidence = bestScore!.Total,
                AssociationReason = bestScore.ToJson(),
            });
            await RecountSourcesAsync(db, best, o.SourceId, ct);
            best.TrackConfidence = TrackUpdater.ComputeTrackConfidence(best, await BestObservationConfidenceAsync(db, best, o, ct));
            db.ThreatTrackRevisions.Add(TrackUpdater.Revision(best, o.ObservationId, best.UpdatedAt));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Observation {Obs} attached to track {Track} (score {Score:F2})", o.ObservationId, best.ThreatTrackId, bestScore.Total);
        }
        events.Add(new PulujEvent(PulujEventType.TrackUpserted, best.ThreatTrackId, now));
    }

    /// <summary>Same class, same place, same event within the duplicate window, from another message.</summary>
    private async Task<Observation?> FindDuplicateAsync(PulujDbContext db, Observation o, CancellationToken ct)
    {
        var window = options.CurrentValue.DuplicateWindow;
        var from = o.ObservedAt - window;
        var to = o.ObservedAt + window;
        var query = db.Observations
            .Where(x => x.ObservationId != o.ObservationId && x.RawMessageId != o.RawMessageId
                        && x.DuplicateOfObservationId == null
                        && x.EventType == o.EventType
                        && x.ObservedAt >= from && x.ObservedAt <= to
                        && x.ThreatCategoryId == o.ThreatCategoryId
                        && (x.ThreatClassId == o.ThreatClassId || x.ThreatClassId == null || o.ThreatClassId == null));
        query = o.LocationPlaceId is int place
            ? query.Where(x => x.LocationPlaceId == place)
            : query.Where(x => x.LocationPlaceId == null && x.DestinationPlaceId == o.DestinationPlaceId);
        return await query.OrderBy(x => x.ObservedAt).FirstOrDefaultAsync(ct);
    }

    private async Task CloseTracksInRegionAsync(PulujDbContext db, Observation o, string reason, ICollection<PulujEvent> events, DateTimeOffset now, CancellationToken ct)
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
        var active = await db.ThreatTracks.Where(t => t.Status == TrackStatus.Active && t.LastLocationPlaceId != null).ToListAsync(ct);
        foreach (var t in active)
        {
            var tp = indexes.Gazetteer.Get(t.LastLocationPlaceId!.Value);
            if (tp is null || indexes.Gazetteer.RegionOf(tp)?.PlaceId != region.PlaceId)
            {
                continue;
            }
            // Only close tracks of the same threat kind when the cancellation names one ("відбій загрози БпЛА").
            if (o.ThreatCategoryId is not null && o.ThreatCategoryId != t.ThreatCategoryId)
            {
                continue;
            }
            t.Status = TrackStatus.Cancelled;
            t.ClosedReason = reason;
            t.UpdatedAt = Later(t.UpdatedAt, o.ObservedAt);
            db.ThreatTrackRevisions.Add(TrackUpdater.Revision(t, o.ObservationId, t.UpdatedAt));
            events.Add(new PulujEvent(PulujEventType.TrackClosed, t.ThreatTrackId, now));
            logger.LogInformation("Track {Track} closed ({Reason}) by observation {Obs}", t.ThreatTrackId, reason, o.ObservationId);
        }
    }

    private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static async Task RecountSourcesAsync(PulujDbContext db, ThreatTrack track, int currentSourceId, CancellationToken ct)
    {
        var persisted = await db.ThreatTrackObservations
            .Where(l => l.ThreatTrackId == track.ThreatTrackId)
            .Select(l => l.Observation!.SourceId)
            .Distinct()
            .ToListAsync(ct);
        track.DistinctSourceCount = persisted.Append(currentSourceId).Distinct().Count();
    }

    private static async Task<ConfidenceLevel> BestObservationConfidenceAsync(PulujDbContext db, ThreatTrack track, Observation current, CancellationToken ct)
    {
        var levels = await db.ThreatTrackObservations
            .Where(l => l.ThreatTrackId == track.ThreatTrackId)
            .Select(l => (int)l.Observation!.ObservationConfidence)
            .ToListAsync(ct);
        return (ConfidenceLevel)levels.Append((int)current.ObservationConfidence).Max();
    }
}
