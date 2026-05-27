// HexMapGenerator.cs:
// This is *supposed* to make the ocean map look intentional. Right now it
// rolls obstacles/rough water, smooths the spicy bits, and then cheats a little to make sure nothing is trapped.
using System.Collections.Generic;
using UnityEngine;

namespace OA.Simulation.Navigation
{
    // HexMapGenerator is the procedural map maker.
    // Seed goes in, navigable-ish hex soup comes out.
    public sealed class HexMapGenerator
    {
        // Rough water is passable, just expensive enough that paths prefer calmer tiles.
        private const float RoughMoveCost = 3.2f;

        // Reused scratch buffers so reachability checks do not keep allocating tiny garbage.
        private readonly List<Vector2Int> scratchFrontier = new List<Vector2Int>(2048);
        private readonly HashSet<int> scratchVisited = new HashSet<int>();
        private readonly Vector2Int[] neighborBuffer = new Vector2Int[6];

        // Kicks off a deterministic map roll, then clears enough space for spawn/goal.
        public void Generate(
            HexMapRuntime map,
            int seed,
            float obstacleChance,
            float roughWaterChance,
            int smoothingPasses,
            Vector2Int guaranteedStart,
            Vector2Int guaranteedGoal)
        {
            if (map == null)
            {
                return;
            }

            // Clamp the designer-facing knobs before they can make an unusable map.
            System.Random random = new System.Random(seed);
            float clampedObstacleChance = Mathf.Clamp(obstacleChance, 0.05f, 0.45f);
            float clampedRoughChance = Mathf.Clamp(roughWaterChance, 0f, 0.85f);
            int clampedSmoothingPasses = Mathf.Clamp(smoothingPasses, 0, 8);

            // First pass: roll blocked cells and rough-water costs from the seed.
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    bool isBlocked = random.NextDouble() < clampedObstacleChance;

                    map.SetBlocked(x, y, isBlocked);

                    float cost = isBlocked
                        ? 1f
                        : (random.NextDouble() < clampedRoughChance ? RoughMoveCost : 1f);

                    map.SetMoveCost(x, y, cost);
                }
            }

            // Smoothing turns noisy single-cell static into bigger obstacle blobs.
            for (int i = 0; i < clampedSmoothingPasses; i++)
            {
                ApplySmoothingPass(map);
            }

            ApplyDepthClasses(map);

