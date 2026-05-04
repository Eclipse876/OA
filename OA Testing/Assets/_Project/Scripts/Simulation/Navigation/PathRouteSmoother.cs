using System.Collections.Generic;
using UnityEngine;

namespace OA.Simulation.Navigation
{
    public static class PathRouteSmoother
    {
        public static void BuildRoute(
            HexMapRuntime map,
            bool[] blockedMask,
            IReadOnlyList<Vector2Int> cellPath,
            Vector2 startWorld,
            Vector2 destinationWorld,
            List<Vector2> routeOut,
            List<Vector2> scratchCandidates,
            float sampleFactor = 0.2f)
        {
            if (routeOut == null || scratchCandidates == null)
            {
                return;
            }

            routeOut.Clear();
            scratchCandidates.Clear();

            if (map == null || cellPath == null || cellPath.Count == 0)
            {
                return;
            }

            Vector2Int destinationCell = cellPath[cellPath.Count - 1];
            Vector2 destinationCenter = map.GetWorldCenter(destinationCell.x, destinationCell.y);
            Vector2 finalDestination = ClampPointToCell(destinationWorld, destinationCenter, map.CellSize * 0.4f);

            scratchCandidates.Add(startWorld);

            for (int i = 1; i < cellPath.Count; i++)
            {
                Vector2Int c = cellPath[i];
                scratchCandidates.Add(map.GetWorldCenter(c.x, c.y));
            }

            scratchCandidates.Add(finalDestination);

            routeOut.Add(startWorld);

            int anchor = 0;
            while (anchor < scratchCandidates.Count - 1)
            {
                int farthestReachable = anchor + 1;
                for (int candidate = farthestReachable + 1; candidate < scratchCandidates.Count; candidate++)
                {
                    if (IsSegmentTraversable(
                        map,
                        blockedMask,
                        scratchCandidates[anchor],
                        scratchCandidates[candidate],
                        sampleFactor))
                    {
                        farthestReachable = candidate;
                    }
                }

                Vector2 point = scratchCandidates[farthestReachable];

                if (Vector2.Distance(routeOut[routeOut.Count - 1], point) > 0.0005f)
                {
                    routeOut.Add(point);
                }

                anchor = farthestReachable;
            }

            if (routeOut.Count < 2)
            {
                routeOut.Clear();
                routeOut.Add(startWorld);
                routeOut.Add(finalDestination);
            }
        }

        private static bool IsSegmentTraversable(
            HexMapRuntime map,
            bool[] blockedMask,
            Vector2 a,
            Vector2 b,
            float sampleFactor)
        {
            float distance = Vector2.Distance(a, b);
            if (distance < 0.0001f)
            {
                return true;
            }

            float sampleStep = Mathf.Max(0.04f, map.CellSize * Mathf.Clamp(sampleFactor, 0.05f, 1f));
            int samples = Mathf.Max(2, Mathf.CeilToInt(distance / sampleStep));

            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector2 p = Vector2.Lerp(a, b, t);

                if (!map.TryWorldToCell(p, out Vector2Int cell))
                {
                    return false;
                }

                if (!IsTraversable(map, blockedMask, cell))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsTraversable(HexMapRuntime map, bool[] blockedMask, Vector2Int cell)
        {
            if (!map.InBounds(cell.x, cell.y))
            {
                return false;
            }

            if (blockedMask == null || blockedMask.Length == 0)
            {
                return map.IsWalkable(cell.x, cell.y);
            }

            int index = map.GetIndex(cell.x, cell.y);
            return index >= 0 && index < blockedMask.Length && !blockedMask[index];
        }

        private static Vector2 ClampPointToCell(Vector2 point, Vector2 cellCenter, float maxOffset)
        {
            Vector2 delta = point - cellCenter;
            if (delta.sqrMagnitude > maxOffset * maxOffset)
            {
                delta = delta.normalized * maxOffset;
            }

            return cellCenter + delta;
        }
    }
}
