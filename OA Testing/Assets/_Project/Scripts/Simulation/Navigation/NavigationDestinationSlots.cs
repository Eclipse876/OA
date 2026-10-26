using System.Collections.Generic;
using UnityEngine;

namespace OA.Simulation.Navigation
{
    public struct NavigationSlotReservation
    {
        public readonly Vector2 Position;
        public readonly float Radius;

        public NavigationSlotReservation(Vector2 position, float radius)
        {
            Position = position;
            Radius = Mathf.Max(0f, radius);
        }
    }

    // Allocates endpoints only. The normal route planner still proves that a
    // ship can physically execute its route to the allocated position.
    public static class NavigationDestinationSlots
    {
        private const int SearchDepth = 6;
        private const int RingCount = 12;
        private const int RingSamples = 24;
        private const float SeparationMargin = 0.01f;

        public static bool IsWorldInsideMap(HexMapRuntime map, Vector2 world)
        {
            if (map == null || !map.HasWorldCenters || !IsFinite(world)) return false;

            // TryWorldToCell intentionally snaps even distant off-map queries.
            // Placement needs a strict bounds and near-center check instead.
            Vector2 low = map.GetWorldCenter(0, 0);
            Vector2 high = low;
            Include(map.GetWorldCenter(map.Width - 1, 0), ref low, ref high);
            Include(map.GetWorldCenter(0, 1), ref low, ref high);
            Include(map.GetWorldCenter(map.Width - 1, 1), ref low, ref high);
            Include(map.GetWorldCenter(0, map.Height - 1), ref low, ref high);
            Include(map.GetWorldCenter(map.Width - 1, map.Height - 1), ref low, ref high);
            float extent = map.CellSize * 0.65f;
            if (world.x < low.x - extent || world.x > high.x + extent ||
                world.y < low.y - extent || world.y > high.y + extent ||
                !map.TryWorldToCell(world, out Vector2Int cell)) return false;

            // A radius-half-cell circle cuts valid corners out of every hex.
            // Use its six Voronoi half-planes, including virtual neighbors beyond
            // the map edge so nearest-edge snapping cannot admit off-map points.
            Vector2 column = map.GetWorldCenter(1, 0) - map.GetWorldCenter(0, 0);
            Vector2 row = (map.GetWorldCenter(0, 2) - map.GetWorldCenter(0, 0)) * 0.5f;
            if (column.sqrMagnitude < 0.000001f || row.sqrMagnitude < 0.000001f) return false;
            Vector2 delta = world - map.GetWorldCenter(cell.x, cell.y);
            return WithinBisector(delta, column) && WithinBisector(delta, -column) &&
                WithinBisector(delta, row + column * 0.5f) &&
                WithinBisector(delta, row - column * 0.5f) &&
                WithinBisector(delta, -row + column * 0.5f) &&
                WithinBisector(delta, -row - column * 0.5f);
        }

        private static bool WithinBisector(Vector2 delta, Vector2 neighbor)
        {
            return Vector2.Dot(delta, neighbor) <= neighbor.sqrMagnitude * 0.5f + 0.001f;
        }

