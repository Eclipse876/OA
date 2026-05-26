using System.Collections.Generic;
using OA.Simulation.Units;
using UnityEngine;

namespace OA.Simulation.Movement
{
    public class KinematicRoutePlanner
    {
        private const float TurnSlowAngleDegrees = 25f;
        private const float TightTurnAngleDegrees = 60f;
        private const float EpsilonKnots = 0.05f;

        private enum SpeedLimiter { None, Turn, FinalStop }


        public static void BuildRoute(
            IReadOnlyList<Vector2> geometryPath,
            MovementProfileDefinition profile,
            MovementSpeedMode speedMode,
            ShipRoute routeOut)
        {

            if (routeOut == null)
            {
                return;
            }

            routeOut.Clear();

            if (geometryPath == null || geometryPath.Count < 2 || profile == null)
            {
                return;
            }

            ShipHandlingProfile handling = ShipAgilityPresets.Resolve(profile);

            float targetSpeedKnots = ShipKinematicUtility.GetTargetSpeedKnots(speedMode, profile);
            float cruiseSpeedKnots = Mathf.Max(0f, profile.cruiseSpeedKnots);

            float[] distances = BuildCumulativeDistances(geometryPath);
            float totalDistance = distances[distances.Length - 1];

            if (totalDistance <= 0.0001f)
            {
                return;
            }

            float[] pointSpeedLimits = BuildTurnSpeedLimits(
                geometryPath,
                profile,
                handling,
                targetSpeedKnots,
                cruiseSpeedKnots);

            float sampleStep = CalculateSampleStep(profile);
            float decelWorld = MovementMath.KnotsPerSecondToWorldUnitsPerSecondSquared(
                profile.decelerationKnotsPerSecond,
                profile.metersPerWorldUnit);

            float estimatedTravelSeconds = 0f;
            Vector2 previousPosition = geometryPath[0];
            float previousSpeed = targetSpeedKnots;

            for (float distance = 0f; distance < totalDistance; distance += sampleStep)
            {
                Vector2 position = GetPositionAtDistance(
                    geometryPath,
                    distances,
                    distance);

                float plannedSpeed = CalculatePlannedSpeedAtDistance(
                    distance,
                    totalDistance,
                    distances,
                    pointSpeedLimits,
                    targetSpeedKnots,
                    decelWorld,
                    profile,
                    out SpeedLimiter limiter);

                RouteSegmentIntent intent = DetermineSegmentIntent(
                    speedMode,
                    plannedSpeed,
                    cruiseSpeedKnots,
                    limiter);

                ShipRouteSample sample = new ShipRouteSample(
                    position,
                    plannedSpeed,
                    intent);

                routeOut.PredictedSamples.Add(sample);
                routeOut.ControlPoints.Add(new ShipRoutePoint(position, plannedSpeed));

                estimatedTravelSeconds += EstimateStepSeconds(
                    previousPosition,
                    position,
                    previousSpeed,
                    plannedSpeed,
                    profile);

                previousPosition = position;
                previousSpeed = plannedSpeed;
            }

            Vector2 finalPosition = geometryPath[geometryPath.Count - 1];
            routeOut.PredictedSamples.Add(new ShipRouteSample(
                finalPosition,
                0f,
                RouteSegmentIntent.Stop));

            routeOut.ControlPoints.Add(new ShipRoutePoint(
                finalPosition,
                0f));

            estimatedTravelSeconds += EstimateStepSeconds(
                previousPosition,
                finalPosition,
                previousSpeed,
                0f,
                profile);

            routeOut.TotalDistanceWorld = totalDistance;
            routeOut.EstimatedTimeSeconds = estimatedTravelSeconds;
            routeOut.IsValid = routeOut.ControlPoints.Count >= 2 && routeOut.PredictedSamples.Count >= 2;
        }

