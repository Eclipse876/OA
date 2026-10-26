using System.Collections.Generic;
using NUnit.Framework;
using OA.Simulation.Navigation;
using UnityEngine;

public sealed class UnitSelectionAndSlotsTests
{
    [Test]
    public void Selection_StartsEmpty_ReplacesAndAddsWithoutDuplicates()
    {
        var selection = new UnitSelectionSet<object>();
        var first = new object();
        var second = new object();
        var third = new object();
        Assert.AreEqual(0, selection.Count);
        selection.Select(first, false);
        selection.Select(second, true);
        selection.Select(first, true);
        CollectionAssert.AreEqual(new[] { first, second }, selection.Items);
        selection.Select(third, false);
        CollectionAssert.AreEqual(new[] { third }, selection.Items);
        selection.Select((object)null, true);
        Assert.IsTrue(selection.Contains(third));
        selection.Select((object)null, false);
        Assert.AreEqual(0, selection.Count);
    }

    [Test]
    public void Selection_BoxReplacement_HandlesEmptyInputAndItsOwnItems()
    {
        var selection = new UnitSelectionSet<object>();
        var first = new object();
        var second = new object();
        selection.Select(new[] { first, null, first, second }, false);
        selection.Select(selection.Items, false);
        CollectionAssert.AreEqual(new[] { first, second }, selection.Items);
        selection.Select(System.Array.Empty<object>(), true);
        Assert.AreEqual(2, selection.Count);
        Assert.IsTrue(selection.Remove(first));
        Assert.IsFalse(selection.Remove(first));
        Assert.IsFalse(selection.Contains(first));
        selection.Select((IEnumerable<object>)null, false);
        Assert.AreEqual(0, selection.Count);
        selection.Select(first, false);
        selection.Clear();
        Assert.AreEqual(0, selection.Count);
    }

    [Test]
    public void SpawnBounds_RejectOffMapAndNonFinitePositions_InsteadOfSnappingToEdge()
    {
        HexMapRuntime map = Map();
        Assert.IsTrue(NavigationDestinationSlots.IsWorldInsideMap(map, map.GetWorldCenter(2, 3)));
        Vector2 outside = map.GetWorldCenter(0, 0) - Vector2.one * map.CellSize;
        Assert.IsTrue(map.TryWorldToCell(outside, out _), "Nearest-cell lookup itself deliberately accepts distant points.");
        Assert.IsFalse(NavigationDestinationSlots.IsWorldInsideMap(map, outside));
        Assert.IsFalse(NavigationDestinationSlots.IsWorldInsideMap(map, new Vector2(float.NaN, 0f)));
        Assert.IsTrue(NavigationDestinationSlots.IsWorldInsideMap(map,
            map.GetWorldCenter(4, 4) + new Vector2(0.45f, 0.25f) * map.CellSize));
        Assert.IsFalse(NavigationDestinationSlots.IsWorldInsideMap(map,
            map.GetWorldCenter(0, 0) - Vector2.right * map.CellSize * 0.51f));
    }

    [Test]
    public void Slots_SeparateMixedHullRadii_AndAreDeterministic()
    {
        HexMapRuntime map = Map();
        NavigationTraversalMask mask = Mask(map);
        var reservations = new List<NavigationSlotReservation>();
        Vector2 desired = map.GetWorldCenter(12, 8);
        float[] radii = { 3f, 6f, 3f, 6f, 3f, 3f };
        foreach (float radius in radii)
        {
            Assert.IsTrue(NavigationDestinationSlots.TryFindSlot(map, mask, new Vector2Int(2, 8),
                desired, radius, reservations, out Vector2 point));
            Assert.IsTrue(NavigationDestinationSlots.TryFindSlot(map, mask, new Vector2Int(2, 8),
                desired, radius, reservations, out Vector2 repeated));
            Assert.AreEqual(point, repeated);
            foreach (NavigationSlotReservation previous in reservations)
                Assert.GreaterOrEqual(Vector2.Distance(point, previous.Position), radius + previous.Radius);
            Assert.IsTrue(map.TryWorldToCell(point, out Vector2Int cell));
            Assert.IsFalse(mask.IsBlocked(cell));
            reservations.Add(new NavigationSlotReservation(point, radius));
        }
    }

