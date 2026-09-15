using Puluj.Analytics.Persistence;
using Puluj.Analytics.Reporting;

namespace Puluj.Admin;

/// <summary>
/// The source analytics page: results of the analytics service (who copies whom, forwards, activity, track firsts)
/// and the state of the service itself. Read straight from the `analytics` schema the service maintains; `reset`
/// clears it so the service rebuilds everything on its next run.
/// </summary>
public static class AnalyticsEndpoints
{
    private const string NotInitialized = "Сервіс аналітики ще не створив свою схему (він ще не запускався).";

    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/analytics").AddEndpointFilter(AdminEndpoints.AuthorizeAsync);

        group.MapGet("/status", (AnalyticsReportService reports, CancellationToken ct) => reports.StatusAsync(ct));
        group.MapGet("/report", async (int? days, AnalyticsReportService reports, CancellationToken ct) =>
            await reports.ReportAsync(days ?? 14, ct) is { } report ? Results.Ok(report) : Results.NotFound(new { error = NotInitialized }));
        // kind: near | verbatim | forward; primaryOnly: only the earliest original of every copy.
        group.MapGet("/recent", async (int? limit, int? sourceId, string? kind, bool? primaryOnly, AnalyticsReportService reports, CancellationToken ct) =>
        {
            CopyKind? copyKind = null;
            if (!string.IsNullOrEmpty(kind))
            {
                if (!Enum.TryParse<CopyKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                {
                    return Results.BadRequest(new { error = "kind: near, verbatim або forward" });
                }
                copyKind = parsed;
            }
            return Results.Ok(await reports.RecentAsync(limit ?? 30, sourceId, copyKind, primaryOnly ?? false, ct));
        });
        group.MapGet("/pairs/{copierId:int}/{originalId:int}", async (int copierId, int originalId, int? days, AnalyticsReportService reports, CancellationToken ct) =>
            await reports.PairAsync(copierId, originalId, days ?? 14, ct) is { } pair ? Results.Ok(pair) : Results.NotFound(new { error = NotInitialized }));
        group.MapPost("/reset", async (AnalyticsReportService reports, CancellationToken ct) =>
        {
            await reports.ResetAsync(ct);
            return Results.Ok(new { ok = true });
        });
        return app;
    }
}
