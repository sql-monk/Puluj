using Puluj.Api.Services;
using Puluj.Domain.Enums;

namespace Puluj.Api.Tests;

/// <summary>Ancestors / descendants over the place tree: what the region window and the alert history call "concerning a place".</summary>
public class PlaceHierarchyTests
{
    private static ReferenceCache.PlaceInfo Place(int id, string name, PlaceLevel level, int? parent = null) =>
        new(id, name, level, parent, "UA", 30, 50, 10, 0);

    // Kyiv oblast (5) → Buchanskyi raion (32597) → Irpin hromada (34000) → Irpin (4000); Kyiv city (26) → Podilskyi district (5078).
    private static readonly Dictionary<int, ReferenceCache.PlaceInfo> Places = new()
    {
        [5] = Place(5, "Київська область", PlaceLevel.Region),
        [32597] = Place(32597, "Бучанський район", PlaceLevel.District, 5),
        [32596] = Place(32596, "Броварський район", PlaceLevel.District, 5),
        [34000] = Place(34000, "Ірпінська громада", PlaceLevel.Hromada, 32597),
        [4000] = Place(4000, "Ірпінь", PlaceLevel.City, 34000),
        [26] = Place(26, "Київ", PlaceLevel.City),
        [5078] = Place(5078, "Подільський район", PlaceLevel.District, 26),
    };

    private static ReferenceCache.PlaceInfo? Lookup(int? id) => id is int i && Places.TryGetValue(i, out var p) ? p : null;

    private static ILookup<int, int> Children(IEnumerable<ReferenceCache.PlaceInfo> places) =>
        places.Where(p => p.ParentId is not null).ToLookup(p => p.ParentId!.Value, p => p.Id);

    [Fact]
    public void Ancestors_WalkNearestFirstUpToTheRoot()
    {
        Assert.Equal([34000, 32597, 5], ReferenceCache.Ancestors(Lookup, 4000));
        Assert.Equal([5], ReferenceCache.Ancestors(Lookup, 32597));
        Assert.Equal([26], ReferenceCache.Ancestors(Lookup, 5078));
        Assert.Empty(ReferenceCache.Ancestors(Lookup, 5));
        Assert.Empty(ReferenceCache.Ancestors(Lookup, null));
        Assert.Empty(ReferenceCache.Ancestors(Lookup, 999));
    }

    [Fact]
    public void Ancestors_StopAtAParentLoop()
    {
        var looped = new Dictionary<int, ReferenceCache.PlaceInfo>
        {
            [1] = Place(1, "a", PlaceLevel.District, 2),
            [2] = Place(2, "b", PlaceLevel.District, 1),
        };
        Assert.Equal([2], ReferenceCache.Ancestors(id => id is int i && looped.TryGetValue(i, out var p) ? p : null, 1));
    }

    [Fact]
    public void Descendants_CoverEveryLevelBelowButNotTheOtherTree()
    {
        var children = Children(Places.Values);
        var oblast = ReferenceCache.Descendants(children, 5);
        Assert.Equal([4000, 32596, 32597, 34000], oblast.Order());
        Assert.DoesNotContain(5078, oblast); // Kyiv is not under the oblast
        Assert.Equal([4000, 34000], ReferenceCache.Descendants(children, 32597).Order());
        Assert.Empty(ReferenceCache.Descendants(children, 4000));
        Assert.Equal([5078], ReferenceCache.Descendants(children, 26).Order());
    }

    [Fact]
    public void Descendants_SurviveAParentLoop()
    {
        var looped = new[] { Place(1, "a", PlaceLevel.District, 2), Place(2, "b", PlaceLevel.District, 1) };
        Assert.Equal([2], ReferenceCache.Descendants(Children(looped), 1).Order());
    }
}
