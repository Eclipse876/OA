// ShipRouteFollower.cs:
// Shared route-following logic for both prediction and the live ship.
// Keeping this rule in one place is what lets the colored line remain an honest
// preview of the motion the player sees once the order is accepted.
using System.Collections.Generic;
using OA.Simulation.Navigation;
using OA.Simulation.Units;
using UnityEngine;

namespace OA.Simulation.Movement
{
    // Tracks how far the ship has progressed through its current route without demanding
    // that an inertial hull strike every sampled point exactly.
    public struct ShipRouteFollowState
    {
        public float ProgressWorld;
        public bool IsComplete;
        public Vector2 PreviousPosition;
        public bool HasPreviousPosition;
        public int SegmentIndex;
        public int ConstraintIndex;

        public void Reset()
        {
            ProgressWorld = 0f;
            IsComplete = false;
            PreviousPosition = default;
            HasPreviousPosition = false;
            SegmentIndex = 0;
            ConstraintIndex = 1;
        }
    }

    public static class ShipRouteFollower
    {
        // Builds one movement command from current motion plus the route's current look-ahead target.
        public static MovementCommand BuildCommand(
            MovementState movementState,
            IReadOnlyList<ShipRoutePoint> route,
            ref ShipRouteFollowState followState,
            MovementSpeedMode speedMode,
            MovementProfileDefinition profile,
            HexMapRuntime map,
            float lookAheadDistance,
            float arrivalDistance,
            bool routeChanged,
            out RouteSegmentIntent segmentIntent)
        {
            segmentIntent = RouteSegmentIntent.Cruise;

            if (route == null || route.Count < 2)
            {
                followState.IsComplete = true;
                return MovementCommand.Hold(movementState.Position, routeChanged);
            }

            UpdateProgress(
                movementState.Position,
                route,
                arrivalDistance,
                ref followState);

            ShipRoutePoint finalPoint = route[route.Count - 1];
            float totalDistance = finalPoint.DistanceFromStartWorld;
            float finalDistance = Vector2.Distance(
                movementState.Position,
                finalPoint.Position);

            float arrival = Mathf.Max(0.001f, arrivalDistance);

            if (finalDistance <= arrival)
            {
                followState.ProgressWorld = totalDistance;
            }

            bool stopped =
                movementState.SpeedKnots <= 0.01f &&
                movementState.VelocityWorld.sqrMagnitude <= 0.000001f;

            if (followState.ProgressWorld >= totalDistance - arrival &&
                finalDistance <= arrival &&
                stopped)
            {
                followState.IsComplete = true;
                segmentIntent = RouteSegmentIntent.Stop;
                return MovementCommand.Hold(movementState.Position);
            }

            followState.IsComplete = false;

            int guidanceIndex = AdvancePointIndex(
                route,
                followState.ProgressWorld + 0.0001f,
                followState.ConstraintIndex);

            followState.ConstraintIndex = guidanceIndex;

            float steeringDistance = Mathf.Min(
                totalDistance,
                followState.ProgressWorld + Mathf.Max(0.01f, lookAheadDistance));

            Vector2 steeringTarget = GetPositionAtDistance(
                route,
                steeringDistance,
                followState.SegmentIndex);

            float remainingDistance = Mathf.Max(
                0f,
                totalDistance - followState.ProgressWorld);

            float speedLimitKnots = KinematicRoutePlanner.CalculateAllowedSpeedKnots(
                route,
                followState.ProgressWorld,
                speedMode,
                profile,
                out segmentIntent);

            // Once the route reaches its final sample, brake against actual endpoint distance.
            // This lets a heavy ship correct a small overshoot instead of declaring the route done.
            if (guidanceIndex == route.Count - 1 ||
                remainingDistance <= arrival)
            {
                steeringTarget = finalPoint.Position;
                remainingDistance = finalDistance;
                segmentIntent = RouteSegmentIntent.Stop;
            }

            float terrainSpeedMultiplier = GetTerrainSpeedMultiplier(
                map,
                movementState.Position);

            return MovementCommand.Move(
                steeringTarget,
                remainingDistance,
                speedMode,
                speedLimitKnots,
                routeChanged,
                terrainSpeedMultiplier);
        }

