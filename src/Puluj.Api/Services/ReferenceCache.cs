using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Puluj.Contracts;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Api.Services;

/// <summary>Taxonomy and place names in memory (small, rarely changing), refreshed every 10 minutes.</summary>
public sealed class ReferenceCache(IDbContextFactory<PulujDbContext> factory, ILogger<ReferenceCache> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public sealed record PlaceInfo(int Id, string Name, PlaceLevel Level, int? ParentId, string CountryCode, double Lon, double Lat, double RadiusKm, int Population);

    public IReadOnlyDictionary<int, ThreatCategory> Categories { get; private set; } = new Dictionary<int, ThreatCategory>();
    public IReadOnlyDictionary<int, ThreatClass> Classes { get; private set; } = new Dictionary<int, ThreatClass>();
    public IReadOnlyDictionary<int, ThreatFamily> Families { get; private set; } = new Dictionary<int, ThreatFamily>();
    public IReadOnlyDictionary<int, ThreatModel> Models { get; private set; } = new Dictionary<int, ThreatModel>();
    public IReadOnlyDictionary<int, PlaceInfo> Places { get; private set; } = new Dictionary<int, PlaceInfo>();
    public IReadOnlyDictionary<int, Source> Sources { get; private set; } = new Dictionary<int, Source>();
    public TaxonomyDto Taxonomy { get; private set; } = new([]);

    public Task Ready => _ready.Task;

    public PlaceInfo? Place(int? id) => id is int i && Places.TryGetValue(i, out var p) ? p : null;

    public PlaceInfo? RegionOf(int? placeId)
    {
        var p = Place(placeId);
        for (var i = 0; i < 5 && p is not null; i++)
        {
            if (p.Level is PlaceLevel.Region or PlaceLevel.NamedArea or PlaceLevel.Country || (p.Level == PlaceLevel.City && p.ParentId is null))
            {
                return p;
            }
            p = Place(p.ParentId);
        }
        return null;
    }

    public SpeedProfileDto SpeedProfile(int? classId, int? modelId)
    {
        var cls = classId is int c && Classes.TryGetValue(c, out var k) ? k.Metadata : null;
        var model = modelId is int m && Models.TryGetValue(m, out var md) ? md.Metadata : null;
        return new SpeedProfileDto(
            Num(model, "speedKmhMin") ?? Num(cls, "speedKmhMin"),
            Num(model, "speedKmhMax") ?? Num(cls, "speedKmhMax"),
            Bool(cls, "etaEnabled") ?? false);
    }

    public (string DisplayMode, int FadeMinutes) Display(int? classId)
    {
        var cls = classId is int c && Classes.TryGetValue(c, out var k) ? k.Metadata : null;
        return (Str(cls, "displayMode") ?? "uav", (int)(Num(cls, "fadeMinutes") ?? 20));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reference cache refresh failed");
            }
            // Until the Worker has seeded the gazetteer there is nothing to cache: poll faster.
            await Task.Delay(Places.Count == 0 ? TimeSpan.FromSeconds(15) : RefreshInterval, ct);
        }
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        Categories = await db.ThreatCategories.AsNoTracking().ToDictionaryAsync(x => x.ThreatCategoryId, ct);
        Classes = await db.ThreatClasses.AsNoTracking().ToDictionaryAsync(x => x.ThreatClassId, ct);
        Families = await db.ThreatFamilies.AsNoTracking().ToDictionaryAsync(x => x.ThreatFamilyId, ct);
        Models = await db.ThreatModels.AsNoTracking().ToDictionaryAsync(x => x.ThreatModelId, ct);
        Sources = await db.Sources.AsNoTracking().ToDictionaryAsync(x => x.SourceId, ct);
        Places = (await db.Places.AsNoTracking()
                .Select(p => new { p.PlaceId, p.Name, p.Level, p.ParentId, p.CountryCode, p.Centroid, p.RadiusKm, p.Population })
                .ToListAsync(ct))
            .ToDictionary(p => p.PlaceId, p => new PlaceInfo(p.PlaceId, p.Name, p.Level, p.ParentId, p.CountryCode, p.Centroid.X, p.Centroid.Y, p.RadiusKm, p.Population ?? 0));
        Taxonomy = BuildTaxonomy();
        _ready.TrySetResult();
        logger.LogInformation("Reference cache: {Models} models, {Places} places", Models.Count, Places.Count);
    }

    private TaxonomyDto BuildTaxonomy() => new(Categories.Values.OrderBy(c => c.ThreatCategoryId).Select(c => new TaxonomyCategoryDto(
        c.ThreatCategoryId, c.Code, c.Name,
        Classes.Values.Where(k => k.ThreatCategoryId == c.ThreatCategoryId).OrderBy(k => k.ThreatClassId).Select(k =>
        {
            var (display, fade) = Display(k.ThreatClassId);
            return new TaxonomyClassDto(k.ThreatClassId, k.Code, k.Name, display, fade, SpeedProfile(k.ThreatClassId, null),
                Families.Values.Where(f => f.ThreatClassId == k.ThreatClassId).OrderBy(f => f.ThreatFamilyId).Select(f =>
                    new TaxonomyFamilyDto(f.ThreatFamilyId, f.Code, f.Name,
                        Models.Values.Where(m => m.ThreatFamilyId == f.ThreatFamilyId && m.Enabled).OrderBy(m => m.ThreatModelId).Select(m =>
                            new TaxonomyModelDto(m.ThreatModelId, m.Code, m.CanonicalName, m.Manufacturer, m.Country, SpeedProfile(k.ThreatClassId, m.ThreatModelId))).ToList())).ToList());
        }).ToList())).ToList());

    private static double? Num(JsonDocument? doc, string name) =>
        doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static bool? Bool(JsonDocument? doc, string name) =>
        doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static string? Str(JsonDocument? doc, string name) =>
        doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
