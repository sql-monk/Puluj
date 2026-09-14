using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Puluj.Api;
using Puluj.Infrastructure;
using Puluj.Infrastructure.Settings;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddPulujDatabaseSettings();

builder.Services.AddSerilog((sp, cfg) => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("app", "puluj-api"));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("puluj-api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddRuntimeInstrumentation().AddMeter(PulujMetrics.MeterName).AddOtlpExporter());

builder.Services.AddPulujInfrastructure(builder.Configuration);
builder.Services.AddPulujApi(builder.Configuration);
builder.Services.AddResponseCompression(o => o.MimeTypes = ["application/json", "application/geo+json", "text/plain"]);

var app = builder.Build();

app.UseResponseCompression();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseCors("dev");
    app.MapOpenApi();
}

app.MapHealthChecks("/api/health", new HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync,
});
app.MapPulujEndpoints(app.Environment.IsDevelopment());

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
