// KinematicRoutePlanner.cs:
// Turns a legal geometric path into speed-limited guidance, then simulates
// the real inertial ship along that guidance. The resulting samples are the
// line the player sees and the path that navigation validates.
using System.Collections.Generic;
using OA.Simulation.Navigation;
using OA.Simulation.Units;
using UnityEngine;

namespace OA.Simulation.Movement
{
    public static class KinematicRoutePlanner
    {
        private const float TurnSlowAngleDegrees = 25f;
        private const float TightTurnAngleDegrees = 60f;
        private const float EpsilonKnots = 0.05f;
        private const float PredictionSampleSpacing = 0.025f;
        private const int MaximumPredictionSteps = 30000;

        private enum SpeedLimiter
        {
            None,
            Turn,
            FinalStop
        }

        // Produces a route only when the ship's predicted physical track remains
        // legal for the active draft class, safety radius, and terrain mask.
        public static void BuildRoute(
            IReadOnlyList<Vector2> geometryPath,
            MovementState initialState,
            MovementProfileDefinition profile,
            MovementSpeedMode speedMode,
            HexMapRuntime map,
            bool[] blockedMask,
            float fixedDeltaTime,
            float lookAheadDistance,
            float arrivalDistance,
            float constrainedTurnSpeedScale,
            ShipRoute routeOut,
            float maximumUsefulArrivalTimeSeconds = float.PositiveInfinity)
        {
            if (routeOut == null)
            {
                return;
            }

            routeOut.Clear();

            if (geometryPath == null ||
                geometryPath.Count < 2 ||
                profile == null)
            {
                routeOut.Reject(
                    ShipRouteFailureReason.InvalidRequest,
                    initialState.Position,
                    0f);

                return;
            }

            BuildGuidanceRoute(
                geometryPath,
                profile,
                speedMode,
                constrainedTurnSpeedScale,
                routeOut);

            if (routeOut.ControlPoints.Count < 2)
            {
                routeOut.Reject(
                    ShipRouteFailureReason.NoGuidanceCourse,
                    initialState.Position,
                    0f);

                return;
            }

            PredictPhysicalRoute(
                initialState,
                profile,
                speedMode,
                map,
                blockedMask,
                fixedDeltaTime,
                lookAheadDistance,
                arrivalDistance,
                routeOut,
                maximumUsefulArrivalTimeSeconds);
        }

        // Colors and speed limits originate on the geometry route. They are private
        // steering instructions, not yet the physical line the player will see.
        private static void BuildGuidanceRoute(
            IReadOnlyList<Vector2> geometryPath,
            MovementProfileDefinition profile,
            MovementSpeedMode speedMode,
            float constrainedTurnSpeedScale,
            ShipRoute routeOut)
        {
            ShipHandlingProfile handling = ShipAgilityPresets.Resolve(profile);

            float targetSpeedKnots = ShipKinematicUtility.GetTargetSpeedKnots(
                speedMode,
                profile);

            float cruiseSpeedKnots = Mathf.Max(0f, profile.cruiseSpeedKnots);
            float[] distances = BuildCumulativeDistances(geometryPath);
            float totalDistance = distances[distances.Length - 1];

            if (totalDistance <= 0.0001f)
            {
                return;
            }

            float[] pointSpeedLimits = BuildTurnSpeedLimits(
                geometryPath,
                handling,
                targetSpeedKnots,
                cruiseSpeedKnots,
                constrainedTurnSpeedScale);

            float sampleStep = CalculateSampleStep(profile);

            float decelerationWorld =
                MovementMath.KnotsPerSecondToWorldUnitsPerSecondSquared(
                    profile.decelerationKnotsPerSecond,
                    profile.metersPerWorldUnit);

            float sampledDistance = 0f;
            int nextGeometryPoint = 1;

            // Regular speed samples make steering smooth. Original geometry
            // vertices are also inserted exactly so intermediate user waypoints
            // cannot disappear between two sample intervals.
            while (sampledDistance < totalDistance ||
                   nextGeometryPoint < geometryPath.Count - 1)
            {
                float vertexDistance = nextGeometryPoint < geometryPath.Count - 1
                    ? distances[nextGeometryPoint]
                    : float.PositiveInfinity;

                float distance = Mathf.Min(sampledDistance, vertexDistance);

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
                    decelerationWorld,
                    profile,
                    out SpeedLimiter limiter);

                RouteSegmentIntent intent = DetermineSegmentIntent(
                    speedMode,
                    plannedSpeed,
                    cruiseSpeedKnots,
                    limiter);

                routeOut.ControlPoints.Add(new ShipRoutePoint(
                    position,
                    distance,
                    plannedSpeed,
                    intent));

                if (Mathf.Abs(distance - sampledDistance) <= 0.0001f)
                {
                    sampledDistance += sampleStep;
                }

                if (Mathf.Abs(distance - vertexDistance) <= 0.0001f)
                {
                    nextGeometryPoint++;
                }
            }

