using NetTopologySuite.Geometries;
using Puluj.Domain.Enums;

namespace Puluj.Domain.Entities;

/// <summary>Gazetteer entry: administrative unit or named area with geometry.</summary>
public class Place
{
    public int PlaceId { get; set; }
    public required string Name { get; set; }
    /// <summary>Lower-cased name forms for matching: declensions, "-щина" forms, ru/en variants.</summary>
    public string[] NameVariants { get; set; } = [];
    public PlaceLevel Level { get; set; }
    public int? ParentId { get; set; }
    public Place? Parent { get; set; }
    public string CountryCode { get; set; } = "UA";
    public string? KatottgCode { get; set; }
    /// <summary>Stable id in the originating dataset: "iso:UA-74", "geonames:703448", "area:BLACK_SEA".</summary>
    public required string ExternalKey { get; set; }
    public int? Population { get; set; }
    /// <summary>MultiPolygon for admin units, Point for settlements without boundaries. SRID 4326.</summary>
    public required Geometry Geometry { get; set; }
    public required Point Centroid { get; set; }
    /// <summary>Radius (km) of the circle around Centroid that covers Geometry. Used as LocationAccuracy.</summary>
    public double RadiusKm { get; set; }
}
