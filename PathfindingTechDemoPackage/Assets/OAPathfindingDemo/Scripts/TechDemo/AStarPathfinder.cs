using System.Collections.Generic;
using UnityEngine;

namespace OA.TechDemo
{
    public sealed class AStarPathfinder
    {
        public bool TryFindPath(
            GridMap map,
            Vector2Int start,
            Vector2Int goal,
            float safetyRadiusWorld,
            List<Vector2Int> outPath)
        {
            outPath.Clear();

            if (!map.InBounds(start.x, start.y) || !map.InBounds(goal.x, goal.y))
            {
                return false;
            }

            float safetyRadiusCells = Mathf.Max(0f, safetyRadiusWorld / map.CellSize);
            if (!CanTraverse(map, start.x, start.y, safetyRadiusCells) ||
                !CanTraverse(map, goal.x, goal.y, safetyRadiusCells))
            {
                return false;
            }

            int cellCount = map.Width * map.Height;
            float[] gScore = new float[cellCount];
            float[] fScore = new float[cellCount];
            int[] cameFrom = new int[cellCount];
            bool[] openLookup = new bool[cellCount];
            bool[] closed = new bool[cellCount];
            var openSet = new List<int>(512);

            for (int i = 0; i < cellCount; i++)
            {
                gScore[i] = float.PositiveInfinity;
                fScore[i] = float.PositiveInfinity;
                cameFrom[i] = -1;
            }

            int startIndex = map.GetIndex(start.x, start.y);
            int goalIndex = map.GetIndex(goal.x, goal.y);

            gScore[startIndex] = 0f;
            fScore[startIndex] = Heuristic(start, goal);
            openSet.Add(startIndex);
            openLookup[startIndex] = true;

            while (openSet.Count > 0)
            {
                int bestOpenSlot = 0;
                int currentIndex = openSet[0];
                float currentBestF = fScore[currentIndex];

                for (int i = 1; i < openSet.Count; i++)
                {
                    int index = openSet[i];
                    if (fScore[index] < currentBestF)
                    {
                        currentBestF = fScore[index];
                        currentIndex = index;
                        bestOpenSlot = i;
                    }
                }

                openSet.RemoveAt(bestOpenSlot);
                openLookup[currentIndex] = false;

                if (currentIndex == goalIndex)
                {
                    ReconstructPath(map, cameFrom, goalIndex, outPath);
                    SimplifyPath(map, outPath, safetyRadiusCells);
                    return true;
                }

                closed[currentIndex] = true;
                Vector2Int currentCell = IndexToCell(currentIndex, map.Width);

                for (int i = 0; i < NeighborOffsets.Length; i++)
                {
                    Vector2Int offset = NeighborOffsets[i];
                    int nx = currentCell.x + offset.x;
                    int ny = currentCell.y + offset.y;

                    if (!CanTraverse(map, nx, ny, safetyRadiusCells))
                    {
                        continue;
                    }

                    bool diagonal = offset.x != 0 && offset.y != 0;
                    if (diagonal)
                    {
                        if (!CanTraverse(map, currentCell.x + offset.x, currentCell.y, safetyRadiusCells) ||
                            !CanTraverse(map, currentCell.x, currentCell.y + offset.y, safetyRadiusCells))
                        {
                            continue;
                        }
                    }

                    int neighborIndex = map.GetIndex(nx, ny);
                    if (closed[neighborIndex])
                    {
                        continue;
                    }

                    float moveCost = map.GetMoveCost(nx, ny);
                    float stepCost = diagonal ? 1.41421356f : 1f;
                    float tentativeG = gScore[currentIndex] + stepCost * moveCost;

                    if (tentativeG >= gScore[neighborIndex])
                    {
                        continue;
                    }

                    cameFrom[neighborIndex] = currentIndex;
                    gScore[neighborIndex] = tentativeG;
                    fScore[neighborIndex] = tentativeG + Heuristic(new Vector2Int(nx, ny), goal);

                    if (!openLookup[neighborIndex])
                    {
                        openLookup[neighborIndex] = true;
                        openSet.Add(neighborIndex);
                    }
                }
            }

            return false;
        }

        private static void ReconstructPath(GridMap map, int[] cameFrom, int goalIndex, List<Vector2Int> outPath)
        {
            int current = goalIndex;
            while (current >= 0)
            {
                outPath.Add(IndexToCell(current, map.Width));
                current = cameFrom[current];
            }

            outPath.Reverse();
        }

        private static float Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            int diagonal = Mathf.Min(dx, dy);
            int straight = Mathf.Max(dx, dy) - diagonal;
            return diagonal * 1.41421356f + straight;
        }

        private static Vector2Int IndexToCell(int index, int width)
        {
            int x = index % width;
            int y = index / width;
            return new Vector2Int(x, y);
        }

        private static bool CanTraverse(GridMap map, int x, int y, float safetyRadiusCells)
        {
            if (!map.IsWalkable(x, y))
            {
                return false;
            }

            if (safetyRadiusCells <= 0.001f)
            {
                return true;
            }

            int radius = Mathf.CeilToInt(safetyRadiusCells);
            float radiusSq = safetyRadiusCells * safetyRadiusCells;

            for (int cy = -radius; cy <= radius; cy++)
            {
                for (int cx = -radius; cx <= radius; cx++)
                {
                    if (cx * cx + cy * cy > radiusSq)
                    {
                        continue;
                    }

                    if (map.IsBlocked(x + cx, y + cy))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void SimplifyPath(GridMap map, List<Vector2Int> path, float safetyRadiusCells)
        {
            if (path.Count < 3)
            {
                return;
            }

            int writeIndex = 1;
            int anchor = 0;

            while (anchor < path.Count - 1)
            {
                int furthestVisible = anchor + 1;

                for (int test = anchor + 2; test < path.Count; test++)
                {
                    if (HasClearLine(map, path[anchor], path[test], safetyRadiusCells))
                    {
                        furthestVisible = test;
                    }
                    else
                    {
                        break;
                    }
                }

                path[writeIndex++] = path[furthestVisible];
                anchor = furthestVisible;
            }

            if (writeIndex < path.Count)
            {
                path.RemoveRange(writeIndex, path.Count - writeIndex);
            }
        }

        private static bool HasClearLine(GridMap map, Vector2Int a, Vector2Int b, float safetyRadiusCells)
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
                if (!CanTraverse(map, x0, y0, safetyRadiusCells))
                {
                    return false;
                }

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

            return true;
        }

        private static readonly Vector2Int[] NeighborOffsets =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(1, 1),
            new Vector2Int(1, -1),
            new Vector2Int(-1, 1),
            new Vector2Int(-1, -1)
        };
    }
}