            Vector2 finalPosition = geometryPath[geometryPath.Count - 1];

            routeOut.ControlPoints.Add(new ShipRoutePoint(
                finalPosition,
                totalDistance,
                0f,
                RouteSegmentIntent.Stop));
        }

        // Runs the movement model forward from the ship's current rudder, yaw,
        // velocity, and heading. These output samples are the honest route line.
        private static void PredictPhysicalRoute(
            MovementState initialState,
            MovementProfileDefinition profile,
            MovementSpeedMode speedMode,
            HexMapRuntime map,
            bool[] blockedMask,
            float fixedDeltaTime,
            float lookAheadDistance,
            float arrivalDistance,
            ShipRoute routeOut,
            float maximumUsefulArrivalTimeSeconds)
        {
            float dt = Mathf.Max(0.005f, fixedDeltaTime);

            MovementState simulatedState = initialState;
            ShipMovementModel movementModel = new ShipMovementModel();
            ShipRouteFollowState followState = new ShipRouteFollowState();
            followState.Reset();

            float predictedDistance = 0f;
            float predictedTime = 0f;

            ShipRoutePoint firstPoint = routeOut.ControlPoints[0];

            routeOut.PredictedSamples.Add(new ShipRouteSample(
                simulatedState.Position,
                simulatedState.SpeedKnots,
                firstPoint.SpeedLimitKnots,
                firstPoint.SegmentIntent));

            for (int step = 0; step < MaximumPredictionSteps; step++)
            {
                MovementCommand command = ShipRouteFollower.BuildCommand(
                    simulatedState,
                    routeOut.ControlPoints,
                    ref followState,
                    speedMode,
                    lookAheadDistance,
                    arrivalDistance,
                    step == 0,
                    out RouteSegmentIntent intent);

                MovementState nextState = movementModel.Step(
                    simulatedState,
                    command,
                    profile,
                    dt);

                if (!IsPredictedSegmentTraversable(
                    map,
                    blockedMask,
                    simulatedState.Position,
                    nextState.Position))
                {
                    routeOut.Reject(
                        ShipRouteFailureReason.PredictedTrackBlocked,
                        nextState.Position,
                        predictedTime + dt);

                    return;
                }

                predictedDistance += Vector2.Distance(
                    simulatedState.Position,
                    nextState.Position);

                predictedTime += dt;

                AppendPredictedSample(
                    routeOut.PredictedSamples,
                    nextState.Position,
                    nextState.SpeedKnots,
                    command.SpeedLimitKnots,
                    intent,
                    followState.IsComplete);

                simulatedState = nextState;

                if (followState.IsComplete)
                {
                    routeOut.TotalDistanceWorld = predictedDistance;
                    routeOut.EstimatedTimeSeconds = predictedTime;
                    routeOut.IsValid = routeOut.PredictedSamples.Count >= 2;
                    return;
                }

                // Once this candidate is already later than an accepted route,
                // no future movement can make it the fastest arrival.
                if (!float.IsPositiveInfinity(maximumUsefulArrivalTimeSeconds) &&
                    predictedTime > maximumUsefulArrivalTimeSeconds + 0.0001f)
                {
                    routeOut.Reject(
                        ShipRouteFailureReason.SlowerThanBestArrival,
                        simulatedState.Position,
                        predictedTime);

                    return;
                }
            }

            // If a route never arrives within the simulation budget, it is not an executable order.
            routeOut.Reject(
                ShipRouteFailureReason.PredictionBudgetExceeded,
                simulatedState.Position,
                predictedTime);
        }

        // Keeps the rendered polyline light enough to draw while preserving every color transition.
        private static void AppendPredictedSample(
            List<ShipRouteSample> samples,
            Vector2 position,
            float speedKnots,
            float speedLimitKnots,
            RouteSegmentIntent intent,
            bool forceAppend)
        {
            if (samples.Count > 0 && !forceAppend)
            {
                ShipRouteSample previous = samples[samples.Count - 1];
                bool sameIntent = previous.SegmentIntent == intent;
                bool close = Vector2.Distance(previous.Position, position) <
                             PredictionSampleSpacing;

                if (sameIntent && close)
                {
                    return;
                }
            }

            samples.Add(new ShipRouteSample(
                position,
                speedKnots,
                speedLimitKnots,
                intent));
        }

        // Samples every predicted movement slice against the same blocked mask A* used.
        private static bool IsPredictedSegmentTraversable(
            HexMapRuntime map,
            bool[] blockedMask,
            Vector2 a,
            Vector2 b)
        {
            if (map == null)
            {
                return true;
            }

            float distance = Vector2.Distance(a, b);
            float sampleStep = Mathf.Max(0.02f, map.CellSize * 0.12f);
            int samples = Mathf.Max(1, Mathf.CeilToInt(distance / sampleStep));

            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector2 position = Vector2.Lerp(a, b, t);

                if (!map.TryWorldToCell(position, out Vector2Int cell))
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

        private static bool IsTraversable(
            HexMapRuntime map,
            bool[] blockedMask,
            Vector2Int cell)
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

            return index >= 0 &&
                   index < blockedMask.Length &&
                   !blockedMask[index];
        }

