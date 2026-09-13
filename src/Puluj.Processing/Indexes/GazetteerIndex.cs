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

    public GazetteerIndex(IEnumerable<(PlaceEntry Place, string[] Variants)> places)
    {
        foreach (var (place, variants) in places)
        {
            _byId[place.PlaceId] = place;
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
