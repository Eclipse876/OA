using System;
using System.Collections.Generic;
using UnityEngine;

namespace OA.TechDemo
{
    public sealed class RandomGridMapGenerator
    {
        private readonly List<Vector2Int> _scratchFrontier = new List<Vector2Int>(2048);
        private readonly HashSet<int> _scratchVisited = new HashSet<int>();

        public void Generate(
            GridMap map,
            int seed,
            float obstacleChance,
            float roughWaterChance,
            int smoothingPasses,
            Vector2Int guaranteedStart,
            Vector2Int guaranteedGoal)
        {
            var random = new System.Random(seed);
            float clampedObstacleChance = Mathf.Clamp(obstacleChance, 0.05f, 0.45f);
            float clampedRoughChance = Mathf.Clamp01(roughWaterChance);
            int clampedSmoothingPasses = Mathf.Clamp(smoothingPasses, 0, 8);

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    bool isBorder = x == 0 || y == 0 || x == map.Width - 1 || y == map.Height - 1;
                    bool blocked = isBorder || random.NextDouble() < clampedObstacleChance;

                    map.SetBlocked(x, y, blocked);
                    map.SetMoveCost(x, y, random.NextDouble() < clampedRoughChance ? 1.75f : 1f);
                }
            }

            for (int i = 0; i < clampedSmoothingPasses; i++)
            {
                ApplySmoothingPass(map);
            }

            ClearCircle(map, guaranteedStart, 2);
            ClearCircle(map, guaranteedGoal, 2);

            EnsureGuaranteedPath(map, guaranteedStart, guaranteedGoal);
        }

        private static void ApplySmoothingPass(GridMap map)
        {
            bool[] nextBlocked = new bool[map.Width * map.Height];

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    bool isBorder = x == 0 || y == 0 || x == map.Width - 1 || y == map.Height - 1;
                    if (isBorder)
                    {
                        nextBlocked[map.GetIndex(x, y)] = true;
                        continue;
                    }

                    int blockedNeighbors = CountBlockedNeighbors(map, x, y);
                    bool current = map.IsBlocked(x, y);

                    if (blockedNeighbors > 4)
                    {
                        nextBlocked[map.GetIndex(x, y)] = true;
                    }
                    else if (blockedNeighbors < 4)
                    {
                        nextBlocked[map.GetIndex(x, y)] = false;
                    }
                    else
                    {
                        nextBlocked[map.GetIndex(x, y)] = current;
                    }
                }
            }

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    map.SetBlocked(x, y, nextBlocked[map.GetIndex(x, y)]);
                }
            }
        }

        private static int CountBlockedNeighbors(GridMap map, int centerX, int centerY)
        {
            int count = 0;

            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    if (x == 0 && y == 0)
                    {
                        continue;
                    }

                    if (map.IsBlocked(centerX + x, centerY + y))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static void ClearCircle(GridMap map, Vector2Int center, int radius)
        {
            int radiusSq = radius * radius;

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y > radiusSq)
                    {
                        continue;
                    }

                    int cellX = center.x + x;
                    int cellY = center.y + y;
                    if (!map.InBounds(cellX, cellY))
                    {
                        continue;
                    }

                    map.SetBlocked(cellX, cellY, false);
                    map.SetMoveCost(cellX, cellY, 1f);
                }
            }
        }

        private void EnsureGuaranteedPath(GridMap map, Vector2Int start, Vector2Int goal)
        {
            if (!map.InBounds(start.x, start.y) || !map.InBounds(goal.x, goal.y))
            {
                return;
            }

            if (IsReachable(map, start, goal))
            {
                return;
            }

            foreach (Vector2Int cell in RasterizeLine(start, goal))
            {
                map.SetBlocked(cell.x, cell.y, false);
                map.SetMoveCost(cell.x, cell.y, 1f);

                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        int nx = cell.x + x;
                        int ny = cell.y + y;
                        if (!map.InBounds(nx, ny))
                        {
                            continue;
                        }

                        map.SetBlocked(nx, ny, false);
                    }
                }
            }
        }

        private bool IsReachable(GridMap map, Vector2Int start, Vector2Int goal)
        {
            _scratchFrontier.Clear();
            _scratchVisited.Clear();

            int startIndex = map.GetIndex(start.x, start.y);
            _scratchFrontier.Add(start);
            _scratchVisited.Add(startIndex);

            int cursor = 0;
            while (cursor < _scratchFrontier.Count)
            {
                Vector2Int current = _scratchFrontier[cursor++];
                if (current == goal)
                {
                    return true;
                }

                for (int i = 0; i < NeighborOffsets.Length; i++)
                {
                    Vector2Int offset = NeighborOffsets[i];
                    int nx = current.x + offset.x;
                    int ny = current.y + offset.y;

                    if (!map.IsWalkable(nx, ny))
                    {
                        continue;
                    }

                    int index = map.GetIndex(nx, ny);
                    if (_scratchVisited.Contains(index))
                    {
                        continue;
                    }

                    _scratchVisited.Add(index);
                    _scratchFrontier.Add(new Vector2Int(nx, ny));
                }
            }

            return false;
        }

        private static IEnumerable<Vector2Int> RasterizeLine(Vector2Int a, Vector2Int b)
        {
            int x0 = a.x;
            int y0 = a.y;
            int x1 = b.x;
            int y1 = b.y;

            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                yield return new Vector2Int(x0, y0);

                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = err * 2;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private static readonly Vector2Int[] NeighborOffsets =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };
    }
}
