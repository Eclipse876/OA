using System.Collections.Generic;
using UnityEngine;

namespace OA.Simulation.Navigation
{
    // Optional guidance around a snapshot of stationary traffic. These are
    // terrain-safe route hints, never collision constraints or live forces.
    public static class ShipTrafficHints
    {
        public static bool TryGetDetour(
            HexMapRuntime map,
            NavigationTraversalMask mask,
            Vector2 start,
            Vector2 destination,
            float ownRadius,
            IReadOnlyList<NavigationSlotReservation> stationaryShips,
            out Vector2 approach,
            out Vector2 departure)
        {
            approach = default;
            departure = default;
            if (map == null || !map.HasWorldCenters || stationaryShips == null ||
                stationaryShips.Count == 0 || !IsFinite(start) || !IsFinite(destination) ||
                !IsFinite(ownRadius) ||
                (mask != null && (mask.Map != map || mask.MapVersion != map.Version)))
                return false;

            Vector2 leg = destination - start;
            float length = leg.magnitude;
            if (length <= 0.001f) return false;

            ownRadius = Mathf.Max(0f, ownRadius);
            Vector2 forward = leg / length;
            Vector2 normal = new Vector2(-forward.y, forward.x);
            int blockerIndex = -1;
            float nearestAlong = float.PositiveInfinity;

            for (int i = 0; i < stationaryShips.Count; i++)
            {
                NavigationSlotReservation ship = stationaryShips[i];
                if (!IsFinite(ship.Position) || !IsFinite(ship.Radius)) continue;
                float clearance = ownRadius + Mathf.Max(0f, ship.Radius) + 1f;
                Vector2 offset = ship.Position - start;
                float along = Vector2.Dot(offset, forward);
                float padding = GetLongitudinalPadding(map, clearance);

                // A hint must advance along the original leg. Traffic too close
                // to either endpoint is allowed to phase rather than making a loop.
                if (along <= padding || along >= length - padding || along >= nearestAlong ||
                    Mathf.Abs(Vector2.Dot(offset, normal)) >= clearance)
                    continue;

                blockerIndex = i;
                nearestAlong = along;
            }

            if (blockerIndex < 0) return false;

            NavigationSlotReservation blocker = stationaryShips[blockerIndex];
            float requiredClearance = ownRadius + Mathf.Max(0f, blocker.Radius) + 1f;
            float longitudinalPadding = GetLongitudinalPadding(map, requiredClearance);
            float baseOffset = requiredClearance * 1.2f + map.CellSize * 0.25f;
            float bestLength = float.PositiveInfinity;

            // Exactly four candidate paths: two sides at two nearby offsets.
            // The second offset helps avoid cell edges and cramped shoreline.
            for (int offsetIndex = 0; offsetIndex < 2; offsetIndex++)
            {
                float sideOffset = baseOffset + offsetIndex * map.CellSize * 0.35f;
                for (int side = 1; side >= -1; side -= 2)
                {
                    Vector2 sideways = normal * (side * sideOffset);
                    Vector2 candidateApproach = blocker.Position - forward * longitudinalPadding + sideways;
                    Vector2 candidateDeparture = blocker.Position + forward * longitudinalPadding + sideways;
                    float candidateLength = Vector2.Distance(start, candidateApproach) +
                        Vector2.Distance(candidateApproach, candidateDeparture) +
                        Vector2.Distance(candidateDeparture, destination);

                    if (candidateLength >= bestLength ||
                        !NavigationDestinationSlots.IsWorldInsideMap(map, candidateApproach) ||
                        !NavigationDestinationSlots.IsWorldInsideMap(map, candidateDeparture) ||
                        !ClearsTraffic(start, candidateApproach, candidateDeparture,
                            destination, ownRadius, stationaryShips) ||
                        !RouteSegmentUtility.IsSegmentTraversable(map, mask, start, candidateApproach) ||
                        !RouteSegmentUtility.IsSegmentTraversable(map, mask, candidateApproach, candidateDeparture) ||
                        !RouteSegmentUtility.IsSegmentTraversable(map, mask, candidateDeparture, destination))
                        continue;

                    approach = candidateApproach;
                    departure = candidateDeparture;
                    bestLength = candidateLength;
                }
            }

            return !float.IsPositiveInfinity(bestLength);
        }

        private static float GetLongitudinalPadding(HexMapRuntime map, float clearance)
        {
            return Mathf.Max(clearance * 1.5f, map.CellSize * 0.75f);
        }

        private static bool ClearsTraffic(Vector2 start, Vector2 approach,
            Vector2 departure, Vector2 destination, float ownRadius,
            IReadOnlyList<NavigationSlotReservation> stationaryShips)
        {
            for (int i = 0; i < stationaryShips.Count; i++)
            {
                NavigationSlotReservation ship = stationaryShips[i];
                if (!IsFinite(ship.Position) || !IsFinite(ship.Radius)) continue;
                float clearance = ownRadius + Mathf.Max(0f, ship.Radius) + 1f;
                float clearanceSqr = clearance * clearance;

                // Existing overlap cannot be undone by a guidance hint. Do not
                // let it prevent useful separation from another blocker ahead.
                if ((start - ship.Position).sqrMagnitude < clearanceSqr ||
                    (destination - ship.Position).sqrMagnitude < clearanceSqr)
                    continue;

                if (DistanceToSegmentSqr(ship.Position, start, approach) < clearanceSqr ||
                    DistanceToSegmentSqr(ship.Position, approach, departure) < clearanceSqr ||
                    DistanceToSegmentSqr(ship.Position, departure, destination) < clearanceSqr)
                    return false;
            }
            return true;
        }

        private static float DistanceToSegmentSqr(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSqr = segment.sqrMagnitude;
            float t = lengthSqr > 0.000001f
                ? Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSqr)
                : 0f;
            return (point - (start + segment * t)).sqrMagnitude;
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
