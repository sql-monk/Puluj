using Puluj.Infrastructure.Seeding;

namespace Puluj.Processing.Tests.Seeding;

public class NameVariantGeneratorTests
{
    [Theory]
    [InlineData("Бровари", "бровар")]
    [InlineData("Полтава", "полтав")]
    [InlineData("Херсон", "херсон")]
    [InlineData("Суми", "суми")]
    [InlineData("Хмельницький", "хмельницьк")]
    [InlineData("Біла Церква", "біл церкв")]
    public void Produces_expected_primary_stem(string name, string expected)
    {
        var variants = NameVariantGenerator.ForSettlement(name);
        Assert.Contains(expected, variants);
    }

    [Theory]
    [InlineData("Київ", "києв")]
    [InlineData("Львів", "львов")]
    [InlineData("Харків", "харков")]
    [InlineData("Миколаїв", "миколаєв")]
    [InlineData("Тернопіль", "тернопол")]
    public void Adds_vowel_alternation_variant(string name, string expected)
    {
        Assert.Contains(expected, NameVariantGenerator.ForSettlement(name));
    }

    [Fact]
    public void Includes_extra_names_and_deduplicates()
    {
        var variants = NameVariantGenerator.ForSettlement("Дніпро", ["Дніпропетровськ", "Днепр", "Дніпро"]);
        Assert.Contains("дніпропетровськ", variants);
        Assert.Contains("днепр", variants);
        Assert.Equal(variants.Count, variants.Distinct().Count());
    }

    [Theory]
    [InlineData("  Кам’янець-Подільський ", "кам'янець-подільський")]
    [InlineData("Ёлка", "елка")]
    public void Normalize_unifies_apostrophes_dashes_and_case(string input, string expected)
    {
        Assert.Equal(expected, NameVariantGenerator.Normalize(input));
    }
}
