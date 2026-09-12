using System.Diagnostics.Metrics;

namespace Puluj.Infrastructure;

/// <summary>Application metrics (spec §29: source latency monitoring, parser error logging).</summary>
public sealed class PulujMetrics
{
    public const string MeterName = "Puluj";

    private readonly Counter<long> _rawReceived;
    private readonly Counter<long> _observationsCreated;
    private readonly Counter<long> _parserUnmatched;
    private readonly Counter<long> _processingErrors;
    private readonly Histogram<double> _sourceLatency;
    private readonly Counter<long> _llmCalls;

    public PulujMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _rawReceived = meter.CreateCounter<long>("puluj.rawmessages.received");
        _observationsCreated = meter.CreateCounter<long>("puluj.observations.created");
        _parserUnmatched = meter.CreateCounter<long>("puluj.parser.unmatched");
        _processingErrors = meter.CreateCounter<long>("puluj.processing.errors");
        _sourceLatency = meter.CreateHistogram<double>("puluj.source.latency", unit: "s", description: "ReceivedAt - PublishedAt");
        _llmCalls = meter.CreateCounter<long>("puluj.llm.calls");
    }

    public void RawReceived(string source, TimeSpan latency)
    {
        var tag = new KeyValuePair<string, object?>("source", source);
        _rawReceived.Add(1, tag);
        _sourceLatency.Record(latency.TotalSeconds, tag);
    }

    public void ObservationCreated(string source, string method) =>
        _observationsCreated.Add(1, new("source", source), new("method", method));

    public void ParserUnmatched(string source) => _parserUnmatched.Add(1, new KeyValuePair<string, object?>("source", source));

    public void ProcessingError(string stage) => _processingErrors.Add(1, new KeyValuePair<string, object?>("stage", stage));

    public void LlmCall(string outcome) => _llmCalls.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