        public static bool TryFindSlot(
            HexMapRuntime map,
            NavigationTraversalMask mask,
            Vector2Int startCell,
            Vector2 desired,
            float radius,
            IReadOnlyList<NavigationSlotReservation> reservations,
            out Vector2 destination)
        {
            destination = default;
            if (map == null || !map.HasWorldCenters || mask == null ||
                mask.Map != map || mask.MapVersion != map.Version ||
                mask.IsBlocked(startCell) || !IsFinite(desired) ||
                !IsFinite(radius) || radius < 0f) return false;

            if (reservations != null)
            {
                for (int i = 0; i < reservations.Count; i++)
                    if (!IsFinite(reservations[i].Position) ||
                        !IsFinite(reservations[i].Radius)) return false;
            }

            if (!mask.TryFindClosestReachableCell(startCell, desired, out Vector2Int anchor))
                return false;

            Vector2 anchorWorld = map.GetWorldCenter(anchor.x, anchor.y);
            if (IsWorldInsideMap(map, desired) &&
                map.TryWorldToCell(desired, out Vector2Int clicked) && clicked == anchor)
                anchorWorld = desired;

            var neighbors = new Vector2Int[6];
            Vector2 first = ClampToSafeCell(map, mask, anchor, anchorWorld, neighbors);
            if (CanReserve(first, radius, reservations))
            {
                destination = first;
                return true;
            }

            // Flood only a bounded neighborhood already connected to the anchor.
            // Re-resolving each candidate through the global nearest-reachable
            // scan would be expensive and could collapse slots onto one center.
            var cells = new List<Vector2Int>(128) { anchor };
            var depths = new List<int>(128) { 0 };
            var reachable = new HashSet<int> { map.GetIndex(anchor.x, anchor.y) };
            for (int head = 0; head < cells.Count; head++)
            {
                if (depths[head] >= SearchDepth) continue;
                Vector2Int current = cells[head];
                int count = map.GetNeighborCount(current.x, current.y, neighbors);
                for (int i = 0; i < count; i++)
                {
                    Vector2Int next = neighbors[i];
                    if (mask.IsBlocked(next) || !reachable.Add(map.GetIndex(next.x, next.y)))
                        continue;
                    cells.Add(next);
                    depths.Add(depths[head] + 1);
                }
            }

            bool found = false;
            Vector2 best = default;
            float bestDistance = float.PositiveInfinity;
            float ringStep = Mathf.Max(map.CellSize * 0.08f,
                radius * 2f + SeparationMargin);
            for (int ring = 1; ring <= RingCount; ring++)
            {
                for (int sample = 0; sample < RingSamples; sample++)
                {
                    float angle = sample * (Mathf.PI * 2f / RingSamples);
                    Vector2 candidate = anchorWorld + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) *
                        (ring * ringStep);
                    if (!map.TryWorldToCell(candidate, out Vector2Int cell) ||
                        !reachable.Contains(map.GetIndex(cell.x, cell.y)) ||
                        (candidate - map.GetWorldCenter(cell.x, cell.y)).sqrMagnitude >
                            map.CellSize * map.CellSize * 0.25f) continue;

                    Consider(ClampToSafeCell(map, mask, cell, candidate, neighbors), desired,
                        radius, reservations, ref found, ref best, ref bestDistance);
                }
            }

            // Centers and small intra-cell rings cover nearby cells even when a
            // large hull makes the global ring step wider than a single cell.
            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int cell = cells[i];
                Vector2 center = map.GetWorldCenter(cell.x, cell.y);
                Consider(center, desired, radius, reservations,
                    ref found, ref best, ref bestDistance);
                Consider(ClampToSafeCell(map, mask, cell, anchorWorld, neighbors), desired,
                    radius, reservations, ref found, ref best, ref bestDistance);
                float offset = SafeOffset(map, mask, cell, neighbors);
                for (int sample = 0; sample < 8; sample++)
                {
                    float angle = sample * (Mathf.PI * 0.25f);
                    Vector2 candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * offset;
                    Consider(candidate, desired, radius, reservations,
                        ref found, ref best, ref bestDistance);
                }
            }

            destination = best;
            return found;
        }

        private static void Consider(Vector2 candidate, Vector2 desired, float radius,
            IReadOnlyList<NavigationSlotReservation> reservations, ref bool found,
            ref Vector2 best, ref float bestDistance)
        {
            float distance = (candidate - desired).sqrMagnitude;
            if (distance >= bestDistance || !CanReserve(candidate, radius, reservations)) return;
            found = true;
            best = candidate;
            bestDistance = distance;
        }

        private static bool CanReserve(Vector2 candidate, float radius,
            IReadOnlyList<NavigationSlotReservation> reservations)
        {
            if (reservations == null) return true;
            for (int i = 0; i < reservations.Count; i++)
            {
                float minimumDistance = radius + reservations[i].Radius + SeparationMargin;
                if ((candidate - reservations[i].Position).sqrMagnitude <
                    minimumDistance * minimumDistance) return false;
            }
            return true;
        }

        private static Vector2 ClampToSafeCell(HexMapRuntime map, NavigationTraversalMask mask,
            Vector2Int cell, Vector2 candidate, Vector2Int[] neighbors)
        {
            Vector2 center = map.GetWorldCenter(cell.x, cell.y);
            return center + Vector2.ClampMagnitude(candidate - center, SafeOffset(map, mask, cell, neighbors));
        }

        private static float SafeOffset(HexMapRuntime map, NavigationTraversalMask mask,
            Vector2Int cell, Vector2Int[] neighbors)
        {
            // Match final-stop adjustment, staying slightly inside its limit so
            // the route controller will preserve the assigned endpoint.
            int count = map.GetNeighborCount(cell.x, cell.y, neighbors);
            bool restrictedNeighbor = false;
            for (int i = 0; i < count; i++)
                if (mask.IsBlocked(neighbors[i])) { restrictedNeighbor = true; break; }
            return map.CellSize * (restrictedNeighbor ? 0.12f : 0.35f) * 0.98f;
        }

        private static void Include(Vector2 point, ref Vector2 low, ref Vector2 high)
        {
            low = Vector2.Min(low, point);
            high = Vector2.Max(high, point);
        }

        private static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