        // Projects the ship locally forward along its present leg instead of
        // snapping to a later leg when a loop or return route passes nearby.
        private static void UpdateProgress(
            Vector2 position,
            IReadOnlyList<ShipRoutePoint> route,
            float arrivalDistance,
            ref ShipRouteFollowState followState)
        {
            float traveledWorld = followState.HasPreviousPosition
                ? Vector2.Distance(position, followState.PreviousPosition)
                : 0f;

            followState.PreviousPosition = position;
            followState.HasPreviousPosition = true;

            float maximumAdvance = Mathf.Max(
                0.1f,
                traveledWorld * 2f + Mathf.Max(0.001f, arrivalDistance));

            float maximumProgress = Mathf.Min(
                route[route.Count - 1].DistanceFromStartWorld,
                followState.ProgressWorld + maximumAdvance);

            int startSegment = Mathf.Max(0, followState.SegmentIndex - 1);
            int endSegment = startSegment;

            while (endSegment < route.Count - 2 &&
                   route[endSegment + 1].DistanceFromStartWorld <=
                   maximumProgress + 0.001f)
            {
                endSegment++;
            }

            float bestProgress = followState.ProgressWorld;
            float bestDistanceSqr = float.PositiveInfinity;

            for (int i = startSegment; i <= endSegment; i++)
            {
                Vector2 a = route[i].Position;
                Vector2 b = route[i + 1].Position;
                Vector2 segment = b - a;
                float segmentLengthSqr = segment.sqrMagnitude;

                if (segmentLengthSqr <= 0.000001f)
                {
                    continue;
                }

                float t = Mathf.Clamp01(
                    Vector2.Dot(position - a, segment) / segmentLengthSqr);

                Vector2 projected = a + segment * t;
                float projectedProgress =
                    route[i].DistanceFromStartWorld +
                    Vector2.Distance(a, projected);

                if (projectedProgress + 0.001f < followState.ProgressWorld)
                {
                    continue;
                }

                if (projectedProgress > maximumProgress + 0.001f)
                {
                    continue;
                }

                float distanceSqr = (position - projected).sqrMagnitude;

                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestProgress = projectedProgress;
                }
            }

            followState.ProgressWorld = Mathf.Max(
                followState.ProgressWorld,
                bestProgress);

            followState.SegmentIndex = AdvanceSegmentIndex(
                route,
                followState.ProgressWorld,
                followState.SegmentIndex);
        }

        private static int AdvancePointIndex(
            IReadOnlyList<ShipRoutePoint> route,
            float distance,
            int startIndex)
        {
            int index = Mathf.Clamp(startIndex, 1, route.Count - 1);

            while (index < route.Count - 1 &&
                   route[index].DistanceFromStartWorld < distance)
            {
                index++;
            }

            return index;
        }

        private static int AdvanceSegmentIndex(
            IReadOnlyList<ShipRoutePoint> route,
            float distance,
            int startIndex)
        {
            int index = Mathf.Clamp(startIndex, 0, route.Count - 2);

            while (index < route.Count - 2 &&
                   route[index + 1].DistanceFromStartWorld < distance)
            {
                index++;
            }

            return index;
        }

        private static Vector2 GetPositionAtDistance(
            IReadOnlyList<ShipRoutePoint> route,
            float distance,
            int startIndex)
        {
            int segmentIndex = AdvanceSegmentIndex(
                route,
                distance,
                startIndex);

            ShipRoutePoint a = route[segmentIndex];
            ShipRoutePoint b = route[segmentIndex + 1];

            float segmentLength = Mathf.Max(
                0.0001f,
                b.DistanceFromStartWorld - a.DistanceFromStartWorld);

            float t = Mathf.Clamp01(
                (distance - a.DistanceFromStartWorld) / segmentLength);

            return Vector2.Lerp(a.Position, b.Position, t);
        }

        private static float GetTerrainSpeedMultiplier(
            HexMapRuntime map,
            Vector2 position)
        {
            if (map == null ||
                !map.TryWorldToCell(position, out Vector2Int cell))
            {
                return 1f;
            }

            return 1f / Mathf.Max(1f, map.GetMoveCost(cell.x, cell.y));
        }
    }
}
