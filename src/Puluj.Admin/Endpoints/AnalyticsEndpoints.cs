using Puluj.Analytics.Reporting;

namespace Puluj.Admin;

/// <summary>
/// The source analytics page: results of the analytics service (who copies whom, forwards, activity, track firsts)
/// and the state of the service itself. Read straight from the `analytics` schema the service maintains; `reset`
/// clears it so the service rebuilds everything on its next run.
/// </summary>
public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/analytics").AddEndpointFilter(AdminEndpoints.AuthorizeAsync);

        group.MapGet("/status", (AnalyticsReportService reports, CancellationToken ct) => reports.StatusAsync(ct));
        group.MapGet("/report", async (int? days, AnalyticsReportService reports, CancellationToken ct) =>
            await reports.ReportAsync(days ?? 14, ct) is { } report ? Results.Ok(report) : Results.NotFound(new { error = "Сервіс аналітики ще не створив свою схему (він ще не запускався)." }));
        group.MapGet("/recent", (int? limit, int? sourceId, AnalyticsReportService reports, CancellationToken ct) => reports.RecentAsync(limit ?? 30, sourceId, ct));
        group.MapPost("/reset", async (AnalyticsReportService reports, CancellationToken ct) =>
        {
            await reports.ResetAsync(ct);
            return Results.Ok(new { ok = true });
        });
        return app;
    }
}
