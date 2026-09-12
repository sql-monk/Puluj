using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace Puluj.Infrastructure.Persistence;

/// <summary>Shared geometry factory: everything in Puluj is WGS84 (SRID 4326).</summary>
public static class Geo
{
    public const int Srid = 4326;
    public static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory(Srid);

    public static Point Point(double lon, double lat) => Factory.CreatePoint(new Coordinate(lon, lat));

    /// <summary>Great-circle distance in km (haversine). Good enough for ETA/correlation on the client and server alike.</summary>
    public static double DistanceKm(Coordinate a, Coordinate b)
    {
        const double r = 6371.0088;
        double dLat = ToRad(b.Y - a.Y), dLon = ToRad(b.X - a.X);
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                   + Math.Cos(ToRad(a.Y)) * Math.Cos(ToRad(b.Y)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Sqrt(h));
    }

    /// <summary>Initial bearing from a to b in degrees [0, 360).</summary>
    public static double BearingDeg(Coordinate a, Coordinate b)
    {
        double lat1 = ToRad(a.Y), lat2 = ToRad(b.Y), dLon = ToRad(b.X - a.X);
        double y = Math.Sin(dLon) * Math.Cos(lat2);
        double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        return (ToDeg(Math.Atan2(y, x)) + 360) % 360;
    }

    /// <summary>Point at the given bearing and distance from the start (great-circle).</summary>
    public static Point Offset(Coordinate start, double bearingDeg, double km)
    {
        const double r = 6371.0088;
        double lat1 = ToRad(start.Y), lon1 = ToRad(start.X), brng = ToRad(bearingDeg), d = km / r;
        double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d) + Math.Cos(lat1) * Math.Sin(d) * Math.Cos(brng));
        double lon2 = lon1 + Math.Atan2(Math.Sin(brng) * Math.Sin(d) * Math.Cos(lat1), Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));
        return Point(ToDeg(lon2), ToDeg(lat2));
    }

    /// <summary>Smallest absolute difference between two bearings, in degrees [0, 180].</summary>
    public static double AngleDiffDeg(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    /// <summary>Radius in km of the circle around the centroid that covers the geometry.</summary>
    public static double CoveringRadiusKm(Geometry g)
    {
        var c = g.Centroid.Coordinate;
        return g.Coordinates.Length == 0 ? 0 : g.Coordinates.Max(p => DistanceKm(c, p));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;
    private static double ToDeg(double rad) => rad * 180 / Math.PI;
}
