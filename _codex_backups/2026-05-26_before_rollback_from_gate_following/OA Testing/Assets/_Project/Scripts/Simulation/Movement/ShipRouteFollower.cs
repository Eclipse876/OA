// ShipRouteFollower.cs:
// Shared route-following logic for both prediction and the live ship.
// Keeping this rule in one place is what lets the colored line remain an honest
// preview of the motion the player sees once the order is accepted.
using System.Collections.Generic;
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
        public int NextGateIndex;
        public Vector2 PreviousGateCheckPosition;
        public bool HasPreviousGateCheckPosition;

        public void Reset()
        {
            ProgressWorld = 0f;
            IsComplete = false;
            PreviousPosition = default;
            HasPreviousPosition = false;
            NextGateIndex = 0;
            PreviousGateCheckPosition = default;
            HasPreviousGateCheckPosition = false;
        }
    }

    public static class ShipRouteFollower
    {
        // Builds one movement command from current motion plus the route's current look-ahead target.
        public static MovementCommand BuildCommand(
            MovementState movementState,
            IReadOnlyList<ShipRoutePoint> route,
            IReadOnlyList<ShipRouteGate> gates,
            ref ShipRouteFollowState followState,
            MovementSpeedMode speedMode,
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

            AdvancePassedIntermediateGates(
                movementState.Position,
                gates,
                ref followState);

            bool hasPendingIntermediateGate = TryGetPendingIntermediateGate(
                gates,
                followState.NextGateIndex,
                out ShipRouteGate pendingGate);

            float maximumProgressDistance = hasPendingIntermediateGate
                ? pendingGate.GuidanceTargetDistanceWorld
                : float.PositiveInfinity;

            UpdateProgress(
                movementState.Position,
                route,
                arrivalDistance,
                maximumProgressDistance,
                ref followState);

            ShipRoutePoint finalPoint = route[route.Count - 1];
            float totalDistance = finalPoint.DistanceFromStartWorld;
            float finalDistance = Vector2.Distance(
                movementState.Position,
                finalPoint.Position);

            float arrival = Mathf.Max(0.001f, arrivalDistance);

            if (!hasPendingIntermediateGate &&
                finalDistance <= arrival)
            {
                followState.ProgressWorld = totalDistance;
            }

            bool stopped =
                movementState.SpeedKnots <= 0.01f &&
                movementState.VelocityWorld.sqrMagnitude <= 0.000001f;

            if (!hasPendingIntermediateGate &&
                followState.ProgressWorld >= totalDistance - arrival &&
                finalDistance <= arrival &&
                stopped)
            {
                followState.IsComplete = true;
                segmentIntent = RouteSegmentIntent.Stop;
                return MovementCommand.Hold(movementState.Position);
            }

            followState.IsComplete = false;

            int guidanceIndex = FindPointAtOrAfterDistance(
                route,
                followState.ProgressWorld + 0.0001f);

            ShipRoutePoint guidance = route[guidanceIndex];
            segmentIntent = guidance.SegmentIntent;

            float steeringDistance = Mathf.Min(
                totalDistance,
                followState.ProgressWorld + Mathf.Max(0.01f, lookAheadDistance));

            if (hasPendingIntermediateGate)
            {
                steeringDistance = Mathf.Min(
                    steeringDistance,
                    pendingGate.GuidanceTargetDistanceWorld);
            }

            Vector2 steeringTarget = GetPositionAtDistance(route, steeringDistance);
            float remainingDistance = Mathf.Max(
                0f,
                totalDistance - followState.ProgressWorld);

            // Once the route reaches its final sample, brake against actual endpoint distance.
            // This lets a heavy ship correct a small overshoot instead of declaring the route done.
            if (!hasPendingIntermediateGate &&
                (guidanceIndex == route.Count - 1 ||
                 remainingDistance <= arrival))
            {
                steeringTarget = finalPoint.Position;
                remainingDistance = finalDistance;
                segmentIntent = RouteSegmentIntent.Stop;
            }

            float speedLimitKnots = guidance.SpeedLimitKnots;

            if (hasPendingIntermediateGate)
            {
                int gateGuidanceIndex = FindPointAtOrAfterDistance(
                    route,
                    pendingGate.GuidanceTargetDistanceWorld);

                ShipRoutePoint gateGuidance = route[gateGuidanceIndex];

                speedLimitKnots = MostRestrictiveSpeedLimit(
                    speedLimitKnots,
                    gateGuidance.SpeedLimitKnots);

                segmentIntent = gateGuidance.SegmentIntent;
            }

            return MovementCommand.Move(
                steeringTarget,
                remainingDistance,
                speedMode,
                speedLimitKnots,
                routeChanged);
        }

        // Intermediate orders are pass-through gates. Until the physical track
        // has crossed the next gate, later legs are not eligible steering targets.
        private static void AdvancePassedIntermediateGates(
            Vector2 position,
            IReadOnlyList<ShipRouteGate> gates,
            ref ShipRouteFollowState followState)
        {
            if (!followState.HasPreviousGateCheckPosition)
            {
                followState.PreviousGateCheckPosition = position;
                followState.HasPreviousGateCheckPosition = true;
            }

            if (gates == null)
            {
                followState.PreviousGateCheckPosition = position;
                return;
            }

            while (followState.NextGateIndex < gates.Count)
            {
                ShipRouteGate gate = gates[followState.NextGateIndex];

                if (gate.MustStop)
                {
                    break;
                }

                if (DistanceFromSegmentToPoint(
                        followState.PreviousGateCheckPosition,
                        position,
                        gate.Position) > gate.RadiusWorld + 0.0001f)
                {
                    break;
                }

                followState.NextGateIndex++;
            }

            followState.PreviousGateCheckPosition = position;
        }

        private static bool TryGetPendingIntermediateGate(
            IReadOnlyList<ShipRouteGate> gates,
            int nextGateIndex,
            out ShipRouteGate pendingGate)
        {
            pendingGate = default;

            if (gates == null ||
                nextGateIndex < 0 ||
                nextGateIndex >= gates.Count)
            {
                return false;
            }

            pendingGate = gates[nextGateIndex];

            return !pendingGate.MustStop &&
                   pendingGate.GuidanceTargetDistanceWorld >= 0f;
        }

        // Projects the ship locally forward along its present leg instead of
        // snapping to a later leg when a loop or return route passes nearby.
        private static void UpdateProgress(
            Vector2 position,
            IReadOnlyList<ShipRoutePoint> route,
            float arrivalDistance,
            float maximumProgressDistance,
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

            if (!float.IsPositiveInfinity(maximumProgressDistance))
            {
                maximumProgress = Mathf.Min(
                    maximumProgress,
                    Mathf.Max(
                        followState.ProgressWorld,
                        maximumProgressDistance));
            }

            int startSegment = Mathf.Max(
                0,
                FindSegmentAtDistance(route, followState.ProgressWorld) - 1);

            int endSegment = Mathf.Min(
                route.Count - 2,
                FindSegmentAtDistance(route, maximumProgress));

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
        }

        // Zero means unrestricted. Otherwise preserve the lower active speed cap.
        private static float MostRestrictiveSpeedLimit(float a, float b)
        {
            if (a <= 0f)
            {
                return b;
            }

            if (b <= 0f)
            {
                return a;
            }

            return Mathf.Min(a, b);
        }

        private static float DistanceFromSegmentToPoint(
            Vector2 a,
            Vector2 b,
            Vector2 point)
        {
            Vector2 segment = b - a;
            float lengthSqr = segment.sqrMagnitude;

            if (lengthSqr <= 0.000001f)
            {
                return Vector2.Distance(a, point);
            }

            float t = Mathf.Clamp01(
                Vector2.Dot(point - a, segment) / lengthSqr);

            return Vector2.Distance(a + segment * t, point);
        }

        private static int FindPointAtOrAfterDistance(
            IReadOnlyList<ShipRoutePoint> route,
            float distance)
        {
            for (int i = 1; i < route.Count; i++)
            {
                if (route[i].DistanceFromStartWorld >= distance)
                {
                    return i;
                }
            }

            return route.Count - 1;
        }

        private static int FindSegmentAtDistance(
            IReadOnlyList<ShipRoutePoint> route,
            float distance)
        {
            for (int i = 0; i < route.Count - 1; i++)
            {
                if (route[i + 1].DistanceFromStartWorld >= distance)
                {
                    return i;
                }
            }

            return Mathf.Max(0, route.Count - 2);
        }

        private static Vector2 GetPositionAtDistance(
            IReadOnlyList<ShipRoutePoint> route,
            float distance)
        {
            int segmentIndex = FindSegmentAtDistance(route, distance);
            ShipRoutePoint a = route[segmentIndex];
            ShipRoutePoint b = route[segmentIndex + 1];

            float segmentLength = Mathf.Max(
                0.0001f,
                b.DistanceFromStartWorld - a.DistanceFromStartWorld);

            float t = Mathf.Clamp01(
                (distance - a.DistanceFromStartWorld) / segmentLength);

            return Vector2.Lerp(a.Position, b.Position, t);
        }
    }
}
