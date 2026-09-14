using NetTopologySuite.Geometries;
using Puluj.Domain.Enums;

namespace Puluj.Processing.Indexes;

/// <param name="Boundary">Polygon of an admin unit or named area (null for settlements, which are points): lets the
/// correlator ask "is this town inside that oblast" instead of trusting the oblast's covering radius.</param>
public sealed record PlaceEntry(int PlaceId, string Name, PlaceLevel Level, int? ParentId, string CountryCode,
    int Population, Point Centroid, double RadiusKm, Geometry? Boundary = null);

public sealed record PlaceVariant(string[] Words, PlaceEntry Place);

/// <summary>In-memory gazetteer: variant stems bucketed by the first three characters of the first word.</summary>
public sealed class GazetteerIndex
{
    private readonly Dictionary<string, List<PlaceVariant>> _byPrefix = new(StringComparer.Ordinal);
    private readonly Dictionary<int, PlaceEntry> _byId = [];
    private readonly Dictionary<(PlaceLevel, string), List<PlaceEntry>> _byLevelName = [];
    private readonly Dictionary<PlaceLevel, List<PlaceEntry>> _polygonsByLevel = [];

    public GazetteerIndex(IEnumerable<(PlaceEntry Place, string[] Variants)> places)
    {
        foreach (var (place, variants) in places)
        {
            _byId[place.PlaceId] = place;
            if (place.Level is PlaceLevel.District or PlaceLevel.Hromada or PlaceLevel.Region)
            {
                var key = (place.Level, AdminKey(place.Name));
                if (!_byLevelName.TryGetValue(key, out var list))
                {
                    _byLevelName[key] = list = [];
                }
                list.Add(place);
            }
            if (place.Boundary is not null)
            {
                if (!_polygonsByLevel.TryGetValue(place.Level, out var polys))
                {
                    _polygonsByLevel[place.Level] = polys = [];
                }
                polys.Add(place);
            }
            foreach (var v in variants)
            {
                var words = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0)
                {
                    continue;
                }
                var key = Prefix(words[0]);
                if (!_byPrefix.TryGetValue(key, out var list))
                {
                    list = [];
                    _byPrefix[key] = list;
                }
                list.Add(new PlaceVariant(words, place));
            }
        }
    }

    public static GazetteerIndex Empty { get; } = new([]);

    private static readonly HashSet<string> Friendly = ["UA", "MD", "XX"];
    private List<Geometry>? _hostile;

    /// <summary>Polygons of the regions targets are launched from (everything imported that is not Ukraine, Moldova or a named area).</summary>
    private List<Geometry> Hostile => _hostile ??= _byId.Values
        .Where(p => p.Boundary is not null && p.Level == PlaceLevel.Region && !Friendly.Contains(p.CountryCode))
        .Select(p => p.Boundary!)
        .ToList();

    /// <summary>
    /// The course an object heading for `destination` most plausibly holds: from the nearest hostile territory towards it.
    /// "Київ: БпЛА курсом на Троєщину" is not flying from the city centre; it comes in from Belarus or Russia.
    /// Null when no hostile polygon is loaded.
    /// </summary>
    public double? ApproachBearingTo(Coordinate destination)
    {
        var point = new GeometryFactory().CreatePoint(destination);
        Coordinate? nearest = null;
        var best = double.MaxValue;
        foreach (var g in Hostile)
        {
            var pts = NetTopologySuite.Operation.Distance.DistanceOp.NearestPoints(g, point);
            var d = pts[0].Distance(pts[1]);
            if (d < best)
            {
                best = d;
                nearest = pts[0];
            }
        }
        return nearest is null ? null : Puluj.Infrastructure.Persistence.Geo.BearingDeg(nearest, destination);
    }

    public int Count => _byId.Count;

    /// <summary>
    /// An administrative unit by its official name ("Бахмутський район", "Вовчанська територіальна громада"),
    /// optionally only inside the given oblast. Exact after normalisation; "громада" and "територіальна громада" are
    /// the same thing.
    /// </summary>
    public PlaceEntry? FindAdmin(PlaceLevel level, string name, int? withinRegionId = null)
    {
        if (!_byLevelName.TryGetValue((level, AdminKey(name)), out var list))
        {
            return null;
        }
        if (withinRegionId is int rid)
        {
            var inside = list.Where(p => RegionOf(p)?.PlaceId == rid).ToList();
            if (inside.Count > 0)
            {
                return inside[0];
            }
        }
        return list.Count == 1 ? list[0] : null;
    }

    /// <summary>The polygon of the given level that contains the point (a hromada around a city, a raion around a village).</summary>
    public PlaceEntry? PolygonAt(Coordinate point, PlaceLevel level)
    {
        if (!_polygonsByLevel.TryGetValue(level, out var polys))
        {
            return null;
        }
        var pt = new Point(point) { SRID = 4326 };
        return polys.FirstOrDefault(p => p.Boundary!.EnvelopeInternal.Contains(point) && p.Boundary.Contains(pt));
    }

    private static string AdminKey(string name)
    {
        var s = name.Trim().ToLowerInvariant().Replace('’', '\'').Replace('ʼ', '\'');
        s = s.Replace(" територіальна громада", " громада").Replace(" тг", " громада").Replace(" отг", " громада");
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public PlaceEntry? Get(int placeId) => _byId.TryGetValue(placeId, out var p) ? p : null;

    /// <summary>Candidates whose first stem could match a token starting with these characters.</summary>
    public IReadOnlyList<PlaceVariant> Candidates(string token) =>
        _byPrefix.TryGetValue(Prefix(token), out var list) ? list : [];

    /// <summary>Region (oblast-level) ancestor of a place, or the place itself when it is a region.</summary>
    public PlaceEntry? RegionOf(PlaceEntry place)
    {
        var p = place;
        for (var i = 0; i < 5 && p is not null; i++)
        {
            if (p.Level is PlaceLevel.Region or PlaceLevel.Country or PlaceLevel.NamedArea)
            {
                return p;
            }
            if (p.Level == PlaceLevel.City && p.ParentId is null)
            {
                return p; // Kyiv, Sevastopol: city-regions
            }
            p = p.ParentId is int id ? Get(id) : null;
        }
        return null;
    }

    private static string Prefix(string word) => word.Length <= 3 ? word : word[..3];
}
