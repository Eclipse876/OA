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
            IReadOnlyList<ShipRouteGate> gates,
            MovementState initialState,
            MovementProfileDefinition profile,
            MovementSpeedMode speedMode,
            HexMapRuntime map,
            bool[] blockedMask,
            float fixedDeltaTime,
            float lookAheadDistance,
            float arrivalDistance,
            float constrainedTurnSpeedScale,
            float courseSmoothingStrength,
            ShipRoute routeOut)
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

            List<Vector2> shapedPath = new List<Vector2>(geometryPath.Count * 4);
            List<float> shapedSpeedLimits = new List<float>(geometryPath.Count * 4);

            KinematicCourseShaper.BuildCourse(
                geometryPath,
                gates,
                profile,
                speedMode,
                constrainedTurnSpeedScale,
                courseSmoothingStrength,
                shapedPath,
                shapedSpeedLimits);

            BuildGuidanceRoute(
                shapedPath,
                shapedSpeedLimits,
                profile,
                speedMode,
                routeOut);

            if (routeOut.ControlPoints.Count < 2)
            {
                routeOut.Reject(
                    ShipRouteFailureReason.NoGuidanceCourse,
                    initialState.Position,
                    0f);

                return;
            }

            if (gates != null)
            {
                routeOut.Gates.AddRange(gates);
            }

            if (!ResolveGuidanceGateTargets(routeOut, out int missingGuidanceGateIndex))
            {
                routeOut.Reject(
                    ShipRouteFailureReason.MissedRequiredWaypoint,
                    initialState.Position,
                    0f,
                    missingGuidanceGateIndex);

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
                routeOut);
        }

        // Colors and speed limits originate on the geometry route. They are private
        // steering instructions, not yet the physical line the player will see.
        private static void BuildGuidanceRoute(
            IReadOnlyList<Vector2> geometryPath,
            IReadOnlyList<float> pointSpeedLimits,
            MovementProfileDefinition profile,
            MovementSpeedMode speedMode,
            ShipRoute routeOut)
        {
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
            ShipRoute routeOut)
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
                    routeOut.Gates,
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

                    if (!ResolveGateCrossings(routeOut, out int missedGateIndex))
                    {
                        routeOut.Reject(
                            ShipRouteFailureReason.MissedRequiredWaypoint,
                            simulatedState.Position,
                            predictedTime,
                            missedGateIndex);

                        return;
                    }

                    routeOut.IsValid = routeOut.PredictedSamples.Count >= 2;
                    CalculateManeuverScore(routeOut);
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
            IReadOnlyList<float> pointSpeedLimits,
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

        // Gives the follower a point inside each pass-through gate that keeps
        // steering constrained until the physical hull has satisfied the order.
        private static bool ResolveGuidanceGateTargets(
            ShipRoute route,
            out int missingGateIndex)
        {
            missingGateIndex = -1;

            if (route.Gates.Count == 0)
            {
                return true;
            }

            float searchFromDistance = 0f;
            float finalDistance =
                route.ControlPoints[route.ControlPoints.Count - 1].DistanceFromStartWorld;

            for (int gateIndex = 0; gateIndex < route.Gates.Count; gateIndex++)
            {
                ShipRouteGate gate = route.Gates[gateIndex];

                if (gate.MustStop)
                {
                    gate.GuidanceTargetDistanceWorld = finalDistance;
                    route.Gates[gateIndex] = gate;
                    continue;
                }

                bool foundGateRegion = false;
                float closestDistance = float.PositiveInfinity;
                float targetDistance = -1f;

                for (int pointIndex = 0;
                     pointIndex < route.ControlPoints.Count - 1;
                     pointIndex++)
                {
                    ShipRoutePoint a = route.ControlPoints[pointIndex];
                    ShipRoutePoint b = route.ControlPoints[pointIndex + 1];

                    if (b.DistanceFromStartWorld + 0.0001f < searchFromDistance)
                    {
                        continue;
                    }

                    Vector2 segment = b.Position - a.Position;
                    float segmentLengthSqr = segment.sqrMagnitude;

                    if (segmentLengthSqr <= 0.000001f)
                    {
                        continue;
                    }

                    float t = Mathf.Clamp01(
                        Vector2.Dot(gate.Position - a.Position, segment) /
                        segmentLengthSqr);

                    float projectedDistance = Mathf.Lerp(
                        a.DistanceFromStartWorld,
                        b.DistanceFromStartWorld,
                        t);

                    if (projectedDistance + 0.0001f < searchFromDistance)
                    {
                        continue;
                    }

                    Vector2 projected = Vector2.Lerp(a.Position, b.Position, t);
                    float distance = Vector2.Distance(projected, gate.Position);

                    if (distance > gate.RadiusWorld + 0.0001f)
                    {
                        if (foundGateRegion &&
                            a.DistanceFromStartWorld >
                            targetDistance + gate.RadiusWorld * 2f)
                        {
                            break;
                        }

                        continue;
                    }

                    foundGateRegion = true;

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        targetDistance = projectedDistance;
                    }
                }

                if (!foundGateRegion)
                {
                    missingGateIndex = gateIndex;
                    return false;
                }

                gate.GuidanceTargetDistanceWorld = targetDistance;
                route.Gates[gateIndex] = gate;
                searchFromDistance = targetDistance + 0.0001f;
            }

            return true;
        }

        // A legal physical track must actually pass each ordered gate in sequence.
        private static bool ResolveGateCrossings(
            ShipRoute route,
            out int missedGateIndex)
        {
            missedGateIndex = -1;

            if (route.Gates.Count == 0)
            {
                return true;
            }

            int searchFromSample = 0;

            for (int gateIndex = 0; gateIndex < route.Gates.Count; gateIndex++)
            {
                ShipRouteGate gate = route.Gates[gateIndex];
                int crossingSample = -1;

                for (int sampleIndex = searchFromSample;
                     sampleIndex < route.PredictedSamples.Count - 1;
                     sampleIndex++)
                {
                    float distance = DistanceFromSegmentToPoint(
                        route.PredictedSamples[sampleIndex].Position,
                        route.PredictedSamples[sampleIndex + 1].Position,
                        gate.Position);

                    if (distance <= gate.RadiusWorld)
                    {
                        crossingSample = sampleIndex + 1;
                        break;
                    }
                }

                if (crossingSample < 0)
                {
                    missedGateIndex = gateIndex;
                    return false;
                }

                gate.PredictedCrossingSampleIndex = crossingSample;
                route.Gates[gateIndex] = gate;
                searchFromSample = crossingSample;
            }

            return true;
        }

        // Prefers quick, short physical tracks, with noticeable penalties for
        // avoidable S-turns and for leaving an intermediate order in the wrong
        // direction before recovering toward the next order.
        private static void CalculateManeuverScore(ShipRoute route)
        {
            int reversals = 0;
            int previousTurnSign = 0;

            for (int i = 2; i < route.PredictedSamples.Count; i++)
            {
                Vector2 previous =
                    route.PredictedSamples[i - 1].Position -
                    route.PredictedSamples[i - 2].Position;

                Vector2 next =
                    route.PredictedSamples[i].Position -
                    route.PredictedSamples[i - 1].Position;

                if (previous.sqrMagnitude < 0.000001f ||
                    next.sqrMagnitude < 0.000001f)
                {
                    continue;
                }

                float turn = Vector2.SignedAngle(previous, next);

                if (Mathf.Abs(turn) < 0.25f)
                {
                    continue;
                }

                int turnSign = turn > 0f ? 1 : -1;

                if (previousTurnSign != 0 &&
                    turnSign != previousTurnSign)
                {
                    reversals++;
                }

                previousTurnSign = turnSign;
            }

            float maximumGateBacktrackWorld;
            float gateDeparturePenalty = CalculateGateDeparturePenalty(
                route,
                out maximumGateBacktrackWorld);

            route.HeadingReversalCount = reversals;
            route.GateDeparturePenalty = gateDeparturePenalty;
            route.MaximumGateBacktrackWorld = maximumGateBacktrackWorld;
            route.ManeuverScore =
                route.EstimatedTimeSeconds +
                route.TotalDistanceWorld * 0.2f +
                reversals * 12f +
                gateDeparturePenalty;
        }

        // A pass-through waypoint should leave the ship making useful progress
        // toward the next order. This does not invalidate a wide legal turn; it
        // lets a slower, cleaner physical candidate beat a broad overshoot loop.
        private static float CalculateGateDeparturePenalty(
            ShipRoute route,
            out float maximumGateBacktrackWorld)
        {
            const float BacktrackPenaltyPerWorldUnit = 36f;
            const float BackwardTravelPenaltyPerWorldUnit = 18f;
            const float DepartureAngleGraceDegrees = 35f;
            const float DepartureAnglePenaltyPerDegree = 0.4f;

            maximumGateBacktrackWorld = 0f;
            float penalty = 0f;

            for (int gateIndex = 0; gateIndex < route.Gates.Count - 1; gateIndex++)
            {
                ShipRouteGate gate = route.Gates[gateIndex];

                if (gate.MustStop ||
                    gate.PredictedCrossingSampleIndex < 0 ||
                    gate.PredictedCrossingSampleIndex >= route.PredictedSamples.Count - 1)
                {
                    continue;
                }

                Vector2 desiredDeparture =
                    route.Gates[gateIndex + 1].Position - gate.Position;

                if (desiredDeparture.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                desiredDeparture.Normalize();

                int startSample = gate.PredictedCrossingSampleIndex;
                int maximumEndSample = route.Gates[gateIndex + 1].PredictedCrossingSampleIndex;

                if (maximumEndSample <= startSample)
                {
                    maximumEndSample = route.PredictedSamples.Count - 1;
                }

                float assessmentDistance = Mathf.Max(
                    3f,
                    gate.RadiusWorld * 6f);

                float traveled = 0f;
                float maximumBacktrack = 0f;
                float backwardTravel = 0f;
                int endSample = startSample;

                for (int sampleIndex = startSample;
                     sampleIndex < maximumEndSample &&
                     sampleIndex < route.PredictedSamples.Count - 1 &&
                     traveled < assessmentDistance;
                     sampleIndex++)
                {
                    Vector2 a = route.PredictedSamples[sampleIndex].Position;
                    Vector2 b = route.PredictedSamples[sampleIndex + 1].Position;
                    Vector2 movement = b - a;
                    float segmentDistance = movement.magnitude;

                    if (segmentDistance <= 0.000001f)
                    {
                        continue;
                    }

                    traveled += segmentDistance;
                    endSample = sampleIndex + 1;

                    float progress = Vector2.Dot(b - gate.Position, desiredDeparture);

                    maximumBacktrack = Mathf.Max(
                        maximumBacktrack,
                        Mathf.Max(0f, -progress));

                    backwardTravel += Mathf.Max(
                        0f,
                        -Vector2.Dot(movement, desiredDeparture));
                }

                maximumGateBacktrackWorld = Mathf.Max(
                    maximumGateBacktrackWorld,
                    maximumBacktrack);

                float departureAnglePenalty = 0f;
                Vector2 measuredDeparture =
                    route.PredictedSamples[endSample].Position - gate.Position;

                if (measuredDeparture.sqrMagnitude > 0.000001f)
                {
                    float departureAngle = Vector2.Angle(
                        measuredDeparture,
                        desiredDeparture);

                    departureAnglePenalty =
                        Mathf.Max(0f, departureAngle - DepartureAngleGraceDegrees) *
                        DepartureAnglePenaltyPerDegree;
                }

                penalty +=
                    maximumBacktrack * BacktrackPenaltyPerWorldUnit +
                    backwardTravel * BackwardTravelPenaltyPerWorldUnit +
                    departureAnglePenalty;
            }

            return penalty;
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

    // Converts sharp visibility-path corners into smooth guidance. Terrain
    // corners remain on-course, while intermediate waypoint gates permit a
    // bounded fly-by instead of forcing the ship through one exact point.
    internal static class KinematicCourseShaper
    {
        private const float TurnSlowAngleDegrees = 25f;
        private const float TightTurnAngleDegrees = 60f;
        private const float MinimumCurveAngleDegrees = 3f;
        private const float CurveSampleSpacingWorld = 0.12f;

        public static void BuildCourse(
            IReadOnlyList<Vector2> geometryPath,
            IReadOnlyList<ShipRouteGate> gates,
            MovementProfileDefinition profile,
            MovementSpeedMode speedMode,
            float constrainedTurnSpeedScale,
            float smoothingStrength,
            List<Vector2> pathOut,
            List<float> speedLimitsOut)
        {
            pathOut.Clear();
            speedLimitsOut.Clear();

            if (geometryPath == null || geometryPath.Count == 0)
            {
                return;
            }

            float strength = Mathf.Clamp01(smoothingStrength);
            ShipHandlingProfile handling = ShipAgilityPresets.Resolve(profile);
            float targetSpeedKnots = ShipKinematicUtility.GetTargetSpeedKnots(
                speedMode,
                profile);

            AddPoint(pathOut, speedLimitsOut, geometryPath[0], 0f);

            for (int i = 1; i < geometryPath.Count - 1; i++)
            {
                Vector2 previous = geometryPath[i - 1];
                Vector2 corner = geometryPath[i];
                Vector2 next = geometryPath[i + 1];

                Vector2 incoming = corner - previous;
                Vector2 outgoing = next - corner;

                if (incoming.sqrMagnitude <= 0.000001f ||
                    outgoing.sqrMagnitude <= 0.000001f)
                {
                    AddPoint(pathOut, speedLimitsOut, corner, 0f);
                    continue;
                }

                Vector2 incomingDirection = incoming.normalized;
                Vector2 outgoingDirection = outgoing.normalized;

                float signedAngle = Vector2.SignedAngle(
                    incomingDirection,
                    outgoingDirection);

                float turnAngle = Mathf.Abs(signedAngle);
                float speedLimit = CalculateTurnSpeedLimit(
                    turnAngle,
                    profile,
                    handling,
                    targetSpeedKnots,
                    constrainedTurnSpeedScale);

                if (strength <= 0.0001f ||
                    turnAngle < MinimumCurveAngleDegrees ||
                    turnAngle >= 175f)
                {
                    AddPoint(pathOut, speedLimitsOut, corner, speedLimit);
                    continue;
                }

                float curveSpeed = speedLimit > 0.0001f
                    ? speedLimit
                    : targetSpeedKnots;

                float radiusWorld = ShipKinematicUtility.CalculateTurnRadiusWorld(
                    curveSpeed,
                    profile,
                    handling);

                float halfAngleRadians = turnAngle * 0.5f * Mathf.Deg2Rad;
                float naturalBlend = radiusWorld * Mathf.Tan(halfAngleRadians);
                float maximumBlend = Mathf.Min(
                    incoming.magnitude,
                    outgoing.magnitude) * 0.42f;

                float blend = Mathf.Min(
                    naturalBlend * strength,
                    maximumBlend);

                if (blend <= 0.01f)
                {
                    AddPoint(pathOut, speedLimitsOut, corner, speedLimit);
                    continue;
                }

                float gateRadiusWorld;
                bool passThroughGate = TryGetPassThroughGateRadius(
                    corner,
                    gates,
                    out gateRadiusWorld);

                if (passThroughGate)
                {
                    blend = FitFlyByBlendToGate(
                        corner,
                        incomingDirection,
                        outgoingDirection,
                        blend,
                        gateRadiusWorld);

                    if (blend > 0.01f)
                    {
                        Vector2 flyByEntry =
                            corner - incomingDirection * blend;

                        Vector2 flyByExit =
                            corner + outgoingDirection * blend;

                        AddPoint(pathOut, speedLimitsOut, flyByEntry, speedLimit);

                        AppendQuadraticFlyBy(
                            flyByEntry,
                            corner,
                            flyByExit,
                            speedLimit,
                            pathOut,
                            speedLimitsOut);

                        continue;
                    }
                }

                Vector2 crossingTangent =
                    (incomingDirection + outgoingDirection).normalized;

                if (crossingTangent.sqrMagnitude <= 0.000001f)
                {
                    AddPoint(pathOut, speedLimitsOut, corner, speedLimit);
                    continue;
                }

                Vector2 entry = corner - incomingDirection * blend;
                Vector2 exit = corner + outgoingDirection * blend;
                float controlDistance = blend * 0.45f;

                AddPoint(pathOut, speedLimitsOut, entry, speedLimit);

                AppendCubic(
                    entry,
                    entry + incomingDirection * controlDistance,
                    corner - crossingTangent * controlDistance,
                    corner,
                    speedLimit,
                    pathOut,
                    speedLimitsOut);

                AppendCubic(
                    corner,
                    corner + crossingTangent * controlDistance,
                    exit - outgoingDirection * controlDistance,
                    exit,
                    speedLimit,
                    pathOut,
                    speedLimitsOut);
            }

            AddPoint(
                pathOut,
                speedLimitsOut,
                geometryPath[geometryPath.Count - 1],
                0f);
        }

        // An intermediate order is satisfied by entering its gate radius. The
        // final order is excluded because stopping at its exact point is intentional.
        private static bool TryGetPassThroughGateRadius(
            Vector2 corner,
            IReadOnlyList<ShipRouteGate> gates,
            out float radiusWorld)
        {
            radiusWorld = 0f;

            if (gates == null)
            {
                return false;
            }

            for (int i = 0; i < gates.Count; i++)
            {
                ShipRouteGate gate = gates[i];

                if (!gate.MustStop &&
                    Vector2.Distance(gate.Position, corner) <= 0.001f)
                {
                    radiusWorld = gate.RadiusWorld;
                    return true;
                }
            }

            return false;
        }

        // Preserve the widest useful fly-by that still passes inside the order
        // gate. Physical prediction remains the authority on whether inertia
        // allows the ship itself to make that crossing.
        private static float FitFlyByBlendToGate(
            Vector2 corner,
            Vector2 incomingDirection,
            Vector2 outgoingDirection,
            float desiredBlend,
            float gateRadiusWorld)
        {
            float low = 0f;
            float high = Mathf.Max(0f, desiredBlend);

            for (int i = 0; i < 10; i++)
            {
                float testBlend = (low + high) * 0.5f;
                Vector2 entry = corner - incomingDirection * testBlend;
                Vector2 exit = corner + outgoingDirection * testBlend;

                if (ClosestQuadraticDistanceToCorner(
                        entry,
                        corner,
                        exit,
                        corner) <= gateRadiusWorld)
                {
                    low = testBlend;
                }
                else
                {
                    high = testBlend;
                }
            }

            return low;
        }

        private static float ClosestQuadraticDistanceToCorner(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 corner)
        {
            float closestDistance = float.PositiveInfinity;

            for (int sample = 0; sample <= 16; sample++)
            {
                float t = sample / 16f;
                float oneMinusT = 1f - t;

                Vector2 position =
                    oneMinusT * oneMinusT * a +
                    2f * oneMinusT * t * b +
                    t * t * c;

                closestDistance = Mathf.Min(
                    closestDistance,
                    Vector2.Distance(position, corner));
            }

            return closestDistance;
        }

        private static void AppendQuadraticFlyBy(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            float speedLimit,
            List<Vector2> pathOut,
            List<float> speedLimitsOut)
        {
            float lengthEstimate =
                Vector2.Distance(a, b) +
                Vector2.Distance(b, c);

            int samples = Mathf.Clamp(
                Mathf.CeilToInt(lengthEstimate / CurveSampleSpacingWorld),
                2,
                32);

            for (int sample = 1; sample <= samples; sample++)
            {
                float t = sample / (float)samples;
                float oneMinusT = 1f - t;

                Vector2 position =
                    oneMinusT * oneMinusT * a +
                    2f * oneMinusT * t * b +
                    t * t * c;

                AddPoint(pathOut, speedLimitsOut, position, speedLimit);
            }
        }

        private static float CalculateTurnSpeedLimit(
            float turnAngle,
            MovementProfileDefinition profile,
            ShipHandlingProfile handling,
            float targetSpeedKnots,
            float constrainedTurnSpeedScale)
        {
            if (turnAngle < TurnSlowAngleDegrees)
            {
                return 0f;
            }

            float cruiseSpeedKnots = Mathf.Max(0f, profile.cruiseSpeedKnots);
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

            return limit < targetSpeedKnots - 0.05f
                ? Mathf.Max(0f, limit)
                : 0f;
        }

        private static void AppendCubic(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            float speedLimit,
            List<Vector2> pathOut,
            List<float> speedLimitsOut)
        {
            float lengthEstimate =
                Vector2.Distance(a, b) +
                Vector2.Distance(b, c) +
                Vector2.Distance(c, d);

            int samples = Mathf.Clamp(
                Mathf.CeilToInt(lengthEstimate / CurveSampleSpacingWorld),
                2,
                32);

            for (int sample = 1; sample <= samples; sample++)
            {
                float t = sample / (float)samples;
                float oneMinusT = 1f - t;

                Vector2 position =
                    oneMinusT * oneMinusT * oneMinusT * a +
                    3f * oneMinusT * oneMinusT * t * b +
                    3f * oneMinusT * t * t * c +
                    t * t * t * d;

                AddPoint(pathOut, speedLimitsOut, position, speedLimit);
            }
        }

        private static void AddPoint(
            List<Vector2> pathOut,
            List<float> speedLimitsOut,
            Vector2 position,
            float speedLimit)
        {
            if (pathOut.Count > 0 &&
                Vector2.Distance(pathOut[pathOut.Count - 1], position) <= 0.0001f)
            {
                int lastIndex = speedLimitsOut.Count - 1;
                speedLimitsOut[lastIndex] = MaximumMeaningfulLimit(
                    speedLimitsOut[lastIndex],
                    speedLimit);

                return;
            }

            pathOut.Add(position);
            speedLimitsOut.Add(Mathf.Max(0f, speedLimit));
        }

        // Zero denotes no limit; when two turn regions meet, retain the slower cap.
        private static float MaximumMeaningfulLimit(float a, float b)
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
    }
}
