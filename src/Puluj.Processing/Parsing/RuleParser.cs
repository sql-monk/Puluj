using Puluj.Domain.Enums;
using Puluj.Processing.Indexes;
using Puluj.Processing.Text;

namespace Puluj.Processing.Parsing;

/// <summary>
/// Deterministic parser: alias dictionary + gazetteer + a handful of phrase rules.
/// One segment produces one fact per distinct threat mention; list-style messages inherit the threat from a header line.
/// </summary>
public sealed class RuleParser(IIndexes indexes) : IParser
{
    public const string Version = "rule-0.1";

    public Task<IReadOnlyList<ParsedFact>> ParseAsync(NormalizedMessage message, ParseContext ctx, CancellationToken ct) => Task.FromResult(Parse(message, ctx));

    public IReadOnlyList<ParsedFact> Parse(NormalizedMessage message, ParseContext ctx)
    {
        var taxonomy = indexes.Taxonomy;
        var gazetteer = indexes.Gazetteer;
        var threatMatcher = new ThreatMatcher(taxonomy);
        var placeMatcher = new PlaceMatcher(gazetteer);

        // Pass 1: regions mentioned anywhere give context for ambiguous settlement names in pass 2.
        var contextRegions = new HashSet<int>();
        var noSpans = new HashSet<(int, int)>();
        foreach (var segment in message.Segments)
        {
            foreach (var p in placeMatcher.Match(segment, ctx, contextRegions, noSpans))
            {
                var region = gazetteer.RegionOf(p.Place);
                if (region is not null && p.Place.Level is PlaceLevel.Region or PlaceLevel.City or PlaceLevel.NamedArea)
                {
                    contextRegions.Add(region.PlaceId);
                }
            }
        }

        var facts = new List<ParsedFact>();
        ThreatMention? headerThreat = null;   // "Шахеди:" — applies to every following line
        ThreatMention? carryThreat = null;    // "Балістика!" followed by "Дніпро — в укриття!" — applies to the next line only
        int? carryFactIndex = null;
        foreach (var segment in message.Segments)
        {
            var rules = new List<string>();
            var threats = Collapse(threatMatcher.Match(segment, ctx));
            var reserved = threats.Select(t => (t.TokenIndex, t.TokenCount)).ToHashSet();
            var places = placeMatcher.Match(segment, ctx, contextRegions, reserved);
            var direction = DirectionExtractor.Extract(segment);
            var (eventType, eventRule, launch) = EventTypeMatcher.Match(segment, threats.Count > 0);
            var alertLevel = AlertLevelExtractor.Extract(segment);
            if (eventRule is not null)
            {
                rules.Add(eventRule);
            }

            var isHeader = segment.Text.TrimEnd().EndsWith(':');
            if (threats.Count > 0 && isHeader && places.Count == 0)
            {
                headerThreat = threats[0];
                rules.Add("header_threat");
                if (eventType is EventType.ThreatObserved or EventType.Unknown)
                {
                    continue; // "Шахеди:" — facts come from the lines below
                }
            }
            if (threats.Count == 0 && (places.Count > 0 || direction is not null) && eventType is EventType.Unknown or EventType.ThreatObserved)
            {
                if (carryThreat is not null)
                {
                    threats = [carryThreat];
                    eventType = EventType.ThreatObserved;
                    rules.Add("inherit_previous_threat");
                }
                else if (headerThreat is not null)
                {
                    threats = [headerThreat];
                    eventType = EventType.ThreatObserved;
                    rules.Add("inherit_header_threat");
                }
            }
            if (rules.Contains("inherit_previous_threat") && carryFactIndex is int idx && idx < facts.Count)
            {
                facts.RemoveAt(idx); // the bare "Балістика!" line is superseded by the located line that follows it
            }
            carryThreat = threats.Count > 0 && places.Count == 0 && !rules.Contains("inherit_previous_threat") ? threats[0] : null;
            carryFactIndex = carryThreat is null ? null : facts.Count;
            if (threats.Count == 0 && eventType == EventType.Unknown)
            {
                continue;
            }

            if (threats.Count == 0)
            {
                facts.Add(new ParsedFact
                {
                    SegmentIndex = segment.Index,
                    SegmentText = segment.Text,
                    EventType = eventType,
                    AlertLevel = alertLevel,
                    Places = places,
                    Direction = direction,
                    IsLaunch = launch,
                    Rules = rules,
                });
                continue;
            }

            foreach (var threat in threats)
            {
                var (count, approx) = CountExtractor.Extract(segment, threat.TokenIndex);
                var factRules = new List<string>(rules) { $"alias:{threat.MatchedText}" };
                if (threat.Hedged)
                {
                    factRules.Add("hedged");
                }
                facts.Add(new ParsedFact
                {
                    SegmentIndex = segment.Index,
                    SegmentText = segment.Text,
                    EventType = eventType == EventType.Unknown ? EventType.ThreatObserved : eventType,
                    AlertLevel = alertLevel,
                    Threat = threat,
                    Count = count,
                    CountIsApproximate = approx,
                    Places = places,
                    Direction = direction,
                    IsLaunch = launch,
                    Rules = factRules,
                });
            }
        }
        return facts;
    }

    /// <summary>Drops duplicate mentions ("🛵 ... шахеди") and generic ones covered by a more specific mention of the same category ("БпЛА ... шахеди").</summary>
    private static IReadOnlyList<ThreatMention> Collapse(IReadOnlyList<ThreatMention> mentions)
    {
        var result = new List<ThreatMention>();
        foreach (var m in mentions.OrderByDescending(x => (int)x.Ref.Level).ThenBy(x => x.TokenIndex))
        {
            var covered = result.Any(r => r.Ref.Code == m.Ref.Code
                || (r.Ref.CategoryId == m.Ref.CategoryId && (int)m.Ref.Level < (int)r.Ref.Level && (m.Ref.ClassId is null || m.Ref.ClassId == r.Ref.ClassId)));
            if (!covered)
            {
                result.Add(m);
            }
        }
        return result.OrderBy(r => r.TokenIndex).ToList();
    }
}
