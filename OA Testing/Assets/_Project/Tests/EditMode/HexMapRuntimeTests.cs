using NUnit.Framework;
using OA.Simulation.Navigation;
using UnityEngine;

public sealed class HexMapRuntimeTests
{
    [Test]
    public void NeighborCount_IsCorrect_ForCenterAndCorner()
    {
        HexMapRuntime map = new HexMapRuntime(34, 24, 1.1f);
        Vector2Int[] buffer = new Vector2Int[6];

        int centerCount = map.GetNeighborCount(10, 10, buffer);
        int cornerCount = map.GetNeighborCount(0, 0, buffer);

        Assert.AreEqual(6, centerCount);
        Assert.AreEqual(2, cornerCount);
    }

    [Test]
    public void WorldToCell_RequiresWorldCentersReady()
    {
        HexMapRuntime map = new HexMapRuntime(10, 10, 1f);

        bool beforeReady = map.TryWorldToCell(Vector2.zero, out _);
        Assert.IsFalse(beforeReady);

        map.SetWorldCenter(0, 0, new Vector2(0f, 0f));
        map.SetWorldCenter(1, 0, new Vector2(1f, 0f));
        map.MarkWorldCentersReady();

        bool afterReady = map.TryWorldToCell(new Vector2(0.1f, 0.05f), out Vector2Int cell);
        Assert.IsTrue(afterReady);
        Assert.AreEqual(new Vector2Int(0, 0), cell);
    }
}
