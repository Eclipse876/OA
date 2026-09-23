// RouteSegmentUtility.cs:
// Shared sampling rule for direct routing, corridor smoothing, and physical
// prediction. One legality test keeps the visible line and A* constraints aligned.
using UnityEngine;

namespace OA.Simulation.Navigation
{
    public static class RouteSegmentUtility
    {
        public static bool IsSegmentTraversable(
            HexMapRuntime map,
            NavigationTraversalMask mask,
            Vector2 start,
            Vector2 end)
        {
            return TryEvaluateSegment(
                map,
                mask,
                start,
                end,
                out _);
        }

        public static float GetHighestMoveCostAlongSegment(
            HexMapRuntime map,
            NavigationTraversalMask mask,
            Vector2 start,
            Vector2 end)
        {
            return TryEvaluateSegment(
                map,
                mask,
                start,
                end,
                out float highestMoveCost)
                ? highestMoveCost
                : float.PositiveInfinity;
        }

        public static bool TryEvaluateSegment(
            HexMapRuntime map,
            NavigationTraversalMask mask,
            Vector2 start,
            Vector2 end,
            out float highestMoveCost)
        {
            highestMoveCost = 1f;

            if (map == null)
            {
                return false;
            }

            float distance = Vector2.Distance(start, end);
            float sampleStep = Mathf.Max(0.02f, map.CellSize * 0.12f);
            int samples = Mathf.Max(1, Mathf.CeilToInt(distance / sampleStep));

            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector2 position = Vector2.Lerp(start, end, t);

                if (!map.TryWorldToCell(position, out Vector2Int cell))
                {
                    return false;
                }

                if (mask != null
                    ? mask.IsBlocked(cell)
                    : !map.IsWalkable(cell.x, cell.y))
                {
                    return false;
                }

                highestMoveCost = Mathf.Max(
                    highestMoveCost,
                    map.GetMoveCost(cell.x, cell.y));
            }

            return true;
        }
    }
}