        // Tight corners establish low speed limits. The distance-to-limit pass below
        // paints the braking approach as Cruise or Slow before the ship reaches the turn.
        private static float[] BuildTurnSpeedLimits(
            IReadOnlyList<Vector2> path,
            ShipHandlingProfile handling,
            float targetSpeedKnots,
            float cruiseSpeedKnots,
            float constrainedTurnSpeedScale)
        {
            float[] limits = new float[path.Count];

            for (int i = 1; i < path.Count - 1; i++)
            {
                Vector2 inbound = path[i] - path[i - 1];
                Vector2 outbound = path[i + 1] - path[i];

                if (inbound.sqrMagnitude < 0.0001f ||
                    outbound.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                float turnAngle = Mathf.Abs(Vector2.SignedAngle(
                    inbound.normalized,
                    outbound.normalized));

                if (turnAngle < TurnSlowAngleDegrees)
                {
                    continue;
                }

                float turnFactor = Mathf.InverseLerp(
                    TurnSlowAngleDegrees,
                    TightTurnAngleDegrees,
                    turnAngle);

                float tightSpeed =
                    cruiseSpeedKnots *
                    handling.TightTurnSpeedMultiplier *
                    Mathf.Clamp(constrainedTurnSpeedScale, 0.02f, 1f);

                float limit = Mathf.Lerp(
                    cruiseSpeedKnots,
                    tightSpeed,
                    turnFactor);

                if (limit < targetSpeedKnots - EpsilonKnots)
                {
                    limits[i] = Mathf.Max(0f, limit);
                }
            }

            return limits;
        }

        private static float CalculatePlannedSpeedAtDistance(
            float distance,
            float totalDistance,
            float[] pointDistances,
            float[] pointSpeedLimits,
            float targetSpeedKnots,
            float decelerationWorld,
            MovementProfileDefinition profile,
            out SpeedLimiter limiter)
        {
            limiter = SpeedLimiter.None;
            float plannedSpeed = targetSpeedKnots;

            for (int i = 1; i < pointDistances.Length; i++)
            {
                float limit = pointSpeedLimits[i];

                if (limit <= 0f || pointDistances[i] < distance)
                {
                    continue;
                }

                float distanceToTurn = pointDistances[i] - distance;

                float limitWorld = MovementMath.KnotsToWorldUnitsPerSecond(
                    limit,
                    profile.metersPerWorldUnit);

                float safeWorld = Mathf.Sqrt(
                    limitWorld * limitWorld +
                    2f * decelerationWorld * distanceToTurn);

                float safeKnots = MovementMath.WorldUnitsPerSecondToKnots(
                    safeWorld,
                    profile.metersPerWorldUnit);

                if (safeKnots < plannedSpeed)
                {
                    plannedSpeed = safeKnots;
                    limiter = SpeedLimiter.Turn;
                }
            }

            float remainingToFinal = Mathf.Max(
                0f,
                totalDistance - distance - profile.stoppingDistance);

            float finalSafeWorld = Mathf.Sqrt(
                2f * decelerationWorld * remainingToFinal);

            float finalSafeKnots = MovementMath.WorldUnitsPerSecondToKnots(
                finalSafeWorld,
                profile.metersPerWorldUnit);

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
            if (limiter == SpeedLimiter.FinalStop &&
                plannedSpeedKnots < cruiseSpeedKnots - EpsilonKnots)
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

        private static float[] BuildCumulativeDistances(
            IReadOnlyList<Vector2> path)
        {
            float[] distances = new float[path.Count];

            for (int i = 1; i < path.Count; i++)
            {
                distances[i] = distances[i - 1] +
                               Vector2.Distance(path[i - 1], path[i]);
            }

            return distances;
        }

        private static Vector2 GetPositionAtDistance(
            IReadOnlyList<Vector2> path,
            float[] distances,
            float distance)
        {
            for (int i = 1; i < path.Count; i++)
            {
                if (distances[i] < distance)
                {
                    continue;
                }

                float segmentStart = distances[i - 1];
                float segmentLength = Mathf.Max(
                    0.0001f,
                    distances[i] - segmentStart);

                float t = Mathf.Clamp01(
                    (distance - segmentStart) / segmentLength);

                return Vector2.Lerp(path[i - 1], path[i], t);
            }

            return path[path.Count - 1];
        }

        private static float CalculateSampleStep(
            MovementProfileDefinition profile)
        {
            float shipLengthWorld =
                Mathf.Max(1f, profile.lengthMeters) /
                Mathf.Max(0.001f, profile.metersPerWorldUnit);

            return Mathf.Clamp(shipLengthWorld * 0.15f, 0.05f, 0.5f);
        }
    }
}
