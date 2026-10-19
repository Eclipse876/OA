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
            NavigationTraversalMask mask,
            IReadOnlyList<Vector2Int> cellPath,
            Vector2 startWorld,
            Vector2 destinationWorld,
            List<Vector2> routeOut,
            List<Vector2> scratchCandidates)
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
            AppendSimplifiedRange(
                map,
                mask,
                scratchCandidates,
                0,
                scratchCandidates.Count - 1,
                routeOut);
            RemoveRedundantWaypoints(map, mask, routeOut);

            // Emergency fallback so callers always get at least a start/end line.
            if (routeOut.Count < 2)
            {
                routeOut.Clear();
                routeOut.Add(startWorld);
                routeOut.Add(finalDestination);
            }
        }

        // The recursive pass normally leaves only a handful of points. A final
        // greedy pass over that small list recovers shortcuts that cross a split
        // boundary without returning to the original all-cells search cost.
        private static void RemoveRedundantWaypoints(
            HexMapRuntime map,
            NavigationTraversalMask mask,
            List<Vector2> route)
        {
            int sourceCount = route.Count;
            if (sourceCount <= 2)
            {
                return;
            }

            int anchor = 0;
            int writeIndex = 1;

            while (anchor < sourceCount - 1)
            {
                int farthestReachable = anchor + 1;

                for (int candidate = sourceCount - 1;
                     candidate > anchor;
                     candidate--)
                {
                    if (RouteSegmentUtility.IsSegmentTraversable(
                        map,
                        mask,
                        route[anchor],
                        route[candidate]))
                    {
                        farthestReachable = candidate;
                        break;
                    }
                }

                route[writeIndex++] = route[farthestReachable];
                anchor = farthestReachable;
            }

            if (writeIndex < route.Count)
            {
                route.RemoveRange(writeIndex, route.Count - writeIndex);
            }
        }

        // Recursively accepts the longest segment in each half that is known to
        // be legal. The old destination-backward scan repeatedly sampled almost
        // the whole route for every candidate and became cubic on long paths.
        private static void AppendSimplifiedRange(
            HexMapRuntime map,
            NavigationTraversalMask mask,
            IReadOnlyList<Vector2> candidates,
            int startIndex,
            int endIndex,
            List<Vector2> routeOut)
        {
            if (endIndex <= startIndex)
            {
                return;
            }

            if (endIndex == startIndex + 1 ||
                RouteSegmentUtility.IsSegmentTraversable(
                    map,
                    mask,
                    candidates[startIndex],
                    candidates[endIndex]))
            {
                Vector2 point = candidates[endIndex];
                if (Vector2.Distance(
                    routeOut[routeOut.Count - 1],
                    point) > 0.0005f)
                {
                    routeOut.Add(point);
                }

                return;
            }

            int midpoint = startIndex + (endIndex - startIndex) / 2;
            AppendSimplifiedRange(
                map,
                mask,
                candidates,
                startIndex,
                midpoint,
                routeOut);
            AppendSimplifiedRange(
                map,
                mask,
                candidates,
                midpoint,
                endIndex,
                routeOut);
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