            // Always clear breathing room around the guaranteed endpoints, then carve if needed.
            ClearHexRadius(map, guaranteedStart, 2);
            ClearHexRadius(map, guaranteedGoal, 2);
            EnsureGuaranteedPath(map, guaranteedStart, guaranteedGoal);
        }

        // Runs one cellular-automata-ish cleanup pass over the blocked cells.
        private void ApplySmoothingPass(HexMapRuntime map)
        {
            bool[] nextBlocked = new bool[map.Width * map.Height];

            // Decide next blocked states without mutating the map mid-pass.
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int blockedNeighbors = CountBlockedNeighbors(map, x, y);
                    bool current = map.IsBlocked(x, y);

                    if (blockedNeighbors >= 4)
                    {
                        nextBlocked[map.GetIndex(x, y)] = true;
                    }
                    else if (blockedNeighbors <= 2)
                    {
                        nextBlocked[map.GetIndex(x, y)] = false;
                    }
                    else
                    {
                        nextBlocked[map.GetIndex(x, y)] = current;
                    }
                }
            }

            // Apply the decided blocked state and keep passable cells at a valid movement cost.
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    bool blocked = nextBlocked[map.GetIndex(x, y)];
                    map.SetBlocked(x, y, blocked);

                    if (blocked)
                    {
                        map.SetMoveCost(x, y, 1f);
                    }
                    else
                    {
                        map.SetMoveCost(x, y, Mathf.Max(1f, map.GetMoveCost(x, y)));
                    }
                }
            }
        }

        private void ApplyDepthClasses(HexMapRuntime map)
        {
            WaterDepthClass[] nextDepth = new WaterDepthClass[map.Width * map.Height];

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int index = map.GetIndex(x, y);

                    if (map.IsBlocked(x, y))
                    {
                        nextDepth[index] = WaterDepthClass.Shallow;
                    }
                    else 
                    {
                        nextDepth[index] = HasBlockedNeighbor(map, x, y)
                            ? WaterDepthClass.Shallow
                            : WaterDepthClass.Deep;
                    }
                }
            }

            for (int y = 0;y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    map.SetDepthClass(x, y, nextDepth[map.GetIndex(x, y)]);
                }
            }
        }

        // Counts blocked neighbors around one hex so smoothing knows whether it should flip.
        private int CountBlockedNeighbors(HexMapRuntime map, int centerX, int centerY)
        {
            int count = 0;
            int neighborCount = map.GetNeighborCount(centerX, centerY, neighborBuffer);

            for (int i = 0; i < neighborCount; i++)
            {
                Vector2Int n = neighborBuffer[i];
                if (map.IsBlocked(n.x, n.y))
                {
                    count++;
                }
            }

            return count;
        }

        private bool HasBlockedNeighbor(HexMapRuntime map, int centerX, int centerY)
        {
            int neighborCount = map.GetNeighborCount(centerX, centerY, neighborBuffer);
        
            for (int i = 0; i < neighborCount; i++)
            {
                Vector2Int n = neighborBuffer[i];
                if (map.IsBlocked(n.x, n.y))
                {
                    return true;
                }
            }
            
            return false;
        }

        // Clears a small hex-shaped patch around important cells like spawn and goal.
        private static void ClearHexRadius(HexMapRuntime map, Vector2Int center, int radius)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    Vector2Int cell = new Vector2Int(center.x + x, center.y + y);
                    if (!map.InBounds(cell.x, cell.y))
                    {
                        continue;
                    }

                    if (HexMapRuntime.HexDistance(center, cell) > radius)
                    {
                        continue;
                    }

                    map.SetBlocked(cell.x, cell.y, false);
                    map.SetMoveCost(cell.x, cell.y, 1f);
                    map.SetDepthClass(cell.x, cell.y, WaterDepthClass.Deep);
                }
            }
        }

        // Makes sure generation did not accidentally split start and goal into separate oceans.
        private void EnsureGuaranteedPath(HexMapRuntime map, Vector2Int start, Vector2Int goal)
        {
            if (!map.InBounds(start.x, start.y) || !map.InBounds(goal.x, goal.y))
            {
                return;
            }

            if (IsReachable(map, start, goal))
            {
                return;
            }

            // If BFS says the map is split, carve a simple hex-line corridor between endpoints.
            Vector2Int? previous = null;

            foreach (Vector2Int cell in HexLine(start, goal))
            {
                if (previous.HasValue && previous.Value == cell)
                {
                    continue;
                }

                previous = cell;

                if (!map.InBounds(cell.x, cell.y))
                {
                    continue;
                }

                map.SetBlocked(cell.x, cell.y, false);
                map.SetMoveCost(cell.x, cell.y, 1f);
                ClearHexRadius(map, cell, 1);
            }
        }

        // Breadth-first search used as a quick "can these two cells connect?" check.
        private bool IsReachable(HexMapRuntime map, Vector2Int start, Vector2Int goal)
        {
            scratchFrontier.Clear();
            scratchVisited.Clear();

            scratchFrontier.Add(start);
            scratchVisited.Add(map.GetIndex(start.x, start.y));

            // Cursor-style BFS over a list keeps the queue simple and allocation-light.
            int cursor = 0;
            while (cursor < scratchFrontier.Count)
            {
                Vector2Int current = scratchFrontier[cursor++];

                if (current == goal)
                {
                    return true;
                }

                int neighbors = map.GetNeighborCount(current.x, current.y, neighborBuffer);
                for (int i = 0; i < neighbors; i++)
                {
                    Vector2Int next = neighborBuffer[i];

                    if (!map.IsWalkable(next.x, next.y))
                    {
                        continue;
                    }

                    int index = map.GetIndex(next.x, next.y);
                    if (!scratchVisited.Add(index))
                    {
                        continue;
                    }

                    scratchFrontier.Add(next);
                }
            }

            return false;
        }

        // Walks a straight-ish line between two hex cells using cube-coordinate interpolation.
        private static IEnumerable<Vector2Int> HexLine(Vector2Int a, Vector2Int b)
        {
            HexMapRuntime.ToCube(a, out int ax, out int ay, out int az);
            HexMapRuntime.ToCube(b, out int bx, out int by, out int bz);

            int steps = Mathf.Max(1, HexMapRuntime.HexDistance(a, b));

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float x = Mathf.Lerp(ax, bx, t);
                float y = Mathf.Lerp(ay, by, t);
                float z = Mathf.Lerp(az, bz, t);

                CubeRound(x, y, z, out int rx, out int ry, out int rz);
                yield return HexMapRuntime.CubeToOffset(rx, rz);
            }
        }

        // Rounds cube coordinates while repairing the axis with the biggest rounding error.
        private static void CubeRound(float x, float y, float z, out int rx, out int ry, out int rz)
        {
            rx = Mathf.RoundToInt(x);
            ry = Mathf.RoundToInt(y);
            rz = Mathf.RoundToInt(z);

            float xDiff = Mathf.Abs(rx - x);
            float yDiff = Mathf.Abs(ry - y);
            float zDiff = Mathf.Abs(rz - z);

            if (xDiff > yDiff && xDiff > zDiff)
            {
                rx = -ry - rz;
            }
            else if (yDiff > zDiff)
            {
                ry = -rx - rz;
            }
            else
            {
                rz = -rx - ry;
            }
        }
    }
}
