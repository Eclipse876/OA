using NUnit.Framework;
using OA.Simulation.Navigation;
using UnityEngine;

public sealed class HexMapGeneratorDeterminismTests
{
    [Test]
    public void Generator_IsDeterministic_WithSameSeed()
    {
        HexMapRuntime mapA = new HexMapRuntime(34, 24, 1.1f);
        HexMapRuntime mapB = new HexMapRuntime(34, 24, 1.1f);

        HexMapGenerator generator = new HexMapGenerator();

        int seed = 123456;
        Vector2Int start = new Vector2Int(2, 2);
        Vector2Int goal = new Vector2Int(31, 21);

        generator.Generate(mapA, seed, 0.2f, 0f, 3, start, goal);
        generator.Generate(mapB, seed, 0.2f, 0f, 3, start, goal);

        int count = mapA.Width * mapA.Height;
        for (int i = 0; i < count; i++)
        {
            Assert.AreEqual(mapA.Blocked[i], mapB.Blocked[i], $"Blocked mismatch at index {i}");
            Assert.AreEqual(mapA.MoveCost[i], mapB.MoveCost[i], $"Cost mismatch at index {i}");
        }
    }

    [Test]
    public void Generator_ClearsGuaranteedCells()
    {
        HexMapRuntime map = new HexMapRuntime(34, 24, 1.1f);
        HexMapGenerator generator = new HexMapGenerator();

        Vector2Int start = new Vector2Int(2, 2);
        Vector2Int goal = new Vector2Int(31, 21);

        generator.Generate(map, 42, 0.35f, 0f, 3, start, goal);

        Assert.IsTrue(map.IsWalkable(start.x, start.y));
        Assert.IsTrue(map.IsWalkable(goal.x, goal.y));
    }
}
