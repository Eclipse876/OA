using System.Collections.Generic;
using UnityEngine;

namespace OA.LegacyTechDemo
{
    public sealed class LegacyGridPathfinder
    {
        public bool TryFindPath(
            LegacyGridMap map,
            Vector2Int start,
            Vector2Int goal,
            List<Vector2Int> outPath)
        {
            outPath.Clear();

            if (!map.IsWalkable(start.x, start.y) || !map.IsWalkable(goal.x, goal.y))
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
                    return true;
                }

                closed[currentIndex] = true;
                Vector2Int currentCell = IndexToCell(currentIndex, map.Width);

                for (int i = 0; i < NeighborOffsets.Length; i++)
                {
                    Vector2Int offset = NeighborOffsets[i];
                    int nx = currentCell.x + offset.x;
                    int ny = currentCell.y + offset.y;

                    if (!map.IsWalkable(nx, ny))
                    {
                        continue;
                    }

                    int neighborIndex = map.GetIndex(nx, ny);
                    if (closed[neighborIndex])
                    {
                        continue;
                    }

                    float tentativeG = gScore[currentIndex] + 1f;
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

        private static void ReconstructPath(LegacyGridMap map, int[] cameFrom, int goalIndex, List<Vector2Int> outPath)
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
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }

        private static Vector2Int IndexToCell(int index, int width)
        {
            return new Vector2Int(index % width, index / width);
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
