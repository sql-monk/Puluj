using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Puluj.Admin.Docker;
using Puluj.Api.Services;
using Puluj.Domain.Entities;
using Puluj.Infrastructure.Ingestion;
using Puluj.Contracts;
using Puluj.Infrastructure.Persistence;
using Puluj.Infrastructure.Settings;

namespace Puluj.Admin;

/// <summary>
/// Operations view of the system for the admin panel: which service is alive, what every instance reports about
/// itself, what each collector does, how the pipeline keeps up, the containers of the compose stack, what the database
/// holds, and the tail of every service's log. Statistics come from SQL, statuses from `app_settings`, containers from
/// the docker CLI (<see cref="DockerService"/>); the only writes are the container actions and the reprocess request.
/// </summary>
public static class OpsEndpoints
{
    private static readonly TimeSpan WorkerStale = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan WorkerForgotten = TimeSpan.FromMinutes(15); // a killed dev process leaves its key behind

    /// <summary>The Worker writes its status document in camelCase (the web JSON options); enums as strings.</summary>
    private static readonly JsonSerializerOptions StatusJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public static IEndpointRouteBuilder MapOpsEndpoints(this IEndpointRouteBuilder app)
    {
        var ops = app.MapGroup("/api/admin").AddEndpointFilter(AdminEndpoints.AuthorizeAsync);

        ops.MapGet("/ops/overview", OverviewAsync);
        ops.MapGet("/ops/workers", WorkersAsync);
        ops.MapGet("/ops/collectors", CollectorsAsync);
        ops.MapGet("/ops/pipeline", PipelineAsync);
        ops.MapGet("/ops/db", DbAsync);

        // Containers of the compose stack (docs/plan-admin-ops.md §2.3): list, restart / stop / start, scale the processors.
        ops.MapGet("/ops/containers", async (DockerService docker, CancellationToken ct) => Results.Ok(await docker.ListAsync(ct)));
        ops.MapPost("/ops/containers/{id}/{action:regex(^(restart|stop|start)$)}", async (string id, string action, HttpContext http, DockerService docker, CancellationToken ct) =>
        {
            var outcome = await docker.ActAsync(id, action, http.Connection.RemoteIpAddress?.ToString(), ct);
            return Results.Json(outcome.Result, statusCode: outcome.StatusCode);
        });
        ops.MapPost("/ops/processors/scale", async (ScaleRequest req, HttpContext http, DockerService docker, CancellationToken ct) =>
        {
            var outcome = await docker.ScaleAsync(req.Replicas, http.Connection.RemoteIpAddress?.ToString(), ct);
            var containers = outcome.StatusCode is 200 or 502 ? await docker.ListAsync(ct, fresh: true) : null;
            return Results.Json(new { result = outcome.Result, containers }, statusCode: outcome.StatusCode);
        });

        // Earned rating of the sources (originality, who copies whom, groups) with per-day history.
        ops.MapGet("/sources/rating", async (int? days, SnapshotService snapshots, CancellationToken ct) =>
            await snapshots.SourceRatingAsync(days ?? 14, ct));

        ops.MapGet("/logs/files", (LogReader logs) => Results.Ok(logs.Files()));
        ops.MapGet("/logs", (string file, int? lines, string? filter, string? level, LogReader logs) =>
        {
            try
            {
                return Results.Ok(logs.Tail(file, lines ?? 200, filter, level));
            }
            catch (ArgumentException e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // Rebuild everything derived from the raw messages (after a parser / linker change): the Worker re-runs the
        // pipeline over all of them in publication order. Refused while a history load holds processing.
        ops.MapPost("/ops/reprocess", async (ReprocessService reprocess, CancellationToken ct) =>
        {
            if (await reprocess.PausedAsync(ct) is { } paused)
            {
                return Results.Conflict(new { error = $"Обробку призупинено: {paused}" });
            }
            var queued = await reprocess.ResetAsync(ct);
            return Results.Ok(new { queued });
        });

        // Testing without real sources: inject a message as if a collector had received it.
        ops.MapPost("/dev/ingest", async (IngestRequest req, ReferenceCache refs, RawMessageIngestor ingestor, TimeProvider clock, CancellationToken ct) =>
        {
            var source = refs.Sources.Values.FirstOrDefault(s => s.Code == req.SourceCode);
            if (source is null)
            {
                return Results.BadRequest(new { error = $"unknown source '{req.SourceCode}'" });
            }
            var result = await ingestor.IngestAsync(new IncomingMessage
            {
                SourceId = source.SourceId,
                SourceMessageId = req.SourceMessageId ?? $"dev-{Guid.NewGuid():N}",
                PublishedAt = req.PublishedAt ?? clock.GetUtcNow(),
                RawText = string.IsNullOrWhiteSpace(req.Text) ? null : req.Text,
                RawPayload = req.Payload is { ValueKind: JsonValueKind.Object } p
                    ? JsonDocument.Parse(p.GetRawText())
                    : JsonDocument.Parse("{\"kind\":\"dev.ingest\"}"),
                Url = null,
            }, source.Code, ct);
            return Results.Ok(result);
        });

        return app;
    }

    private static async Task<IResult> OverviewAsync(
        SettingsStore settings, IDbContextFactory<PulujDbContext> factory, IHttpClientFactory http, IConfiguration config, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var services = new List<ServiceStatusDto>();

        // Worker instances: each writes its own heartbeat into app_settings every 30 s (one container per role in Docker).
        var all = await settings.GetAllAsync(ct);
        var workers = WorkerHeartbeats(all, now);
        if (workers.Count == 0)
        {
            services.Add(new ServiceStatusDto("worker", "unknown", "heartbeat ще не записано", null));
        }
        var processors = 0;
        foreach (var (name, heartbeat) in workers)
        {
            var alive = now - heartbeat < WorkerStale;
            if (alive && DockerContainers.KindOf(name) == "processor")
            {
                processors++;
            }
            services.Add(new ServiceStatusDto($"worker:{name}", alive ? "ok" : "down",
                alive ? "heartbeat свіжий" : $"heartbeat застарів на {(int)(now - heartbeat).TotalMinutes} хв", heartbeat));
        }

        // Api: its own health endpoint over HTTP.
        var apiUrl = (config["Admin:ApiUrl"] ?? "http://localhost:5257").TrimEnd('/');
        try
        {
            var client = http.CreateClient("api-probe");
            client.Timeout = TimeSpan.FromSeconds(5);
            using var res = await client.GetAsync($"{apiUrl}/api/health", ct);
            services.Add(new ServiceStatusDto("api", res.IsSuccessStatusCode ? "ok" : "warn", $"{apiUrl}/api/health → {(int)res.StatusCode}", now));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            services.Add(new ServiceStatusDto("api", "down", $"{apiUrl}: {e.Message}", null));
        }

        // Collectors: the aggregate of the enabled sources.
        await using var db = await factory.CreateDbContextAsync(ct);
        var collectors = await db.CollectorStates.AsNoTracking().Include(c => c.Source).Where(c => c.Source!.Enabled).ToListAsync(ct);
        var failing = collectors.Count(c => c.ConsecutiveFailures > 0);
        var lastSuccess = collectors.Max(c => c.LastSuccessAt);
        services.Add(new ServiceStatusDto("collectors",
            collectors.Count == 0 ? "unknown" : failing == 0 ? "ok" : failing == collectors.Count ? "down" : "warn",
            $"{collectors.Count} увімкнених, {failing} з помилками", lastSuccess));

        // Telegram session (the worker keeps its status in app_settings).
        var tgStatus = all.TryGetValue("Runtime:Telegram:Status", out var ts) ? ts.Value : null;
        services.Add(new ServiceStatusDto("telegram", tgStatus is null ? "unknown" : tgStatus.Contains("ok", StringComparison.OrdinalIgnoreCase) || tgStatus.Contains("connected", StringComparison.OrdinalIgnoreCase) || tgStatus.StartsWith("listening", StringComparison.OrdinalIgnoreCase) ? "ok" : "warn", tgStatus, null));

        // Database.
        var version = (await db.Database.SqlQueryRaw<string>("SELECT version() AS \"Value\"").ToListAsync(ct)).FirstOrDefault() ?? "?";
        var size = (await db.Database.SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"").ToListAsync(ct)).FirstOrDefault();
        var connections = (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database()").ToListAsync(ct)).FirstOrDefault();
        var migrations = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        services.Add(new ServiceStatusDto("postgres", "ok", version.Split(' ', 3) is { Length: >= 2 } v ? $"{v[0]} {v[1]}" : version, now));
        services.Add(new ServiceStatusDto("admin", "ok", "ця панель", now));

        return Results.Ok(new OpsOverviewDto(now, services, new DbOverviewDto(version, size, connections, migrations.LastOrDefault(), migrations.Count), processors));
    }

    /// <summary>
    /// Heartbeats of the Worker instances (`Runtime:Worker:{name}:Heartbeat`), oldest first. An instance removes its key on a clean
    /// shutdown; one that died leaves a stale value, which is shown as down for a while and then forgotten.
    /// </summary>
    public static List<(string Name, DateTimeOffset At)> WorkerHeartbeats(IReadOnlyDictionary<string, AppSetting> all, DateTimeOffset now)
    {
        var result = new List<(string, DateTimeOffset)>();
        foreach (var (key, setting) in all)
        {
            const string prefix = "Runtime:Worker:", suffix = ":Heartbeat";
            // The pre-roles key `Runtime:Worker:Heartbeat` (no instance name) is skipped, not parsed.
            if (key.Length <= prefix.Length + suffix.Length
                || !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var name = key[prefix.Length..^suffix.Length];
            if (DateTimeOffset.TryParse(setting.Value, out var at) && now - at < WorkerForgotten)
            {
                result.Add((name, at));
            }
        }
        return result.OrderBy(w => w.Item2).ToList();
    }

    /// <summary>The instance's own status document (`Runtime:Worker:{name}:Status`, §2.1); null when absent or unreadable.</summary>
    public static WorkerStatusDto? WorkerStatus(IReadOnlyDictionary<string, AppSetting> all, string name)
    {
        var key = $"Runtime:Worker:{name}:Status";
        var value = all.TryGetValue(key, out var exact) ? exact.Value
            : all.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<WorkerStatusDto>(value, StatusJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ClaimRow(string ClaimedBy, long ProcessedDay, long InProgress);

    /// <summary>Every instance with a heartbeat: its status document, its share of the work (by claimed_by) and its container.</summary>
    private static async Task<IResult> WorkersAsync(SettingsStore settings, IDbContextFactory<PulujDbContext> factory, DockerService docker, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var all = await settings.GetAllAsync(ct);
        var heartbeats = WorkerHeartbeats(all, now);
        await using var db = await factory.CreateDbContextAsync(ct);
        var claims = (await db.Database.SqlQueryRaw<ClaimRow>("""
            SELECT claimed_by AS claimed_by,
                   count(*) FILTER (WHERE processed_at >= now() - interval '24 hours' AND processing_status = 1) AS processed_day,
                   count(*) FILTER (WHERE processing_status = 4) AS in_progress
            FROM raw_messages
            WHERE claimed_by IS NOT NULL AND (processed_at >= now() - interval '24 hours' OR processing_status = 4)
            GROUP BY 1
            """).ToListAsync(ct)).ToDictionary(c => c.ClaimedBy, StringComparer.OrdinalIgnoreCase);
        var containers = docker.Enabled ? (await docker.ListAsync(ct)).Containers : [];

        var list = heartbeats.Select(h =>
        {
            var kind = DockerContainers.KindOf(h.Name);
            var claim = claims.GetValueOrDefault(h.Name);
            var container = DockerContainers.Match(h.Name, kind, containers);
            return new WorkerInstanceDto(h.Name, kind, now - h.At < WorkerStale, h.At, WorkerStatus(all, h.Name),
                claim?.ProcessedDay ?? 0, claim?.InProgress ?? 0,
                container?.Id, container?.Name, container?.State, container?.CpuPercent, container?.MemoryBytes);
        })
        .OrderBy(w => KindOrder(w.Kind)).ThenBy(w => w.Name, StringComparer.Ordinal)
        .ToList();
        return Results.Ok(list);
    }

    private static int KindOrder(string kind) => kind switch
    {
        "processor" => 0,
        "collector-telegram" => 1,
        "collector-alerts" => 2,
        "analytics" => 3,
        "worker" => 4,
        _ => 5,
    };

    private static async Task<IResult> PipelineAsync(int? hours, IDbContextFactory<PulujDbContext> factory, TimeProvider clock, CancellationToken ct)
    {
        var h = hours ?? 24;
        if (!PipelineBuckets.AllowedHours.Contains(h))
        {
            return Results.BadRequest(new { error = $"hours має бути одним із: {string.Join(", ", PipelineBuckets.AllowedHours)}" });
        }
        await using var db = await factory.CreateDbContextAsync(ct);
        return Results.Ok(await PipelineReport.BuildAsync(db, h, clock.GetUtcNow(), ct));
    }

    private sealed record HourCount(int SourceId, DateTime Hour, int Count);

    private static async Task<IResult> CollectorsAsync(IDbContextFactory<PulujDbContext> factory, TimeProvider clock, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        var firstHour = new DateTime(now.UtcDateTime.Year, now.UtcDateTime.Month, now.UtcDateTime.Day, now.UtcDateTime.Hour, 0, 0, DateTimeKind.Utc).AddHours(-23);
        var sources = await db.Sources.AsNoTracking().OrderByDescending(s => s.Enabled).ThenByDescending(s => s.Priority).ToListAsync(ct);
        var states = await db.CollectorStates.AsNoTracking().ToDictionaryAsync(c => c.SourceId, ct);
        var counts = await db.Database.SqlQuery<HourCount>($"""
            SELECT source_id, date_trunc('hour', received_at)::timestamp AS hour, count(*)::int AS count
            FROM raw_messages
            WHERE received_at >= {firstHour}
            GROUP BY 1, 2
            """).ToListAsync(ct);
        var byId = counts.ToLookup(c => c.SourceId);

        var list = sources.Select(s =>
        {
            states.TryGetValue(s.SourceId, out var st);
            var perHour = new int[24];
            foreach (var c in byId[s.SourceId])
            {
                var i = (int)Math.Floor((DateTime.SpecifyKind(c.Hour, DateTimeKind.Utc) - firstHour).TotalHours);
                if (i is >= 0 and < 24)
                {
                    perHour[i] += c.Count;
                }
            }
            return new CollectorStatusDto(s.SourceId, s.Code, s.Name, s.Type.ToString(), s.Enabled,
                st?.LastPolledAt, st?.LastSuccessAt, st?.LastMessageAt, st?.LastError, st?.ConsecutiveFailures ?? 0,
                perHour.Sum(), perHour);
        }).ToList();
        return Results.Ok(list);
    }

    private sealed record TableRow(string Name, long Rows, long Bytes);
    private sealed record RoleRow(string Role, int Connections);

    private static async Task<IResult> DbAsync(IDbContextFactory<PulujDbContext> factory, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var version = (await db.Database.SqlQueryRaw<string>("SELECT version() AS \"Value\"").ToListAsync(ct)).FirstOrDefault() ?? "?";
        var size = (await db.Database.SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"").ToListAsync(ct)).FirstOrDefault();
        var tables = await db.Database.SqlQueryRaw<TableRow>("""
            SELECT relname AS name, n_live_tup AS rows, pg_total_relation_size(relid) AS bytes
            FROM pg_stat_user_tables WHERE schemaname = 'public' ORDER BY bytes DESC
            """).ToListAsync(ct);
        var roles = await db.Database.SqlQueryRaw<RoleRow>("""
            SELECT coalesce(usename, '?') AS role, count(*)::int AS connections
            FROM pg_stat_activity WHERE datname = current_database() GROUP BY 1 ORDER BY 2 DESC
            """).ToListAsync(ct);
        var migrations = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        return Results.Ok(new DbReportDto(version, size,
            tables.Select(t => new DbTableDto(t.Name, t.Rows, t.Bytes)).ToList(),
            migrations,
            roles.Select(r => new DbRoleConnectionsDto(r.Role, r.Connections)).ToList()));
    }
}