        private static float[] BuildTurnSpeedLimits(
            IReadOnlyList<Vector2> path,
            MovementProfileDefinition profile,
            ShipHandlingProfile handling,
            float targetSpeedKnots,
            float cruiseSpeedKnots)
        {
            float[] limits = new float[path.Count];

            for (int i = 1; i < path.Count - 1; i++)
            {
                Vector2 inbound = path[i] - path[i - 1];
                Vector2 outbound = path[i + 1] - path[i];

                if (inbound.sqrMagnitude < 0.0001f || outbound.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                float turnAngle = Mathf.Abs(Vector2.SignedAngle(inbound.normalized, outbound.normalized));

                if (turnAngle < TurnSlowAngleDegrees)
                {
                    continue;
                }

                float turn01 = Mathf.InverseLerp(TurnSlowAngleDegrees, TightTurnAngleDegrees, turnAngle);
                float tightSpeed = cruiseSpeedKnots * handling.TightTurnSpeedMultiplier;
                float limit = Mathf.Lerp(cruiseSpeedKnots, tightSpeed, turn01);

                limits[i] = limit < targetSpeedKnots - EpsilonKnots
                    ? Mathf.Max(0f, limit) : 0f;
            }
            return limits;
        }

        private static float CalculatePlannedSpeedAtDistance(
            float distance,
            float totalDistance,
            float[] pointDistances,
            float[] pointSpeedLimits,
            float targetSpeedKnots,
            float decelWorld,
            MovementProfileDefinition profile,
            out SpeedLimiter limiter)
        {
            limiter = SpeedLimiter.None;
            float plannedSpeed = targetSpeedKnots;

            for (int i = 1; i < pointDistances.Length; i++)
            {
                float limit = pointSpeedLimits[i];

                if (limit <= 0f || pointDistances[i] < distance) { continue; }

                float distanceToTurn = pointDistances[i] - distance;
                float limitWorld = MovementMath.KnotsToWorldUnitsPerSecond(limit, profile.metersPerWorldUnit);
                float safeWorld = Mathf.Sqrt(limitWorld * limitWorld + 2f * decelWorld * distanceToTurn);
                float safeKnots = MovementMath.WorldUnitsPerSecondToKnots(safeWorld, profile.metersPerWorldUnit);

                if (safeKnots < plannedSpeed)
                {
                    plannedSpeed = safeKnots;
                    limiter = SpeedLimiter.Turn;
                }
            }

            float remainingToFinal = Mathf.Max(0f, totalDistance - distance - profile.stoppingDistance);
            float finalSafeWorld = Mathf.Sqrt(2f * decelWorld * remainingToFinal);
            float finalSafeKnots = MovementMath.WorldUnitsPerSecondToKnots(finalSafeWorld, profile.metersPerWorldUnit);

            if (finalSafeKnots < plannedSpeed)
            {
                plannedSpeed = finalSafeKnots;
                limiter = SpeedLimiter.FinalStop;
            }

            return Mathf.Max(0f, plannedSpeed);
        }

        private static RouteSegmentIntent DetermineSegmentIntent(
            MovementSpeedMode speedMode,
            float plannedSpeedKnots,
            float cruiseSpeedKnots,
            SpeedLimiter limiter)
        {
            if (limiter == SpeedLimiter.FinalStop && plannedSpeedKnots < cruiseSpeedKnots - EpsilonKnots)
            {
                return RouteSegmentIntent.Stop;
            }

            if (plannedSpeedKnots < cruiseSpeedKnots - EpsilonKnots)
            {
                return RouteSegmentIntent.Slow;
            }

            if (limiter != SpeedLimiter.None)
            {
                return RouteSegmentIntent.Cruise;
            }

            return speedMode == MovementSpeedMode.Flank
                ? RouteSegmentIntent.Flank 
                : RouteSegmentIntent.Cruise;
        }

        private static float[] BuildCumulativeDistances(IReadOnlyList<Vector2> path)
        {
            float[] distances = new float [path.Count];

            for (int i = 1; i < path.Count; i++)
            {
                distances[i] = distances[i - 1] + Vector2.Distance(path[i - 1], path[i]);
            }
            return distances;
        }

        private static Vector2 GetPositionAtDistance (
            IReadOnlyList<Vector2> path,
            float[] distances,
            float distance)
        {
            for (int i = 1; i < path.Count; i++)
            {
                if (distances[i] < distance) { continue; }

                float segmentStart = distances[i - 1];
                float segmentLength = Mathf.Max(0.0001f, distances[i] - segmentStart);
                float t = Mathf.Clamp01((distance - segmentStart) / segmentLength);
                return Vector2.Lerp(path[i - 1], path[i], t);
            }
            return path[path.Count - 1];
        }

        private static float CalculateSampleStep(MovementProfileDefinition profile)
        {
            float shipLengthWorld = Mathf.Max(1f, profile.lengthMeters) / Mathf.Max(0.001f, profile.metersPerWorldUnit);
            return Mathf.Clamp(shipLengthWorld * 0.15f, 0.05f, 0.5f);
        }

        private static float EstimateStepSeconds(
            Vector2 a,
            Vector2 b,
            float speedA,
            float speedB,
            MovementProfileDefinition profile)
        {
            float distance = Vector2.Distance(a, b);
            float averageSpeedKnots = Mathf.Max(0.1f, (speedA + speedB) * 0.5f);
            float averageSpeedWorld = MovementMath.KnotsToWorldUnitsPerSecond(
                averageSpeedKnots, 
                profile.metersPerWorldUnit);

            float simulationSeconds = distance / Mathf.Max(0.001f, averageSpeedWorld);
            return simulationSeconds / Mathf.Max(0.001f, profile.simulationSecondsPerRealSecond);
        }
    }
}