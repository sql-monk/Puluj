using Puluj.Api.Services;

namespace Puluj.Api;

public static class StatsEndpoints
{
    /// <summary>
    /// Statistics page: every chart of one period in a single payload. `from`/`to` optional (default: the last 24 h);
    /// the end is clamped to now, the span to StatsAggregator.MaxDays. Aggregates only — nothing about the viewer.
    /// </summary>
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/stats", async (DateTimeOffset? from, DateTimeOffset? to, StatsService stats, HttpContext http, CancellationToken ct) =>
        {
            if (from is { } f && to is { } t && t <= f)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["`to` must be after `from`."] });
            }
            var dto = await stats.GetAsync(from, to, ct);
            http.Response.Headers.CacheControl = "public, max-age=60";
            return Results.Ok(dto);
        });
        return api;
    }
}
