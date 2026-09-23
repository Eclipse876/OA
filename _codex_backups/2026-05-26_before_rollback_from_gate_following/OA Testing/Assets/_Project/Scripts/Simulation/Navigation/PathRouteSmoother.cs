// PathRouteSmoother.cs:
// A* gives us polite cell-by-cell breadcrumbs. This file tries to turn those
// breadcrumbs into a route the ship can follow without zig-zagging like that web
// from a spider on caffine.
using System.Collections.Generic;
using UnityEngine;

namespace OA.Simulation.Navigation
{
    // Static helper that removes unnecessary waypoint corners while respecting blocked cells.
    public static class PathRouteSmoother
    {
        // Builds a world-space route from a cell path, keeping the final click inside its cell.
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

            // Clamp the clicked destination so the route does not aim outside the final hex.
            Vector2Int destinationCell = cellPath[cellPath.Count - 1];
            Vector2 destinationCenter = map.GetWorldCenter(destinationCell.x, destinationCell.y);
            Vector2 finalDestination = ClampPointToCell(destinationWorld, destinationCenter, map.CellSize * 0.4f);

            // Candidate list starts at the ship, walks cell centers, and ends at the clamped click point.
            scratchCandidates.Add(startWorld);

            for (int i = 1; i < cellPath.Count; i++)
            {
                Vector2Int c = cellPath[i];
                scratchCandidates.Add(map.GetWorldCenter(c.x, c.y));
            }

            scratchCandidates.Add(finalDestination);

            routeOut.Add(startWorld);

            // Greedy shortcut pass: from each anchor, jump to the farthest visible candidate.
            // Search backward so open-water legs stop after finding the same
            // farthest legal point instead of testing every nearer point too.
            int anchor = 0;
            while (anchor < scratchCandidates.Count - 1)
            {
                int farthestReachable = anchor + 1;
                for (int candidate = scratchCandidates.Count - 1;
                     candidate > farthestReachable;
                     candidate--)
                {
                    if (IsSegmentTraversable(
                        map,
                        blockedMask,
                        scratchCandidates[anchor],
                        scratchCandidates[candidate],
                        sampleFactor))
                    {
                        farthestReachable = candidate;
                        break;
                    }
                }

                Vector2 point = scratchCandidates[farthestReachable];

                if (Vector2.Distance(routeOut[routeOut.Count - 1], point) > 0.0005f)
                {
                    routeOut.Add(point);
                }

                anchor = farthestReachable;
            }

            // Emergency fallback so callers always get at least a start/end line.
            if (routeOut.Count < 2)
            {
                routeOut.Clear();
                routeOut.Add(startWorld);
                routeOut.Add(finalDestination);
            }
        }

        // Samples along a world segment and makes sure every touched cell is traversable.
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

        // Checks a cell against either the safety-expanded mask or the raw map walkability.
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

        // Pulls a point back toward the cell center so final destinations stay inside their hex.
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
