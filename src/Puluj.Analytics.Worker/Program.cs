using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Puluj.Analytics;
using Puluj.Analytics.Persistence;
using Puluj.Analytics.Reporting;
using Puluj.Analytics.Worker;
using Serilog;

// `--healthcheck`: probe the running instance and exit (the aspnet image has neither curl nor wget; compose runs this).
if (args.Contains("--healthcheck"))
{
    return await Healthcheck.RunAsync();
}

// The source analytics service: indexes raw_messages into the analytics schema (who copies whom, forwards, activity,
// who reports tracks first) and serves its own status. The admin panel reads the same schema directly.
var builder = WebApplication.CreateBuilder(args);
var appName = $"puluj-{builder.Configuration[$"{AnalyticsOptions.Section}:Name"] ?? "analytics"}";

builder.Services.AddSerilog((sp, cfg) => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("app", appName));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(appName))
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddOtlpExporter())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddRuntimeInstrumentation().AddMeter(AnalyticsMetrics.MeterName).AddOtlpExporter());

builder.Services.AddPulujAnalytics(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddProblemDetails();
builder.Services.AddHostedService<AnalyticsInitializer>(); // first: the loop needs the schema
builder.Services.AddSingleton<AnalysisLoop>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AnalysisLoop>());
builder.Services.AddHostedService<AnalyticsHeartbeat>();

var app = builder.Build();
app.UseExceptionHandler();

// Healthy while the loop is alive and the last runs did not fail twice in a row.
app.MapGet("/health", (AnalysisLoop loop) =>
{
    var healthy = loop.ConsecutiveFailures < 2;
    var body = new { status = healthy ? "ok" : "failing", lastRunAt = loop.LastRunAt, consecutiveFailures = loop.ConsecutiveFailures, lastError = loop.LastError };
    return healthy ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
});
app.MapGet("/api/analytics/status", (AnalyticsReportService reports, CancellationToken ct) => reports.StatusAsync(ct));
app.MapGet("/api/analytics/report", async (int? days, AnalyticsReportService reports, CancellationToken ct) =>
    await reports.ReportAsync(days ?? 14, ct) is { } report ? Results.Ok(report) : Results.NotFound(new { error = "analytics schema is not initialized" }));
// kind: near | verbatim | forward; primaryOnly: only the earliest original of every copy (same contract as the admin panel).
app.MapGet("/api/analytics/recent", async (int? limit, int? sourceId, string? kind, bool? primaryOnly, AnalyticsReportService reports, CancellationToken ct) =>
{
    CopyKind? copyKind = null;
    if (!string.IsNullOrEmpty(kind))
    {
        if (!Enum.TryParse<CopyKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            return Results.BadRequest(new { error = "kind: near, verbatim or forward" });
        }
        copyKind = parsed;
    }
    return Results.Ok(await reports.RecentAsync(limit ?? 30, sourceId, copyKind, primaryOnly ?? false, ct));
});

app.Services.GetRequiredService<ILogger<Program>>().LogInformation("{App} starting", appName);
await app.RunAsync();
return 0;

public partial class Program;
