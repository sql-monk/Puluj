using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;
using Puluj.Processing.Indexes;
using Puluj.Processing.Parsing;
using Puluj.Processing.Text;

namespace Puluj.Processing.Structured;

/// <summary>
/// Handles RawMessages produced by the alerts.in.ua collector (payload kind alert.started / alert.finished):
/// maintains AirAlert intervals and emits AirRaidAlert / AlertCancelled targets without any NLP.
/// </summary>
public sealed class AlertsInUaHandler(IIndexes indexes, INormalizer normalizer, ILogger<AlertsInUaHandler> logger)
{
    public const string Version = "alerts_in_ua-0.1";

    public static bool CanHandle(RawMessage raw) =>
        raw.RawPayload is not null
        && raw.RawPayload.RootElement.TryGetProperty("kind", out var k)
        && k.GetString() is "alert.started" or "alert.finished";

    public async Task<List<Target>> HandleAsync(PulujDbContext db, RawMessage raw, Source source, CancellationToken ct)
    {
        var root = raw.RawPayload!.RootElement;
        var kind = root.GetProperty("kind").GetString()!;
        var alert = root.GetProperty("alert");
        var sourceAlertId = alert.GetProperty("id").ToString();
        var title = Str(alert, "location_title") ?? "";
        var oblast = Str(alert, "location_oblast") ?? title;
        var locationType = Str(alert, "location_type") ?? "oblast";
        var alertType = Str(alert, "alert_type") switch
        {
            "air_raid" => AirAlertType.AirRaid,
            "artillery_shelling" => AirAlertType.ArtilleryShelling,
            "urban_fights" => AirAlertType.UrbanFights,
            "chemical" => AirAlertType.Chemical,
            "nuclear" => AirAlertType.Nuclear,
            _ => AirAlertType.Unknown,
        };
        var at = root.TryGetProperty("at", out var atEl) && DateTimeOffset.TryParse(atEl.GetString(), out var parsed) ? parsed : raw.PublishedAt;

        // Only oblast-level polygons exist in the gazetteer today; raion/hromada alerts map to their oblast.
        var place = ResolveRegion(locationType == "oblast" ? title : oblast) ?? ResolveRegion(title);
        if (place is null)
        {
            logger.LogWarning("alerts.in.ua: unknown location '{Title}' / '{Oblast}'", title, oblast);
        }

        var existing = await db.AirAlerts.FirstOrDefaultAsync(a => a.SourceId == source.SourceId && a.SourceAlertId == sourceAlertId, ct);
        if (kind == "alert.started")
        {
            if (existing is null && place is not null)
            {
                var startedAt = DateTimeOffset.TryParse(Str(alert, "started_at"), out var s) ? s : at;
                db.AirAlerts.Add(new AirAlert
                {
                    SourceId = source.SourceId,
                    SourceAlertId = sourceAlertId,
                    PlaceId = place.PlaceId,
                    AlertType = alertType,
                    StartedAt = startedAt,
                    StartRawMessageId = raw.RawMessageId,
                });
            }
        }
        else if (existing is not null && existing.EndedAt is null)
        {
            existing.EndedAt = at;
            existing.EndRawMessageId = raw.RawMessageId;
        }

        var obs = new Target
        {
            RawMessageId = raw.RawMessageId,
            SourceId = source.SourceId,
            SegmentIndex = 0,
            SegmentText = $"{(kind == "alert.started" ? "Тривога" : "Відбій")}: {title} ({Str(alert, "alert_type")})",
            ObservedAt = at,
            EventType = kind == "alert.started" ? EventType.AirRaidAlert : EventType.AlertCancelled,
            IdentificationMethod = IdentificationMethod.Structured,
            IdentificationSource = "alerts.in.ua",
            ParserVersion = Version,
            Confidence = ConfidenceLevel.Confirmed,
            LocationKind = place is null ? LocationKind.Unknown : Pipeline.TargetBuilder.KindFor(place.Level),
            LocationPlaceId = place?.PlaceId,
            Location = place?.Centroid,
            LocationAccuracyKm = place?.RadiusKm,
            DirectionKind = DirectionKind.Unknown,
            ParserMetadata = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                kind,
                alertType = Str(alert, "alert_type"),
                locationType,
                title,
                oblast,
                placeId = place?.PlaceId,
                sourceAlertId,
            })),
        };
        return [obs];
    }

    private PlaceEntry? ResolveRegion(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }
        var normalized = normalizer.Normalize(title);
        if (normalized.Segments.Count == 0)
        {
            return null;
        }
        var matcher = new PlaceMatcher(indexes.Gazetteer);
        var ctx = new ParseContext(0, "uk", null);
        return matcher.Match(normalized.Segments[0], ctx, new HashSet<int>(), new HashSet<(int, int)>())
            .Select(m => m.Place)
            .Where(p => p.Level is PlaceLevel.Region or PlaceLevel.City && p.ParentId is null)
            .FirstOrDefault();
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
