using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Puluj.Api.Hubs;
using Puluj.Api.Services;
using Puluj.Contracts;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Ingestion;
using Puluj.Infrastructure.Persistence;
using Puluj.Infrastructure.Seeding;

namespace Puluj.Api;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapPulujEndpoints(this IEndpointRouteBuilder app, bool isDevelopment)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/version", () => new { version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0" });

        // Live state or the state at a moment in the past (spec §20). Same shape for both.
        api.MapGet("/snapshot", async (DateTimeOffset? at, bool? activeOnly, SnapshotService snapshots, CancellationToken ct) =>
            at is null
                ? await snapshots.LiveAsync(activeOnly ?? true, ct)
                : await snapshots.AtAsync(at.Value, activeOnly ?? true, ct));

        api.MapGet("/tracks/{id:long}", async Task<Results<Ok<TrackDetailsDto>, NotFound>> (long id, SnapshotService snapshots, CancellationToken ct) =>
            await snapshots.TrackDetailsAsync(id, ct) is { } details ? TypedResults.Ok(details) : TypedResults.NotFound());

        // Feed: newest observations for the side panel (default last 6 h). `until` bounds a replay window.
        api.MapGet("/observations", async (DateTimeOffset? since, DateTimeOffset? until, int? limit, SnapshotService snapshots, TimeProvider clock, CancellationToken ct) =>
            await snapshots.RecentObservationsAsync(since ?? clock.GetUtcNow().AddHours(-6), until, limit ?? 300, ct));

        api.MapGet("/timeline", async (DateTimeOffset? from, DateTimeOffset? to, int? bucketMinutes, SnapshotService snapshots, TimeProvider clock, CancellationToken ct) =>
        {
            var end = to ?? clock.GetUtcNow();
            var start = from ?? end.AddHours(-6);
            return await snapshots.TimelineAsync(start, end, bucketMinutes ?? 15, ct);
        });

        api.MapGet("/taxonomy", (ReferenceCache refs) => refs.Taxonomy);

        api.MapGet("/sources", (ReferenceCache refs, DtoMapper mapper) =>
            refs.Sources.Values.OrderByDescending(s => s.Priority).Select(mapper.Source));

        // Place search for "my location" (spec §16). Returns centroids only; nothing about the user is stored.
        api.MapGet("/places/search", (string q, int? limit, ReferenceCache refs, DtoMapper mapper) =>
        {
            var query = NameVariantGenerator.Normalize(q ?? "");
            if (query.Length < 2)
            {
                return Array.Empty<PlaceDto>();
            }
            return refs.Places.Values
                .Where(p => p.Level >= PlaceLevel.City || p.Level == PlaceLevel.Region)
                .Where(p => NameVariantGenerator.Normalize(p.Name).StartsWith(query, StringComparison.Ordinal))
                .OrderBy(p => p.Level == PlaceLevel.Region ? 1 : 0)
                .ThenByDescending(p => p.Population)
                .Take(Math.Clamp(limit ?? 10, 1, 50))
                .Select(mapper.Place)
                .ToArray();
        });

        api.MapGet("/places/{id:int}", (int id, ReferenceCache refs, DtoMapper mapper) =>
            refs.Place(id) is { } p ? Results.Ok(mapper.Place(p)) : Results.NotFound());

        // Polygons for regions/named areas: drawn under alerts and behind region-level markers. Cached by the client.
        api.MapGet("/places/regions", async (IDbContextFactory<PulujDbContext> factory, HttpContext http, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var rows = await db.Places.AsNoTracking()
                .Where(p => p.Level == PlaceLevel.Region || p.Level == PlaceLevel.NamedArea || p.Level == PlaceLevel.Country || (p.Level == PlaceLevel.City && p.ParentId == null)
                    || (p.Level == PlaceLevel.District && p.Parent != null && p.Parent.Level == PlaceLevel.City && p.Parent.ParentId == null))
                .Select(p => new RegionDto(p.PlaceId, p.Name, p.Level.ToString(), p.CountryCode, p.ParentId, p.Geometry))
                .ToListAsync(ct);
            http.Response.Headers.CacheControl = "public, max-age=3600";
            return rows;
        });

        api.MapGet("/places/{id:int}/geometry", async (int id, IDbContextFactory<PulujDbContext> factory, HttpContext http, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var geometry = await db.Places.AsNoTracking().Where(p => p.PlaceId == id).Select(p => p.Geometry).FirstOrDefaultAsync(ct);
            if (geometry is null)
            {
                return Results.NotFound();
            }
            http.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Ok(geometry);
        });

        if (isDevelopment)
        {
            // Local testing without real sources: inject a message as if a collector had received it.
            api.MapPost("/dev/ingest", async (IngestRequest req, ReferenceCache refs, RawMessageIngestor ingestor, TimeProvider clock, CancellationToken ct) =>
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
                    RawPayload = req.Payload is { ValueKind: System.Text.Json.JsonValueKind.Object } p
                        ? System.Text.Json.JsonDocument.Parse(p.GetRawText())
                        : System.Text.Json.JsonDocument.Parse("{\"kind\":\"dev.ingest\"}"),
                    Url = null,
                }, source.Code, ct);
                return Results.Ok(result);
            });
        }

        app.MapAdminEndpoints();
        app.MapHub<MapHub>("/hubs/map");
        return app;
    }
}
