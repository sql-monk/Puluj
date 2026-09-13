using System.Text;
using System.Text.Json;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Seeding;
using Puluj.Processing.Parsing;
using Puluj.Processing.Tests.Support;
using Puluj.Processing.Text;
using Xunit.Abstractions;

namespace Puluj.Processing.Tests.Parsing;

/// <summary>Golden tests: every message in data/corpus/cases.json must parse into exactly the listed facts.</summary>
public class CorpusTests(ITestOutputHelper output)
{
    private static readonly Lazy<CorpusFile> Corpus = new(() =>
    {
        using var stream = File.OpenRead(Path.Combine(TestIndexes.RepoRoot, "data/corpus/cases.json"));
        return JsonSerializer.Deserialize<CorpusFile>(stream, SeedFiles.Json)!;
    });

    public static IEnumerable<object[]> CaseIds() => Corpus.Value.Cases.Select(c => new object[] { c.Id });

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Parses_as_expected(string id)
    {
        var c = Corpus.Value.Cases.First(x => x.Id == id);
        var parser = new RuleParser(new StaticIndexes());
        var normalized = new Normalizer().Normalize(c.Text);
        var facts = parser.Parse(normalized, new ParseContext(1, normalized.Language, null));

        output.WriteLine(Describe(facts));
        Assert.True(facts.Count == c.Facts.Count, $"expected {c.Facts.Count} fact(s), got {facts.Count}:\n{Describe(facts)}");
        for (var i = 0; i < c.Facts.Count; i++)
        {
            AssertFact(c.Facts[i], facts[i], i);
        }
    }

    private static void AssertFact(ExpectedFact e, ParsedFact f, int i)
    {
        var where = $"fact #{i}: {Describe([f])}";
        if (e.Event is not null)
        {
            Assert.True(Enum.Parse<EventType>(e.Event) == f.EventType, $"event {e.Event} != {f.EventType}; {where}");
        }
        if (e.Target is not null)
        {
            Assert.True(f.Target is not null, $"target expected; {where}");
            Assert.True(e.Target == f.Target!.Ref.Code, $"target {e.Target} != {f.Target.Ref.Code}; {where}");
        }
        if (e.Level is not null)
        {
            Assert.True(Enum.Parse<AliasTargetLevel>(e.Level) == f.Target?.Ref.Level, $"level {e.Level} != {f.Target?.Ref.Level}; {where}");
        }
        if (e.Hedged is bool h)
        {
            Assert.True(h == (f.Target?.Hedged ?? false), $"hedged {h}; {where}");
        }
        if (e.Count is int n)
        {
            Assert.True(n == f.Count, $"count {n} != {f.Count}; {where}");
        }
        if (e.Approx is bool a)
        {
            Assert.True(a == f.CountIsApproximate, $"approx {a}; {where}");
        }
        if (e.Launch is bool l)
        {
            Assert.True(l == f.IsLaunch, $"launch {l}; {where}");
        }
        if (e.NoPlaces == true)
        {
            Assert.True(f.Places.Count == 0, $"no places expected; {where}");
        }
        AssertPlace(e.Current, f.Places.FirstOrDefault(p => p.Role == PlaceRole.Current), "current", where);
        AssertPlace(e.Origin, f.Places.FirstOrDefault(p => p.Role == PlaceRole.Origin), "origin", where);
        AssertPlace(e.Destination, f.Places.FirstOrDefault(p => p.Role == PlaceRole.Destination), "destination", where);
        AssertPlace(e.Transit, f.Places.FirstOrDefault(p => p.Role == PlaceRole.Transit), "transit", where);
        if (e.Quadrant is double q)
        {
            Assert.True(f.Current?.QuadrantDeg == q, $"quadrant {q} != {f.Current?.QuadrantDeg}; {where}");
        }
        if (e.Direction is double d)
        {
            Assert.True(f.Direction is not null, $"direction {d} expected; {where}");
            Assert.True(Math.Abs(f.Direction!.Degrees - d) < 1, $"direction {d} != {f.Direction.Degrees}; {where}");
        }
    }

    private static void AssertPlace(string? expected, PlaceMention? actual, string role, string where)
    {
        if (expected is null)
        {
            return;
        }
        Assert.True(actual is not null, $"{role} '{expected}' expected; {where}");
        Assert.True(expected == actual!.Place.Name, $"{role} '{expected}' != '{actual.Place.Name}'; {where}");
    }

    private static string Describe(IReadOnlyList<ParsedFact> facts)
    {
        var sb = new StringBuilder();
        foreach (var f in facts)
        {
            sb.Append($"[{f.EventType}] ");
            if (f.Target is { } t)
            {
                sb.Append($"target={t.Ref.Code}/{t.Ref.Level}('{t.MatchedText}'{(t.Hedged ? ", hedged" : "")}) ");
            }
            if (f.Count is int n)
            {
                sb.Append($"count={n}{(f.CountIsApproximate ? "~" : "")} ");
            }
            foreach (var p in f.Places)
            {
                sb.Append($"{p.Role}={p.Place.Name}('{p.MatchedText}') ");
            }
            if (f.Direction is { } d)
            {
                sb.Append($"dir={d.Degrees}({d.Text}) ");
            }
            if (f.IsLaunch)
            {
                sb.Append("launch ");
            }
            sb.Append($"rules=[{string.Join(',', f.Rules)}]");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private sealed record CorpusFile(List<Case> Cases);
    private sealed record Case(string Id, string Text, List<ExpectedFact> Facts);
    private sealed record ExpectedFact(string? Event, string? Target, string? Level, bool? Hedged, int? Count, bool? Approx, bool? Launch,
        bool? NoPlaces, string? Current, string? Origin, string? Destination, string? Transit, double? Direction, double? Quadrant);
}
