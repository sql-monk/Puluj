using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Puluj.Api;

public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static Task WriteAsync(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration,
            checks = report.Entries.ToDictionary(e => e.Key, e => new
            {
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration,
                data = e.Value.Data,
                error = e.Value.Exception?.Message,
            }),
        };
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(body, Options));
    }
}
