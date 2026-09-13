using System.Text.Json;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;
using Puluj.Infrastructure.Seeding;
using Puluj.Processing.Indexes;

namespace Puluj.Processing.Tests.Support;

/// <summary>Builds the in-memory indexes straight from the seed files (no database), so parser tests run anywhere.</summary>
public static class TestIndexes
{
    public static string RepoRoot { get; } = FindRepoRoot();

    private static readonly Lazy<TaxonomyIndex> TaxonomyLazy = new(LoadTaxonomy);
    private static readonly Lazy<GazetteerIndex> GazetteerLazy = new(LoadGazetteer);

    public static TaxonomyIndex Taxonomy => TaxonomyLazy.Value;
    public static GazetteerIndex Gazetteer => GazetteerLazy.Value;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Puluj.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    private static T Read<T>(string relative)
    {
        using var stream = File.OpenRead(Path.Combine(RepoRoot, relative));
        return JsonSerializer.Deserialize<T>(stream, SeedFiles.Json)!;
    }

    private static TaxonomyIndex LoadTaxonomy()
    {
        var tax = Read<TaxonomyFile>("data/taxonomy/taxonomy.json");
        var models = Read<ModelsFile>("data/taxonomy/models.json");
        var refs = new Dictionary<(AliasTargetLevel, int), TargetRef>();
        var catIds = new Dictionary<string, int>();
        var classIds = new Dictionary<string, int>();
        var familyIds = new Dictionary<string, int>();
        var modelIds = new Dictionary<string, int>();
        var profiles = new Dictionary<int, ClassProfile>();
        var next = 1;
        foreach (var c in tax.Categories)
        {
            catIds[c.Code] = next;
            refs[(AliasTargetLevel.Category, next)] = new TargetRef(next, null, null, null, c.Code, c.Name, AliasTargetLevel.Category);
            next++;
        }
        foreach (var c in tax.Classes)
        {
            classIds[c.Code] = next;
            refs[(AliasTargetLevel.Class, next)] = new TargetRef(catIds[c.Category], next, null, null, c.Code, c.Name, AliasTargetLevel.Class);
            profiles[next] = ClassProfile.FromMetadata(next, c.Code, c.Metadata is null ? null : JsonDocument.Parse(c.Metadata.Value.GetRawText()));
            next++;
        }
        foreach (var f in tax.Families)
        {
            familyIds[f.Code] = next;
            var cls = refs[(AliasTargetLevel.Class, classIds[f.Class])];
            refs[(AliasTargetLevel.Family, next)] = new TargetRef(cls.CategoryId, cls.ClassId, next, null, f.Code, f.Name, AliasTargetLevel.Family);
            next++;
        }
        foreach (var m in models.Models)
        {
            modelIds[m.Code] = next;
            var fam = refs[(AliasTargetLevel.Family, familyIds[m.Family])];
            refs[(AliasTargetLevel.Model, next)] = new TargetRef(fam.CategoryId, fam.ClassId, fam.FamilyId, next, m.Code, m.Name, AliasTargetLevel.Model);
            next++;
        }

        var aliases = new List<AliasEntry>();
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot, "data/taxonomy"), "aliases*.json").Order())
        {
            var data = Read<AliasesFile>(Path.GetRelativePath(RepoRoot, file));
            foreach (var a in data.Aliases)
            {
                var (levelText, code) = (a.Target.Split(':')[0], a.Target.Split(':')[1]);
                var level = Enum.Parse<AliasTargetLevel>(levelText, true);
                var id = level switch
                {
                    AliasTargetLevel.Category => catIds[code],
                    AliasTargetLevel.Class => classIds[code],
                    AliasTargetLevel.Family => familyIds[code],
                    _ => modelIds[code],
                };
                var alias = a.Alias.Trim().ToLowerInvariant();
                aliases.Add(new AliasEntry(alias, alias.Split(' ', StringSplitOptions.RemoveEmptyEntries), a.Exact ?? false, level, id,
                    a.Priority ?? 10, Enum.TryParse<ConfidenceLevel>(a.Confidence, true, out var c) ? c : ConfidenceLevel.Medium,
                    string.IsNullOrWhiteSpace(a.Lang) ? "*" : a.Lang, null));
            }
        }
        var ordered = aliases.OrderByDescending(a => a.Words.Length).ThenByDescending(a => a.Priority).ToList();
        return new TaxonomyIndex(ordered, refs, profiles, catIds, classIds);
    }

    private static GazetteerIndex LoadGazetteer()
    {
        var data = Read<GazetteerFile>("data/corpus/gazetteer-lite.json");
        return new GazetteerIndex(data.Places.Select(p =>
            (new PlaceEntry(p.Id, p.Name, (PlaceLevel)p.Level, p.ParentId, p.Country, p.Population, Geo.Point(p.Lon, p.Lat), p.RadiusKm), p.Variants)));
    }

    private sealed record TaxonomyFile(List<CategoryDto> Categories, List<ClassDto> Classes, List<FamilyDto> Families);
    private sealed record CategoryDto(string Code, string Name);
    private sealed record ClassDto(string Code, string Category, string Name, JsonElement? Metadata);
    private sealed record FamilyDto(string Code, string Class, string Name);
    private sealed record ModelsFile(List<ModelDto> Models);
    private sealed record ModelDto(string Code, string Family, string Name);
    private sealed record AliasesFile(List<AliasDto> Aliases);
    private sealed record AliasDto(string Alias, string Target, string? Lang, string? Confidence, bool? Exact, int? Priority);
    private sealed record GazetteerFile(List<PlaceDto> Places);
    private sealed record PlaceDto(int Id, string Name, int Level, int? ParentId, string Country, int Population, double Lon, double Lat, double RadiusKm, string[] Variants, string Key);
}
