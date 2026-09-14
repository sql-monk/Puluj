using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Messaging;
using Puluj.Infrastructure.Persistence;
using Puluj.Processing.Pipeline;

namespace Puluj.Processing.Structured;

/// <summary>
/// Turns AirRaidAlert / AlertCancelled targets parsed from text ("Фастівський район — повітряна тривога, жовтий рівень")
/// into AirAlert intervals, so levelled regional alerts show on the map next to the alerts.in.ua ones.
/// One open interval per (source, place); a repeated message only updates the level.
/// </summary>
public sealed class TextAlertSink(TimeProvider clock, ILogger<TextAlertSink> logger) : ITargetSink
{
    public const string KeyPrefix = "text:";

    /// <summary>Text alerts with no "відбій" are dropped after this long (see TrackWatchdog).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(3);

    public async Task OnTargetsAsync(PulujDbContext db, IReadOnlyList<Target> targets, Source source, ICollection<PulujEvent> events, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        foreach (var o in targets.OrderBy(x => x.ObservedAt))
        {
            if (o.IdentificationMethod == IdentificationMethod.Structured || o.LocationPlaceId is not int placeId)
            {
                continue;
            }
            if (o.EventType == EventType.AirRaidAlert)
            {
                // One key per interval ("text:<place>:<start>"): the (source, key) pair is unique, and a place gets
                // alerted again after an "відбій".
                var prefix = KeyPrefix + placeId + ":";
                var key = prefix + o.ObservedAt.ToUnixTimeSeconds();
                var open = await db.AirAlerts
                    .Where(a => a.SourceId == source.SourceId && a.SourceAlertId.StartsWith(prefix) && a.EndedAt == null)
                    .OrderByDescending(a => a.StartedAt)
                    .FirstOrDefaultAsync(ct);
                // Event time, not the clock: a rebuild of old messages must see the same intervals as live processing did.
                if (open is not null && o.ObservedAt - open.StartedAt > MaxAge)
                {
                    open.EndedAt = o.ObservedAt;
                    open = null;
                }
                if (open is null)
                {
                    var alert = new AirAlert
                    {
                        SourceId = source.SourceId,
                        SourceAlertId = key,
                        PlaceId = placeId,
                        AlertType = AirAlertType.AirRaid,
                        Level = o.AlertLevel,
                        StartedAt = o.ObservedAt,
                        StartRawMessageId = o.RawMessageId,
                    };
                    db.AirAlerts.Add(alert);
                    await db.SaveChangesAsync(ct);
                    events.Add(new PulujEvent(PulujEventType.AlertChanged, alert.AirAlertId, now));
                }
                else if (o.AlertLevel != AirAlertLevel.Unknown && open.Level != o.AlertLevel)
                {
                    open.Level = o.AlertLevel;
                    events.Add(new PulujEvent(PulujEventType.AlertChanged, open.AirAlertId, now));
                }
            }
            else if (o.EventType == EventType.AlertCancelled)
            {
                // "Відбій" for a place also ends the text alerts of the places inside it (raion towns inside their oblast).
                var open = await db.AirAlerts.Include(a => a.Place)
                    .Where(a => a.SourceAlertId.StartsWith(KeyPrefix) && a.EndedAt == null
                        && (a.PlaceId == placeId || a.Place!.ParentId == placeId))
                    .ToListAsync(ct);
                foreach (var a in open)
                {
                    a.EndedAt = o.ObservedAt;
                    a.EndRawMessageId = o.RawMessageId;
                    events.Add(new PulujEvent(PulujEventType.AlertChanged, a.AirAlertId, now));
                }
                if (open.Count > 0)
                {
                    logger.LogDebug("Text alert cancelled for place {PlaceId}: {Count} interval(s) closed", placeId, open.Count);
                }
            }
        }
    }
}
