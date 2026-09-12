using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Puluj.Collectors;
using Puluj.Infrastructure;
using Puluj.Infrastructure.Settings;
using Puluj.Processing;
using Puluj.Worker.Hosting;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddPulujDatabaseSettings(); // values from the admin UI override files/env

builder.Services.AddSerilog((sp, cfg) => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("app", "puluj-worker"));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("puluj-worker"))
    .WithTracing(t => t.AddHttpClientInstrumentation().AddOtlpExporter())
    .WithMetrics(m => m.AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddMeter(PulujMetrics.MeterName).AddOtlpExporter());

builder.Services.AddPulujInfrastructure(builder.Configuration);
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddHostedService<WorkerHeartbeat>();
builder.Services.AddPulujProcessing(builder.Configuration);
builder.Services.AddPulujCollectors(builder.Configuration);

var host = builder.Build();
await host.RunAsync();
