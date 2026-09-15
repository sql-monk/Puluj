using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Puluj.Domain.Enums;
using Puluj.Infrastructure;
using Puluj.Processing.Llm;
using Puluj.Processing.Parsing;
using Puluj.Processing.Tests.Support;
using Puluj.Processing.Text;

namespace Puluj.Processing.Tests.Llm;

public class LlmParserTests
{
    private static LlmParser Create()
    {
        var indexes = new StaticIndexes();
        var normalizer = new Normalizer();
        return new LlmParser(new RuleParser(indexes), indexes, normalizer, new StaticMonitor(new LlmOptions { Enabled = false }),
            new PulujMetrics(new TestMeterFactory()), TimeProvider.System, NullLogger<LlmParser>.Instance);
    }

    [Fact]
    public void Maps_model_answer_to_facts_using_taxonomy_and_gazetteer()
    {
        var parser = Create();
        var message = new Normalizer().Normalize("Об'єкт летить над Полтавщиною на захід, може шахед.");
        const string json = """
            {"facts":[{"eventType":"TargetObserved","target":{"level":"family","code":"SHAHED"},"hedged":true,"count":null,"countApprox":false,
              "places":[{"name":"Полтавська область","role":"current"},{"name":"Атлантида","role":"destination"}],"directionDeg":270,"launch":false,"segment":0,"quote":null}]}
            """;
        var facts = parser.MapJson(json, message);
        var f = Assert.Single(facts);
        Assert.Equal(IdentificationMethod.Llm, f.Method);
        Assert.Equal("SHAHED", f.Target!.Ref.Code);
        Assert.True(f.Target.Hedged);
        Assert.Equal(ConfidenceLevel.Low, f.Target.EffectiveConfidence);
        Assert.Equal("Полтавська область", Assert.Single(f.Places).Place.Name); // unknown "Атлантида" dropped, never invented
        Assert.Equal(270, f.Direction!.Degrees);
    }

    [Fact]
    public void Ignores_unknown_codes_and_empty_answers()
    {
        var parser = Create();
        var message = new Normalizer().Normalize("test");
        Assert.Empty(parser.MapJson("""{"facts":[{"eventType":"TargetObserved","target":{"level":"model","code":"MADE_UP"},"hedged":false,"count":null,"countApprox":false,"places":[],"directionDeg":null,"launch":false,"segment":0,"quote":null}]}""", message));
        Assert.Empty(parser.MapJson("""{"facts":[]}""", message));
    }

    [Fact]
    public async Task Falls_back_to_rules_when_disabled()
    {
        var parser = Create();
        var message = new Normalizer().Normalize("Шахеди на Сумщині.");
        var facts = await parser.ParseAsync(message, new ParseContext(1, "uk", null), CancellationToken.None);
        Assert.Equal(IdentificationMethod.Rule, Assert.Single(facts).Method);
    }

    private sealed class StaticMonitor(LlmOptions value) : IOptionsMonitor<LlmOptions>
    {
        public LlmOptions CurrentValue => value;
        public LlmOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<LlmOptions, string?> listener) => null;
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);

        public void Dispose()
        {
        }
    }
}
