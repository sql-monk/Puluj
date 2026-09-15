using System.Diagnostics.Metrics;
using Puluj.Analytics.Persistence;

namespace Puluj.Analytics;

/// <summary>OpenTelemetry metrics of the analytics service (meter `Puluj.Analytics`).</summary>
public sealed class AnalyticsMetrics
{
    public const string MeterName = "Puluj.Analytics";

    private readonly Counter<long> _indexed;
    private readonly Counter<long> _pairs;
    private readonly Histogram<double> _runDuration;

    /// <summary>Raw messages above the watermark after the last run.</summary>
    public long Backlog { get; set; }

    public AnalyticsMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _indexed = meter.CreateCounter<long>("puluj.analytics.messages.indexed");
        _pairs = meter.CreateCounter<long>("puluj.analytics.pairs.found");
        _runDuration = meter.CreateHistogram<double>("puluj.analytics.run.duration", unit: "s");
        meter.CreateObservableGauge("puluj.analytics.backlog", () => Backlog);
    }

    public void MessagesIndexed(int count) => _indexed.Add(count);

    public void PairFound(CopyKind kind) => _pairs.Add(1, new KeyValuePair<string, object?>("kind", kind.ToString().ToLowerInvariant()));

    public void RunFinished(TimeSpan elapsed) => _runDuration.Record(elapsed.TotalSeconds);
}
