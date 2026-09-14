using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using NetTopologySuite.Simplify;
using Puluj.Domain.Entities;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;

namespace Puluj.Infrastructure.Seeding;

/// <summary>
/// Imports regions (geoBoundaries GeoJSON + regions.json), named areas and GeoNames settlements into Place.
/// Upserts by ExternalKey. Skips silently when data files are absent (they are downloaded, not committed).
/// </summary>
public sealed class GazetteerSeeder(SeedFiles files, IOptions<SeedOptions> options, ILogger<GazetteerSeeder> logger) : ISeeder
{
    public int Order => 30;

    private const double SimplifyToleranceDeg = 0.002; // ~200 m; keeps oblast polygons small enough for the API
    /// <summary>Every GeoNames populated place is imported (the full settlement list); the matcher demands a cue word for small ones.</summary>
    private const int MinPopulation = 0;

    private static readonly JsonSerializerOptions GeoJson = new()
    {
        Converters = { new GeoJsonConverterFactory(Geo.Factory) },
    };

    public async Task SeedAsync(PulujDbContext db, CancellationToken ct)
    {
        if (!options.Value.SeedGazetteer)
        {
            return;
        }
        var regionsFile = await files.ReadAsync<RegionsFile>("gazetteer/regions.json", ct);
        if (regionsFile is null)
        {
            logger.LogWarning("gazetteer/regions.json not found under {Root}; skipping gazetteer", files.Root);
            return;
        }

        var existing = await db.Places.ToDictionaryAsync(p => p.ExternalKey, ct);
        var regions = 0;
        foreach (var file in new[] { "ukr_adm0.geojson", "ukr_adm1.geojson", "blr_adm1.geojson", "rus_adm1.geojson", "mda_adm0.geojson" })
        {
            regions += await ImportRegionsAsync(db, existing, regionsFile.Regions, file, ct);
        }
        regions += await ImportCityDistrictsAsync(db, existing, regionsFile.KyivDistricts ?? [], "kyiv_districts.geojson", "iso:UA-30", ct);
        regions += await ImportCodAdminAsync(db, existing, ct);
        foreach (var area in regionsFile.NamedAreas ?? [])
        {
            var ring = area.Polygon.Select(p => new Coordinate(p[0], p[1])).ToArray();
            var polygon = Geo.Factory.CreatePolygon(ring);
            Upsert(db, existing, $"area:{area.Code}", area.Name, area.Variants, PlaceLevel.NamedArea, "XX", polygon);
            regions++;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Gazetteer: {Count} regions/areas", regions);

        var settlements = await ImportSettlementsAsync(db, existing, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Gazetteer: {Count} settlements", settlements);

        // Assign settlements to the polygon that contains them: city-regions (Kyiv, Sevastopol) first, then regions,
        // then the nearest region for points sitting on a simplified border.
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        foreach (var level in new[] { (int)PlaceLevel.City, (int)PlaceLevel.Region })
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                UPDATE places p SET parent_id = r.place_id
                FROM places r
                WHERE p.external_key LIKE 'geonames:%' AND p.parent_id IS NULL
                  AND r.external_key LIKE 'iso:%' AND r.level = {level}
                  AND ST_Intersects(r.geometry, p.geometry)
                """, ct);
        }
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE places p SET parent_id = (
                SELECT r.place_id FROM places r
                WHERE r.external_key LIKE 'iso:%' AND r.level = 1 AND ST_DWithin(r.geometry, p.geometry, 0.05)
                ORDER BY ST_Distance(r.geometry, p.geometry) LIMIT 1)
            WHERE p.external_key LIKE 'geonames:%' AND p.parent_id IS NULL
            """, ct);
    }

    private async Task<int> ImportRegionsAsync(PulujDbContext db, Dictionary<string, Place> existing,
        Dictionary<string, RegionDto> regions, string file, CancellationToken ct)
    {
        var path = files.Resolve("gazetteer", file);
        if (!File.Exists(path))
        {
            logger.LogWarning("Gazetteer file {File} missing; run scripts/gazetteer/download.ps1", file);
            return 0;
        }
        await using var stream = File.OpenRead(path);
        var fc = await JsonSerializer.DeserializeAsync<FeatureCollection>(stream, GeoJson, ct);
        var count = 0;
        foreach (var f in fc ?? [])
        {
            var iso = f.Attributes.GetOptionalValue("shapeISO")?.ToString();
            if (iso is null || !regions.TryGetValue(iso, out var dto) || f.Geometry is null)
            {
                continue; // e.g. Russian regions far from the border
            }
            var geom = TopologyPreservingSimplifier.Simplify(f.Geometry, SimplifyToleranceDeg);
            geom.SRID = Geo.Srid;
            var level = Enum.TryParse<PlaceLevel>(dto.Level, true, out var l) ? l : PlaceLevel.Region;
            var country = iso == "UKR" ? "UA" : iso.Length >= 2 ? iso[..2] : "XX";
            var variants = dto.Variants.Concat(NameVariantGenerator.ForSettlement(dto.Short ?? dto.Name)).Distinct().ToArray();
            Upsert(db, existing, $"iso:{iso}", dto.Name, variants, level, country, geom);
            count++;
        }
        return count;
    }

    /// <summary>City districts (Kyiv today): polygons from OSM, parented to the city-region so RegionOf() still yields the city.</summary>
    private async Task<int> ImportCityDistrictsAsync(PulujDbContext db, Dictionary<string, Place> existing,
        Dictionary<string, List<string>> variantsByName, string file, string cityKey, CancellationToken ct)
    {
        var path = files.Resolve("gazetteer", file);
        if (!File.Exists(path) || !existing.TryGetValue(cityKey, out var city))
        {
            logger.LogWarning("Gazetteer file {File} or city {City} missing; city districts not imported (run scripts/gazetteer/download-kyiv.py)", file, cityKey);
            return 0;
        }
        await using var stream = File.OpenRead(path);
        var fc = await JsonSerializer.DeserializeAsync<FeatureCollection>(stream, GeoJson, ct);
        var count = 0;
        foreach (var f in fc ?? [])
        {
            var name = f.Attributes.GetOptionalValue("name")?.ToString();
            var osmId = f.Attributes.GetOptionalValue("osmId")?.ToString();
            if (name is null || osmId is null || f.Geometry is null)
            {
                continue;
            }
            var geom = TopologyPreservingSimplifier.Simplify(f.Geometry, SimplifyToleranceDeg / 4);
            geom.SRID = Geo.Srid;
            var extra = variantsByName.TryGetValue(name, out var v) ? v : [];
            var variants = NameVariantGenerator.ForSettlement(name.Replace(" район", "")).Concat(extra).Distinct();
            var place = Upsert(db, existing, $"osm:{osmId}", name, variants, PlaceLevel.District, city.CountryCode, geom);
            place.Parent = city;
            count++;
        }
        return count;
    }

    /// <summary>
    /// Raions (ADM2, 136 + the two city-raions) and hromadas (ADM3, ~1 770) of the 2020 reform from the UN COD-AB
    /// dataset (data/gazetteer/ukr_adm{2,3}_cod.geojson, scripts/gazetteer/download.ps1). These are the levels the
    /// alerts are published at. Raions are parented to the oblast whose polygon holds their centroid, hromadas to
    /// their raion by pcode. Kyiv and Sevastopol already exist as city-regions and are skipped at both levels.
    /// Name variants are the full forms only ("бахмутськ район", "вовчанськ громад"), so the parser never confuses
    /// a raion with the town it is named after.
    /// </summary>
    private async Task<int> ImportCodAdminAsync(PulujDbContext db, Dictionary<string, Place> existing, CancellationToken ct)
    {
        var raionsPath = files.Resolve("gazetteer", "ukr_adm2_cod.geojson");
        var hromadasPath = files.Resolve("gazetteer", "ukr_adm3_cod.geojson");
        if (!File.Exists(raionsPath) || !File.Exists(hromadasPath))
        {
            logger.LogWarning("COD-AB files ukr_adm2_cod.geojson / ukr_adm3_cod.geojson missing; raions and hromadas not imported (run scripts/gazetteer/download.ps1)");
            return 0;
        }
        var oblasts = existing.Values
            .Where(p => p.CountryCode == "UA" && p.ExternalKey.StartsWith("iso:", StringComparison.Ordinal) && (p.Level == PlaceLevel.Region || (p.Level == PlaceLevel.City && p.ParentId is null)))
            .ToList();
        static bool IsCityRegion(string? pcode) => pcode is not null && (pcode.StartsWith("UA80", StringComparison.Ordinal) || pcode.StartsWith("UA85", StringComparison.Ordinal));

        var count = 0;
        var raionByPcode = new Dictionary<string, Place>();
        await using (var stream = File.OpenRead(raionsPath))
        {
            var fc = await JsonSerializer.DeserializeAsync<FeatureCollection>(stream, GeoJson, ct);
            foreach (var f in fc ?? [])
            {
                var pcode = f.Attributes.GetOptionalValue("adm2_pcode")?.ToString();
                var name = f.Attributes.GetOptionalValue("adm2_name1")?.ToString();
                if (pcode is null || name is null || f.Geometry is null || IsCityRegion(pcode))
                {
                    continue;
                }
                var geom = TopologyPreservingSimplifier.Simplify(f.Geometry, SimplifyToleranceDeg * 1.5);
                geom.SRID = Geo.Srid;
                var full = $"{name} район";
                var place = Upsert(db, existing, $"cod:{pcode}", full, NameVariantGenerator.ForSettlement(full), PlaceLevel.District, "UA", geom);
                var centroid = geom.Centroid;
                place.Parent = oblasts.FirstOrDefault(o => o.Geometry.Contains(centroid)) ?? oblasts.OrderBy(o => o.Geometry.Distance(centroid)).FirstOrDefault();
                raionByPcode[pcode] = place;
                count++;
            }
        }
        await using (var stream = File.OpenRead(hromadasPath))
        {
            var fc = await JsonSerializer.DeserializeAsync<FeatureCollection>(stream, GeoJson, ct);
            foreach (var f in fc ?? [])
            {
                var pcode = f.Attributes.GetOptionalValue("adm3_pcode")?.ToString();
                var name = f.Attributes.GetOptionalValue("adm3_name1")?.ToString();
                var raionPcode = f.Attributes.GetOptionalValue("adm2_pcode")?.ToString();
                if (pcode is null || name is null || f.Geometry is null || IsCityRegion(pcode))
                {
                    continue;
                }
                var geom = TopologyPreservingSimplifier.Simplify(f.Geometry, SimplifyToleranceDeg);
                geom.SRID = Geo.Srid;
                var full = $"{name} територіальна громада";
                var variants = NameVariantGenerator.ForSettlement(full).Concat(NameVariantGenerator.ForSettlement($"{name} громада")).Concat(NameVariantGenerator.ForSettlement($"{name} ТГ"));
                var place = Upsert(db, existing, $"cod:{pcode}", full, variants, PlaceLevel.Hromada, "UA", geom);
                if (raionPcode is not null && raionByPcode.TryGetValue(raionPcode, out var raion))
                {
                    place.Parent = raion;
                }
                count++;
            }
        }
        logger.LogInformation("Gazetteer: {Raions} raions, {Total} raions + hromadas from COD-AB", raionByPcode.Count, count);
        return count;
    }

    private async Task<int> ImportSettlementsAsync(PulujDbContext db, Dictionary<string, Place> existing, CancellationToken ct)
    {
        var main = files.Resolve("gazetteer", "geonames_UA.txt");
        var alt = files.Resolve("gazetteer", "geonames_UA_alternatenames.txt");
        if (!File.Exists(main))
        {
            logger.LogWarning("geonames_UA.txt missing; settlements not imported");
            return 0;
        }

        // geonameid -> (uk names, ru names); preferred uk name first.
        var names = new Dictionary<long, (List<string> Uk, List<string> Ru)>();
        if (File.Exists(alt))
        {
            foreach (var line in File.ReadLines(alt))
            {
                var c = line.Split('\t');
                if (c.Length < 5 || (c[2] != "uk" && c[2] != "ru"))
                {
                    continue;
                }
                var id = long.Parse(c[1], CultureInfo.InvariantCulture);
                if (!names.TryGetValue(id, out var entry))
                {
                    entry = ([], []);
                    names[id] = entry;
                }
                var list = c[2] == "uk" ? entry.Uk : entry.Ru;
                if (c[4] == "1")
                {
                    list.Insert(0, c[3]);
                }
                else
                {
                    list.Add(c[3]);
                }
            }
        }

        // Kyiv/Sevastopol/Minsk already exist as polygons from regions.json; do not add GeoNames points for them.
        var cityRegionNames = existing.Values
            .Where(p => p.ExternalKey.StartsWith("iso:", StringComparison.Ordinal) && p.Level == PlaceLevel.City)
            .Select(p => NameVariantGenerator.Normalize(p.Name))
            .ToHashSet();

        var count = 0;
        await foreach (var line in File.ReadLinesAsync(main, ct))
        {
            var c = line.Split('\t');
            if (c.Length < 15 || c[6] != "P")
            {
                continue;
            }
            var fcode = c[7];
            if (fcode is "PPLX" or "PPLH" or "PPLQ" or "PPLW")
            {
                continue; // city districts, historical/abandoned places
            }
            var population = int.TryParse(c[14], out var pop) ? pop : 0;
            var isSeat = fcode is "PPLC" or "PPLA" or "PPLA2";
            if (population < MinPopulation && !isSeat)
            {
                continue;
            }
            var id = long.Parse(c[0], CultureInfo.InvariantCulture);
            names.TryGetValue(id, out var n);
            var ukName = n.Uk?.FirstOrDefault() ?? c[1];
            if (cityRegionNames.Contains(NameVariantGenerator.Normalize(ukName)) && fcode is "PPLC" or "PPLA")
            {
                continue;
            }
            var level = fcode switch
            {
                "PPLC" or "PPLA" => PlaceLevel.City,
                _ when population >= 50_000 => PlaceLevel.City,
                "PPLA2" => PlaceLevel.Town,
                _ when population >= 5_000 => PlaceLevel.Town,
                _ => PlaceLevel.Village,
            };
            var lat = double.Parse(c[4], CultureInfo.InvariantCulture);
            var lon = double.Parse(c[5], CultureInfo.InvariantCulture);
            var point = Geo.Point(lon, lat);
            var variants = NameVariantGenerator.ForSettlement(ukName, (n.Uk ?? []).Skip(1).Concat(n.Ru ?? []));
            var place = Upsert(db, existing, $"geonames:{id}", ukName, variants, level, "UA", point);
            place.Population = population;
            place.RadiusKm = Math.Max(1.5, 0.01 * Math.Sqrt(population));
            count++;
        }
        return count;
    }

    private static Place Upsert(PulujDbContext db, Dictionary<string, Place> existing, string key, string name,
        IEnumerable<string> variants, PlaceLevel level, string country, Geometry geometry)
    {
        var centroid = geometry.Centroid;
        centroid.SRID = Geo.Srid;
        if (!existing.TryGetValue(key, out var place))
        {
            place = new Place { ExternalKey = key, Name = name, Geometry = geometry, Centroid = centroid };
            db.Places.Add(place);
            existing[key] = place;
        }
        place.Name = name;
        place.NameVariants = variants.Select(NameVariantGenerator.Normalize).Distinct().ToArray();
        place.Level = level;
        place.CountryCode = country;
        place.Geometry = geometry;
        place.Centroid = centroid;
        place.RadiusKm = geometry is Point ? place.RadiusKm : Geo.CoveringRadiusKm(geometry);
        return place;
    }

    private sealed record RegionsFile(Dictionary<string, RegionDto> Regions, List<NamedAreaDto>? NamedAreas, Dictionary<string, List<string>>? KyivDistricts);
    private sealed record RegionDto(string Name, string? Short, string? Level, List<string> Variants);
    private sealed record NamedAreaDto(string Code, string Name, List<string> Variants, List<double[]> Polygon);
}
