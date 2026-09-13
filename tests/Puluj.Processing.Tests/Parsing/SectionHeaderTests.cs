using Puluj.Domain.Enums;
using Puluj.Processing.Parsing;
using Puluj.Processing.Tests.Support;
using Puluj.Processing.Text;

namespace Puluj.Processing.Tests.Parsing;

/// <summary>Monitoring channels post lists grouped under oblast headers: every line is its own object, located by its header.</summary>
public class SectionHeaderTests
{
    private const string Message = """
        Сумщина:
        БпЛА курсом на Кириківку
        БпЛА курсом на Тростянець
        БпЛА курсом на Путивль

        Чернігівщина:
        4х БпЛА курсом на Батурин
        2х БпЛА курсом на Дмитрівку
        БпЛА курсом на Сосницю
        БпЛА курсом на Ніжин

        Київщина:
        2х БпЛА курсом на Бровари

        Житомирщина:
        БпЛА курсом на Бердичів

        Тернопільщина:
        БпЛА курсом на Козову

        Рівненщина:
        БпЛА курсом на Березне
        ➡Підписатися
        """;

    [Fact]
    public void Every_line_is_a_fact_and_headers_locate_the_lines_below()
    {
        var parser = new RuleParser(new StaticIndexes());
        var normalized = new Normalizer().Normalize(Message);
        var facts = parser.Parse(normalized, new ParseContext(1, normalized.Language, null));
        var sightings = facts.Where(f => f.EventType == EventType.TargetObserved).ToList();
        Assert.Equal(11, sightings.Count);
        // Headers are not sightings.
        Assert.DoesNotContain(sightings, f => f.SegmentText.Trim().EndsWith(':'));
        // Counts survive.
        Assert.Equal(4, sightings.Single(f => f.SegmentText.Contains("Батурин", StringComparison.OrdinalIgnoreCase)).Count);
        Assert.Equal(2, sightings.Single(f => f.SegmentText.Contains("Бровари", StringComparison.OrdinalIgnoreCase)).Count);
        // Known towns are destinations inside their header's oblast.
        var nizhyn = sightings.Single(f => f.SegmentText.Contains("Ніжин", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Ніжин", nizhyn.Destination?.Place.Name);
        Assert.Equal("Чернігівська область", TestIndexes.Gazetteer.RegionOf(nizhyn.Destination!.Place)?.Name);
        // A town the gazetteer does not know still lands in the right oblast, not nowhere and not in a namesake's oblast.
        var putyvl = sightings.Single(f => f.SegmentText.Contains("Путивль", StringComparison.OrdinalIgnoreCase));
        Assert.Null(putyvl.Destination);
        Assert.Equal("Сумська область", putyvl.Current?.Place.Name);
        Assert.Contains("section_region", putyvl.Rules);
        var berezne = sightings.Single(f => f.SegmentText.Contains("Березне", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Рівненська область", berezne.Current?.Place.Name);
    }
}
