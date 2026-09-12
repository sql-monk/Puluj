using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Ingestion;

namespace Puluj.Collectors.AlertsInUa;

/// <summary>
/// Polls alerts.in.ua active alerts and turns every state change into a RawMessage:
/// kind=alert.started (payload = alert object) / kind=alert.finished. The current active set is kept in
/// CollectorState.Cursor so ends that happened while the worker was down are still emitted after restart.
/// </summary>
public sealed class AlertsInUaCollector(
    IHttpClientFactory httpFactory,
    IOptionsMonitor<AlertsInUaOptions> options,
    RawMessageIngestor ingestor,
    CollectorStateStore states,
    TimeProvider clock,
    ILogger<AlertsInUaCollector> logger) : ICollector
{
    public const string HttpClientName = "alerts.in.ua";
    public const string CollectorCode = "alerts_in_ua";

    public string Name => "alerts.in.ua";

    public bool Handles(Source source) =>
        options.CurrentValue.Enabled
        && source.Type == SourceType.RestApi
        && source.Config?.RootElement.TryGetProperty("collector", out var c) == true
        && c.GetString() == CollectorCode;

    public async Task RunAsync(IReadOnlyList<Source> sources, CancellationToken ct)
    {
        var source = sources[0];
        if (string.IsNullOrWhiteSpace(options.CurrentValue.Token))
        {
            logger.LogWarning("alerts.in.ua token missing (Collectors:AlertsInUa:Token); collector idle");
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return;
        }

        var interval = source.PollingInterval ?? options.CurrentValue.PollingInterval;
        var known = await LoadKnownAsync(source.SourceId, ct);
        logger.LogInformation("alerts.in.ua: {Count} active alerts restored from cursor", known.Count);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                var active = await FetchActiveAsync(ct);
                known = await ReconcileAsync(source, known, active, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "alerts.in.ua poll failed");
                await states.MarkFailureAsync(source.SourceId, ex.Message, ct);
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task<Dictionary<string, JsonObject>> FetchActiveAsync(CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(options.CurrentValue.BaseUrl);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.CurrentValue.Token);
        var doc = await http.GetFromJsonAsync<JsonObject>("v1/alerts/active.json", ct)
                  ?? throw new InvalidOperationException("Empty response");
        var result = new Dictionary<string, JsonObject>();
        foreach (var node in doc["alerts"]?.AsArray() ?? [])
        {
            if (node is JsonObject alert && alert["id"] is not null)
            {
                result[alert["id"]!.ToString()] = alert;
            }
        }
        return result;
    }

    private async Task<Dictionary<string, JsonObject>> ReconcileAsync(Source source, Dictionary<string, JsonObject> known,
        Dictionary<string, JsonObject> active, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        foreach (var (id, alert) in active)
        {
            if (known.ContainsKey(id))
            {
                continue;
            }
            var startedAt = ParseTime(alert["started_at"]) ?? now;
            await ingestor.IngestAsync(new IncomingMessage
            {
                SourceId = source.SourceId,
                SourceMessageId = $"{id}:start",
                PublishedAt = startedAt,
                RawPayload = Wrap("alert.started", alert, startedAt),
                Url = "https://alerts.in.ua/",
            }, source.Code, ct);
        }
        foreach (var (id, alert) in known)
        {
            if (active.ContainsKey(id))
            {
                continue;
            }
            await ingestor.IngestAsync(new IncomingMessage
            {
                SourceId = source.SourceId,
                SourceMessageId = $"{id}:end",
                PublishedAt = now,
                RawPayload = Wrap("alert.finished", alert, now),
                Url = "https://alerts.in.ua/",
            }, source.Code, ct);
        }

        var cursor = new JsonObject { ["active"] = new JsonArray(active.Values.Select(a => (JsonNode)a.DeepClone()).ToArray()) };
        await states.MarkSuccessAsync(source.SourceId, null, active.Count == 0 ? null : now,
            JsonDocument.Parse(cursor.ToJsonString()), ct);
        return active;
    }

    private async Task<Dictionary<string, JsonObject>> LoadKnownAsync(int sourceId, CancellationToken ct)
    {
        var state = await states.GetAsync(sourceId, ct);
        var result = new Dictionary<string, JsonObject>();
        if (state.Cursor is null || !state.Cursor.RootElement.TryGetProperty("active", out var arr))
        {
            return result;
        }
        foreach (var el in arr.EnumerateArray())
        {
            if (JsonNode.Parse(el.GetRawText()) is JsonObject alert && alert["id"] is not null)
            {
                result[alert["id"]!.ToString()] = alert;
            }
        }
        return result;
    }

    private static JsonDocument Wrap(string kind, JsonObject alert, DateTimeOffset at)
    {
        var payload = new JsonObject
        {
            ["kind"] = kind,
            ["at"] = at.ToString("O"),
            ["alert"] = alert.DeepClone(),
        };
        return JsonDocument.Parse(payload.ToJsonString());
    }

    private static DateTimeOffset? ParseTime(JsonNode? node) =>
        node is not null && DateTimeOffset.TryParse(node.ToString(), null, System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
