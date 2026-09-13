using NetTopologySuite.Geometries;
using Puluj.Domain.Enums;
using Puluj.Infrastructure.Persistence;
using Puluj.Processing.Indexes;
using Puluj.Processing.Pipeline;

namespace Puluj.Processing.Tests.Pipeline;

/// <summary>"Київ: БпЛА курсом на Троєщину" comes in from the north, not from the city centre.</summary>
public class ApproachTests
{
    private static readonly PlaceEntry Kyiv = new(1, "Київ", PlaceLevel.City, null, "UA", 2_900_000, Geo.Point(30.52, 50.45), 26);
    private static readonly PlaceEntry Troieshchyna = new(2, "Троєщина", PlaceLevel.District, 1, "UA", 0, Geo.Point(30.60, 50.51), 3);
    // Homel oblast as a box north of Kyiv.
    private static readonly PlaceEntry Homel = new(3, "Гомельська область", PlaceLevel.Region, null, "BY", 0, Geo.Point(30.5, 52.3), 150,
        Geo.Factory.CreatePolygon([new(28.5, 51.6), new(32.5, 51.6), new(32.5, 53.0), new(28.5, 53.0), new(28.5, 51.6)]));
    private static readonly GazetteerIndex Gazetteer = new([(Kyiv, ["київ"]), (Troieshchyna, ["троєщин"]), (Homel, ["гомельщин"])]);

    [Fact]
    public void Course_comes_from_the_nearest_hostile_territory()
    {
        var bearing = Gazetteer.ApproachBearingTo(Troieshchyna.Centroid.Coordinate);
        Assert.NotNull(bearing);
        Assert.InRange(bearing!.Value, 170, 190); // straight down from Belarus
    }

    [Fact]
    public void Destination_inside_the_city_gets_an_approach_anchor_short_of_it()
    {
        Assert.True(TargetBuilder.DestinationInside(Troieshchyna, Kyiv));
        var (point, accuracy) = TargetBuilder.ApproachAnchor(Troieshchyna, 180);
        // 15 km north of the district, heading south into it.
        Assert.InRange(point.Y, Troieshchyna.Centroid.Y + 0.12, Troieshchyna.Centroid.Y + 0.15);
        Assert.InRange(point.X, Troieshchyna.Centroid.X - 0.01, Troieshchyna.Centroid.X + 0.01);
        Assert.Equal(TargetBuilder.ApproachKm, accuracy);
    }
}