    [Test]
    public void Slots_LandAndDisconnectedTargets_StayInStartComponent()
    {
        HexMapRuntime map = Map();
        for (int y = 0; y < map.Height; y++) map.SetBlocked(10, y, true);
        NavigationTraversalMask mask = Mask(map);
        var reservations = new List<NavigationSlotReservation>();
        bool[] originalBlocked = (bool[])mask.BlockedCells.Clone();
        for (int i = 0; i < 5; i++)
        {
            Assert.IsTrue(NavigationDestinationSlots.TryFindSlot(map, mask, new Vector2Int(2, 8),
                map.GetWorldCenter(i == 0 ? 10 : 18, 8), 3f, reservations, out Vector2 point));
            Assert.IsTrue(map.TryWorldToCell(point, out Vector2Int cell));
            Assert.Less(cell.x, 10);
            Assert.IsFalse(mask.IsBlocked(cell));
            foreach (NavigationSlotReservation previous in reservations)
                Assert.GreaterOrEqual(Vector2.Distance(point, previous.Position), 3f + previous.Radius);
            reservations.Add(new NavigationSlotReservation(point, 3f));
        }
        CollectionAssert.AreEqual(originalBlocked, mask.BlockedCells);
    }

    [Test]
    public void Slots_RespectPerShipMask_AndKeepShorelineStopsInsideSafeOffset()
    {
        HexMapRuntime map = Map();
        bool[] shipBlocked = new bool[map.Width * map.Height];
        shipBlocked[map.GetIndex(9, 8)] = true;
        var mask = new NavigationTraversalMask(map, map.Version,
            new NavigationProfile(0f, ShipDraftClass.Deep), shipBlocked);
        Vector2 center = map.GetWorldCenter(8, 8);
        Vector2 desired = center + Vector2.right * map.CellSize * 0.45f;
        Assert.IsTrue(map.IsWalkable(9, 8), "The ship mask must be honored independently of terrain walkability.");
        Assert.IsTrue(NavigationDestinationSlots.TryFindSlot(map, mask, new Vector2Int(2, 8),
            desired, 3f, null, out Vector2 slot));
        Assert.Less(Vector2.Distance(center, slot), map.CellSize * 0.12f);
        Assert.IsTrue(NavigationDestinationSlots.TryFindSlot(map, mask, new Vector2Int(2, 8),
            map.GetWorldCenter(9, 8), 3f, null, out Vector2 restricted));
        Assert.IsTrue(map.TryWorldToCell(restricted, out Vector2Int cell));
        Assert.IsFalse(mask.IsBlocked(cell));
        Assert.AreNotEqual(new Vector2Int(9, 8), cell);
    }

    [Test]
    public void Slots_ReturnFalseWhenNoDistinctEndpointFits_AndRejectStaleMasks()
    {
        HexMapRuntime map = Map();
        NavigationTraversalMask mask = Mask(map);
        var reservations = new[] { new NavigationSlotReservation(map.GetWorldCenter(12, 8), 10000f) };
        Assert.IsFalse(NavigationDestinationSlots.TryFindSlot(map, mask, new Vector2Int(2, 8),
            map.GetWorldCenter(12, 8), 3f, reservations, out _));
        map.SetBlocked(20, 10, true);
        Assert.IsFalse(NavigationDestinationSlots.TryFindSlot(map, mask, new Vector2Int(2, 8),
            map.GetWorldCenter(12, 8), 3f, null, out _));
    }

    private static HexMapRuntime Map()
    {
        var map = new HexMapRuntime(24, 16, 32.22222f);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map.SetWorldCenter(x, y, new Vector2((x + (y % 2) * 0.5f) * map.CellSize,
                    y * map.CellSize * 0.8660254f));
        map.MarkWorldCentersReady();
        return map;
    }

    private static NavigationTraversalMask Mask(HexMapRuntime map)
    {
        return new NavigationTraversalMask(map, map.Version,
            new NavigationProfile(0f, ShipDraftClass.Shallow), (bool[])map.Blocked.Clone());
    }
}
