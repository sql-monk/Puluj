using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Processing.Indexes;

/// <summary>Loads taxonomy and gazetteer snapshots from the database and refreshes them periodically (spec §7: taxonomy lives in the DB).</summary>
public sealed class IndexProvider(IDbContextFactory<PulujDbContext> factory, ILogger<IndexProvider> logger) : BackgroundService, IIndexes
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaxonomyIndex Taxonomy { get; private set; } = TaxonomyIndex.Empty;
    public GazetteerIndex Gazetteer { get; private set; } = GazetteerIndex.Empty;

    /// <summary>Completes after the first successful load.</summary>
    public Task Ready => _ready.Task;

    public async Task RefreshAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        Taxonomy = await LoadTaxonomyAsync(db, ct);
        Gazetteer = await LoadGazetteerAsync(db, ct);
        _ready.TrySetResult();
        logger.LogInformation("Indexes loaded: {Aliases} aliases, {Places} places", Taxonomy.Aliases.Count, Gazetteer.Count);
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
                logger.LogError(ex, "Index refresh failed");
            }
            await Task.Delay(RefreshInterval, ct);
        }
    }

    public static async Task<TaxonomyIndex> LoadTaxonomyAsync(PulujDbContext db, CancellationToken ct)
    {
        var categories = await db.ThreatCategories.AsNoTracking().ToListAsync(ct);
        var classes = await db.ThreatClasses.AsNoTracking().ToListAsync(ct);
        var families = await db.ThreatFamilies.AsNoTracking().ToListAsync(ct);
        var models = await db.ThreatModels.AsNoTracking().Where(m => m.Enabled).ToListAsync(ct);
        var aliases = await db.ThreatModelAliases.AsNoTracking().ToListAsync(ct);

        var classById = classes.ToDictionary(c => c.ThreatClassId);
        var familyById = families.ToDictionary(f => f.ThreatFamilyId);
        var refs = new Dictionary<(AliasTargetLevel, int), ThreatRef>();
        foreach (var c in categories)
        {
            refs[(AliasTargetLevel.Category, c.ThreatCategoryId)] = new ThreatRef(c.ThreatCategoryId, null, null, null, c.Code, c.Name, AliasTargetLevel.Category);
        }
        foreach (var c in classes)
        {
            refs[(AliasTargetLevel.Class, c.ThreatClassId)] = new ThreatRef(c.ThreatCategoryId, c.ThreatClassId, null, null, c.Code, c.Name, AliasTargetLevel.Class);
        }
        foreach (var f in families)
        {
            var c = classById[f.ThreatClassId];
            refs[(AliasTargetLevel.Family, f.ThreatFamilyId)] = new ThreatRef(c.ThreatCategoryId, c.ThreatClassId, f.ThreatFamilyId, null, f.Code, f.Name, AliasTargetLevel.Family);
        }
        foreach (var m in models)
        {
            var f = familyById[m.ThreatFamilyId];
            var c = classById[f.ThreatClassId];
            refs[(AliasTargetLevel.Model, m.ThreatModelId)] = new ThreatRef(c.ThreatCategoryId, c.ThreatClassId, f.ThreatFamilyId, m.ThreatModelId, m.Code, m.CanonicalName, AliasTargetLevel.Model);
        }

        var entries = aliases
            .Where(a => refs.ContainsKey((a.TargetLevel, a.TargetId)))
            .Select(a => new AliasEntry(a.Alias, a.Alias.Split(' ', StringSplitOptions.RemoveEmptyEntries), a.ExactMatch,
                a.TargetLevel, a.TargetId, a.Priority, a.ImpliedConfidence, a.Language, a.SourceId))
            .OrderByDescending(a => a.Words.Length)
            .ThenByDescending(a => a.Priority)
            .ToList();

        var profiles = classes.ToDictionary(c => c.ThreatClassId, c => ClassProfile.FromMetadata(c.ThreatClassId, c.Code, c.Metadata));
        return new TaxonomyIndex(entries, refs, profiles,
            categories.ToDictionary(c => c.Code, c => c.ThreatCategoryId),
            classes.ToDictionary(c => c.Code, c => c.ThreatClassId));
    }

    public static async Task<GazetteerIndex> LoadGazetteerAsync(PulujDbContext db, CancellationToken ct)
    {
        var rows = await db.Places.AsNoTracking()
            .Select(p => new { p.PlaceId, p.Name, p.Level, p.ParentId, p.CountryCode, p.Population, p.Centroid, p.RadiusKm, p.NameVariants, p.Geometry })
            .ToListAsync(ct);
        // Only areal geometries are kept as boundaries; settlements are points and their centroid already says it all.
        return new GazetteerIndex(rows.Select(r =>
            (new PlaceEntry(r.PlaceId, r.Name, r.Level, r.ParentId, r.CountryCode, r.Population ?? 0, r.Centroid, r.RadiusKm,
                r.Geometry is NetTopologySuite.Geometries.IPolygonal ? r.Geometry : null), r.NameVariants)));
    }
}
