using Puluj.Domain.Enums;
using Puluj.Processing.Indexes;
using Puluj.Processing.Text;

namespace Puluj.Processing.Parsing;

/// <summary>
/// Deterministic parser: alias dictionary + gazetteer + a handful of phrase rules.
/// One segment produces one fact per distinct target mention; list-style messages inherit the target from a header line.
/// </summary>
public sealed class RuleParser(IIndexes indexes) : IParser
{
    public const string Version = "rule-0.1";

    public Task<IReadOnlyList<ParsedFact>> ParseAsync(NormalizedMessage message, ParseContext ctx, CancellationToken ct) => Task.FromResult(Parse(message, ctx));

    public IReadOnlyList<ParsedFact> Parse(NormalizedMessage message, ParseContext ctx)
    {
        var taxonomy = indexes.Taxonomy;
        var gazetteer = indexes.Gazetteer;
        var targetMatcher = new TargetMatcher(taxonomy);
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
        int? sectionRegion = null;            // "Сумщина:" — the lines below are about that oblast
        TargetMention? headerTarget = null;   // "Шахеди:" — applies to every following line
        TargetMention? carryTarget = null;    // "Балістика!" followed by "Дніпро — в укриття!" — applies to the next line only
        int? carryFactIndex = null;
        foreach (var segment in message.Segments)
        {
            var rules = new List<string>();
            var targets = Collapse(targetMatcher.Match(segment, ctx));
            var reserved = targets.Select(t => (t.TokenIndex, t.TokenCount)).ToHashSet();
            var places = placeMatcher.Match(segment, ctx, contextRegions, reserved, sectionRegion);
            // A line that is only an oblast name ("Сумщина:", "Чернігівщина") heads a section: it locates the lines
            // below it, and it is not a sighting by itself.
            if (targets.Count == 0 && places.Count == 1 && (places[0].Place.Level is PlaceLevel.Region or PlaceLevel.NamedArea || places[0].Place is { Level: PlaceLevel.City, ParentId: null })
                && (segment.Text.TrimEnd().EndsWith(':') || segment.Tokens.Count <= 2))
            {
                sectionRegion = places[0].Place.PlaceId;
                continue;
            }
            // Under a section header a sighting that names no place we know is at least somewhere in that oblast.
            var sectionRules = new List<string>();
            if (sectionRegion is int sr && places.Count == 0 && targets.Count > 0 && gazetteer.Get(sr) is { } sectionPlace)
            {
                places = [new PlaceMention(sectionPlace, PlaceRole.Current, sectionPlace.Name, 0, 0, 0)];
                sectionRules.Add("section_region");
            }
            var direction = DirectionExtractor.Extract(segment);
            var (eventType, eventRule, launch) = EventTypeMatcher.Match(segment, targets.Count > 0);
            var alertLevel = AlertLevelExtractor.Extract(segment);
            if (eventRule is not null)
            {
                rules.Add(eventRule);
            }
            rules.AddRange(sectionRules);

            var isHeader = segment.Text.TrimEnd().EndsWith(':');
            if (targets.Count > 0 && isHeader && places.Count == 0)
            {
                headerTarget = targets[0];
                rules.Add("header_target");
                if (eventType is EventType.TargetObserved or EventType.Unknown)
                {
                    continue; // "Шахеди:" — facts come from the lines below
                }
            }
            if (targets.Count == 0 && (places.Count > 0 || direction is not null) && eventType is EventType.Unknown or EventType.TargetObserved)
            {
                if (carryTarget is not null)
                {
                    targets = [carryTarget];
                    eventType = EventType.TargetObserved;
                    rules.Add("inherit_previous_target");
                }
                else if (headerTarget is not null)
                {
                    targets = [headerTarget];
                    eventType = EventType.TargetObserved;
                    rules.Add("inherit_header_target");
                }
            }
            if (rules.Contains("inherit_previous_target") && carryFactIndex is int idx && idx < facts.Count)
            {
                facts.RemoveAt(idx); // the bare "Балістика!" line is superseded by the located line that follows it
            }
            carryTarget = targets.Count > 0 && places.Count == 0 && !rules.Contains("inherit_previous_target") ? targets[0] : null;
            carryFactIndex = carryTarget is null ? null : facts.Count;
            if (targets.Count == 0 && eventType == EventType.Unknown)
            {
                continue;
            }

            if (targets.Count == 0)
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

            foreach (var target in targets)
            {
                var (count, approx) = CountExtractor.Extract(segment, target.TokenIndex);
                var factRules = new List<string>(rules) { $"alias:{target.MatchedText}" };
                if (target.Hedged)
                {
                    factRules.Add("hedged");
                }
                facts.Add(new ParsedFact
                {
                    SegmentIndex = segment.Index,
                    SegmentText = segment.Text,
                    EventType = eventType == EventType.Unknown ? EventType.TargetObserved : eventType,
                    AlertLevel = alertLevel,
                    Target = target,
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
    private static IReadOnlyList<TargetMention> Collapse(IReadOnlyList<TargetMention> mentions)
    {
        var result = new List<TargetMention>();
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
