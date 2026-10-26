using NUnit.Framework;
using OA.Simulation.Navigation;
using UnityEngine;

public sealed class ShipTrafficHintsTests
{
    private static HexMapRuntime CreateMap()
    {
        var map = new HexMapRuntime(64, 32, 1f);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map.SetWorldCenter(x, y, new Vector2(x + (y & 1) * 0.5f, y * 0.8660254f));
        map.MarkWorldCentersReady();
        return map;
    }

    private static NavigationTraversalMask Mask(HexMapRuntime map)
    {
        return new NavigationTraversalMask(map, map.Version,
            new NavigationProfile(0f, ShipDraftClass.Shallow), (bool[])map.Blocked.Clone());
    }

    [Test]
    public void StationaryBlocker_ProducesForwardTerrainSafeClearanceHints()
    {
        var map = CreateMap();
        var mask = Mask(map);
        Vector2 start = map.GetWorldCenter(5, 16);
        Vector2 end = map.GetWorldCenter(55, 16);
        Vector2 blocker = map.GetWorldCenter(30, 16);
        var traffic = new[] { new NavigationSlotReservation(blocker, 2f) };

        Assert.IsTrue(ShipTrafficHints.TryGetDetour(map, mask, start, end, 1f, traffic,
            out Vector2 approach, out Vector2 departure));
        Assert.Greater(approach.x, start.x);
        Assert.Greater(departure.x, approach.x);
        Assert.Less(departure.x, end.x);
        Vector2[] course = { start, approach, departure, end };
        for (int i = 1; i < course.Length; i++)
        {
            Assert.IsTrue(RouteSegmentUtility.IsSegmentTraversable(map, mask, course[i - 1], course[i]));
            Assert.GreaterOrEqual(DistanceToSegment(blocker, course[i - 1], course[i]), 4f - 0.001f);
        }
    }

    [Test]
    public void RestrictedUpperSide_ChoosesLowerSide_WithoutChangingWaterMask()
    {
        var map = CreateMap();
        for (int y = 19; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map.SetBlocked(x, y, true);
        var mask = Mask(map);
        bool[] originalMap = (bool[])map.Blocked.Clone();
        bool[] originalMask = (bool[])mask.BlockedCells.Clone();
        int version = map.Version;
        Vector2 start = map.GetWorldCenter(5, 16);
        Vector2 end = map.GetWorldCenter(55, 16);
        var traffic = new[] { new NavigationSlotReservation(map.GetWorldCenter(30, 16), 2f) };

        Assert.IsTrue(ShipTrafficHints.TryGetDetour(map, mask, start, end, 1f, traffic,
            out Vector2 approach, out Vector2 departure));
        Assert.Less(approach.y, start.y);
        Assert.Less(departure.y, start.y);
        CollectionAssert.AreEqual(originalMap, map.Blocked);
        CollectionAssert.AreEqual(originalMask, mask.BlockedCells);
        Assert.AreEqual(version, map.Version);
    }

    [Test]
    public void NarrowWaterLane_DeclinesDetour_WhenBothSidesAreRestricted()
    {
        var map = CreateMap();
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (y != 16) map.SetBlocked(x, y, true);
        var traffic = new[] { new NavigationSlotReservation(map.GetWorldCenter(30, 16), 2f) };

        Assert.IsFalse(ShipTrafficHints.TryGetDetour(map, Mask(map), map.GetWorldCenter(5, 16),
            map.GetWorldCenter(55, 16), 1f, traffic, out _, out _));
    }

    [Test]
    public void EmptyOrDistantTraffic_DoesNotAddUnnecessaryHints()
    {
        var map = CreateMap();
        var mask = Mask(map);
        Vector2 start = map.GetWorldCenter(5, 16);
        Vector2 end = map.GetWorldCenter(55, 16);
        Assert.IsFalse(ShipTrafficHints.TryGetDetour(map, mask, start, end, 1f,
            System.Array.Empty<NavigationSlotReservation>(), out _, out _));
        Assert.IsFalse(ShipTrafficHints.TryGetDetour(map, mask, start, end, 1f,
            new[] { new NavigationSlotReservation(map.GetWorldCenter(30, 2), 2f) }, out _, out _));
    }

    [Test]
    public void OverlappingEndpoints_DoNotProduceBackwardsOrLoopingDetours()
    {
        var map = CreateMap();
        var mask = Mask(map);
        Vector2 start = map.GetWorldCenter(5, 16);
        Vector2 end = map.GetWorldCenter(55, 16);
        Assert.IsFalse(ShipTrafficHints.TryGetDetour(map, mask, start, end, 1f,
            new[] { new NavigationSlotReservation(start + Vector2.right, 2f) }, out _, out _));
        Assert.IsFalse(ShipTrafficHints.TryGetDetour(map, mask, start, end, 1f,
            new[] { new NavigationSlotReservation(end - Vector2.right, 2f) }, out _, out _));
        Assert.IsFalse(ShipTrafficHints.TryGetDetour(map, mask, start, start, 1f,
            new[] { new NavigationSlotReservation(start, 2f) }, out _, out _));
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segment.sqrMagnitude);
        return Vector2.Distance(point, start + segment * t);
    }
}
