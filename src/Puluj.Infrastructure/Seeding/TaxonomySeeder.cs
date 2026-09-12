using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Infrastructure.Seeding;

/// <summary>Upserts the threat hierarchy and aliases from data/taxonomy/*.json. Keyed by Code, so re-runs are safe.</summary>
public sealed class TaxonomySeeder(SeedFiles files, IOptions<SeedOptions> options, ILogger<TaxonomySeeder> logger) : ISeeder
{
    public int Order => 10;

    public async Task SeedAsync(PulujDbContext db, CancellationToken ct)
    {
        if (!options.Value.SeedTaxonomy)
        {
            return;
        }

        var tax = await files.ReadAsync<TaxonomyFile>("taxonomy/taxonomy.json", ct);
        if (tax is null)
        {
            logger.LogWarning("taxonomy/taxonomy.json not found under {Root}; skipping", files.Root);
            return;
        }
        var modelsFile = await files.ReadAsync<ModelsFile>("taxonomy/models.json", ct);

        var categories = await db.ThreatCategories.ToDictionaryAsync(x => x.Code, ct);
        foreach (var c in tax.Categories)
        {
            if (!categories.TryGetValue(c.Code, out var e))
            {
                e = new ThreatCategory { Code = c.Code, Name = c.Name };
                db.ThreatCategories.Add(e);
                categories[c.Code] = e;
            }
            e.Name = c.Name;
        }
        await db.SaveChangesAsync(ct);

        var classes = await db.ThreatClasses.ToDictionaryAsync(x => x.Code, ct);
        foreach (var c in tax.Classes)
        {
            if (!classes.TryGetValue(c.Code, out var e))
            {
                e = new ThreatClass { Code = c.Code, Name = c.Name, ThreatCategoryId = categories[c.Category].ThreatCategoryId };
                db.ThreatClasses.Add(e);
                classes[c.Code] = e;
            }
            e.Name = c.Name;
            e.ThreatCategoryId = categories[c.Category].ThreatCategoryId;
            e.Metadata = ToDoc(c.Metadata);
        }
        await db.SaveChangesAsync(ct);

        var families = await db.ThreatFamilies.ToDictionaryAsync(x => x.Code, ct);
        foreach (var f in tax.Families)
        {
            if (!families.TryGetValue(f.Code, out var e))
            {
                e = new ThreatFamily { Code = f.Code, Name = f.Name, ThreatClassId = classes[f.Class].ThreatClassId };
                db.ThreatFamilies.Add(e);
                families[f.Code] = e;
            }
            e.Name = f.Name;
            e.ThreatClassId = classes[f.Class].ThreatClassId;
            e.Metadata = ToDoc(f.Metadata);
        }
        await db.SaveChangesAsync(ct);

        var models = await db.ThreatModels.ToDictionaryAsync(x => x.Code, ct);
        foreach (var m in modelsFile?.Models ?? [])
        {
            if (!models.TryGetValue(m.Code, out var e))
            {
                e = new ThreatModel { Code = m.Code, CanonicalName = m.Name, ThreatFamilyId = families[m.Family].ThreatFamilyId };
                db.ThreatModels.Add(e);
                models[m.Code] = e;
            }
            e.CanonicalName = m.Name;
            e.ThreatFamilyId = families[m.Family].ThreatFamilyId;
            e.Manufacturer = m.Manufacturer;
            e.Country = m.Country;
            e.Enabled = m.Enabled ?? true;
            e.Metadata = ToDoc(m.Metadata);
        }
        await db.SaveChangesAsync(ct);

        await SeedAliasesAsync(db, categories, classes, families, models, ct);
        logger.LogInformation("Taxonomy: {Cat} categories, {Cls} classes, {Fam} families, {Mod} models",
            categories.Count, classes.Count, families.Count, models.Count);
    }

    private async Task SeedAliasesAsync(PulujDbContext db,
        Dictionary<string, ThreatCategory> categories, Dictionary<string, ThreatClass> classes,
        Dictionary<string, ThreatFamily> families, Dictionary<string, ThreatModel> models, CancellationToken ct)
    {
        var existing = await db.ThreatModelAliases.ToListAsync(ct);
        var byKey = existing.ToDictionary(a => (a.Alias, a.Language, a.TargetLevel, a.TargetId));
        var seen = new HashSet<(string, string, AliasTargetLevel, int)>();

        foreach (var file in Directory.GetFiles(files.Resolve("taxonomy"), "aliases*.json").Order())
        {
            var rel = Path.GetRelativePath(files.Root, file);
            var data = await files.ReadAsync<AliasesFile>(rel, ct);
            foreach (var a in data?.Aliases ?? [])
            {
                var (level, code) = ParseTarget(a.Target);
                int id = level switch
                {
                    AliasTargetLevel.Category => categories[code].ThreatCategoryId,
                    AliasTargetLevel.Class => classes[code].ThreatClassId,
                    AliasTargetLevel.Family => families[code].ThreatFamilyId,
                    AliasTargetLevel.Model => models[code].ThreatModelId,
                    _ => throw new InvalidOperationException($"Unknown alias target level in '{a.Target}'"),
                };
                var alias = a.Alias.Trim().ToLowerInvariant();
                var lang = string.IsNullOrWhiteSpace(a.Lang) ? "*" : a.Lang;
                var key = (alias, lang, level, id);
                seen.Add(key);
                if (!byKey.TryGetValue(key, out var e))
                {
                    e = new ThreatModelAlias { Alias = alias, Language = lang, TargetLevel = level, TargetId = id };
                    db.ThreatModelAliases.Add(e);
                    byKey[key] = e;
                }
                e.Priority = a.Priority ?? 10;
                e.ExactMatch = a.Exact ?? false;
                e.ImpliedConfidence = Enum.TryParse<ConfidenceLevel>(a.Confidence, true, out var c) ? c : ConfidenceLevel.Medium;
            }
        }
        // Aliases removed from the seed files (and not source-specific) are removed from the database too.
        db.ThreatModelAliases.RemoveRange(existing.Where(a => a.SourceId is null && !seen.Contains((a.Alias, a.Language, a.TargetLevel, a.TargetId))));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Taxonomy: {Count} aliases", seen.Count);
    }

    private static (AliasTargetLevel, string) ParseTarget(string target)
    {
        var idx = target.IndexOf(':');
        if (idx <= 0)
        {
            throw new InvalidOperationException($"Alias target must be 'level:CODE', got '{target}'");
        }
        var level = Enum.Parse<AliasTargetLevel>(target[..idx], true);
        return (level, target[(idx + 1)..]);
    }

    private static JsonDocument? ToDoc(JsonElement? el) =>
        el is null || el.Value.ValueKind == JsonValueKind.Undefined ? null : JsonDocument.Parse(el.Value.GetRawText());

    private sealed record TaxonomyFile(List<CategoryDto> Categories, List<ClassDto> Classes, List<FamilyDto> Families);
    private sealed record CategoryDto(string Code, string Name);
    private sealed record ClassDto(string Code, string Category, string Name, JsonElement? Metadata);
    private sealed record FamilyDto(string Code, string Class, string Name, JsonElement? Metadata);
    private sealed record ModelsFile(List<ModelDto> Models);
    private sealed record ModelDto(string Code, string Family, string Name, string? Manufacturer, string? Country, bool? Enabled, JsonElement? Metadata);
    private sealed record AliasesFile(List<AliasDto> Aliases);
    private sealed record AliasDto(string Alias, string Target, string? Lang, string? Confidence, bool? Exact, int? Priority);
}
