// ShipRoutePrediction.cs:
// Advances exact inertial route validation in bounded batches so difficult
// planning does not monopolize the frame in which the player issues an order.
using OA.Simulation.Navigation;
using OA.Simulation.Units;
using UnityEngine;

namespace OA.Simulation.Movement
{
    public sealed class ShipRoutePrediction
    {
        private const float PredictionSampleSpacing = 0.025f;
        private const int MaximumPredictionSteps = 30000;
        private const float MinimumUsefulProgressWorld = 0.04f;
        private const float MinimumUsefulFinalDistanceImprovementWorld = 0.04f;

        private readonly ShipMovementModel movementModel = new ShipMovementModel();

        private ShipRoute candidate;
        private MovementProfileDefinition profile;
        private MovementSpeedMode speedMode;
        private HexMapRuntime map;
        private NavigationTraversalMask traversalMask;
        private float dt;
        private float lookAheadDistance;
        private float arrivalDistance;
        private float maximumUsefulArrivalTimeSeconds;
        private float maximumPredictionSeconds;
        private float stagnationSeconds;

        private MovementState simulatedState;
        private ShipRouteFollowState followState;
        private float predictedDistance;
        private float predictedTime;
        private float bestProgressWorld;
        private float bestFinalDistanceWorld;
        private float lastImprovementTime;
        private Vector2 finalPosition;
        private int steps;

        public bool IsRunning { get; private set; }
        public bool IsComplete => !IsRunning;
        public int StepsExecuted => steps;
        public ShipRoute Candidate => candidate;

        public void Begin(
            ShipRoute candidate,
            MovementState initialState,
            MovementProfileDefinition profile,
            MovementSpeedMode speedMode,
            HexMapRuntime map,
            NavigationTraversalMask traversalMask,
            float fixedDeltaTime,
            float lookAheadDistance,
            float arrivalDistance,
            float maximumUsefulArrivalTimeSeconds = float.PositiveInfinity,
            float maximumPredictionSeconds = 45f,
            float stagnationSeconds = 4f)
        {
            this.candidate = candidate;
            this.profile = profile;
            this.speedMode = speedMode;
            this.map = map;
            this.traversalMask = traversalMask;
            this.dt = Mathf.Max(0.005f, fixedDeltaTime);
            this.lookAheadDistance = lookAheadDistance;
            this.arrivalDistance = arrivalDistance;
            this.maximumUsefulArrivalTimeSeconds = maximumUsefulArrivalTimeSeconds;
            this.maximumPredictionSeconds = Mathf.Max(1f, maximumPredictionSeconds);
            this.stagnationSeconds = Mathf.Max(this.dt, stagnationSeconds);

            simulatedState = initialState;
            followState.Reset();
            predictedDistance = 0f;
            predictedTime = 0f;
            bestProgressWorld = 0f;
            bestFinalDistanceWorld = 0f;
            lastImprovementTime = 0f;
            finalPosition = initialState.Position;
            steps = 0;
            IsRunning = candidate != null &&
                        candidate.ControlPoints.Count >= 2 &&
                        profile != null;

            if (!IsRunning)
            {
                candidate?.Reject(
                    ShipRouteFailureReason.InvalidRequest,
                    initialState.Position,
                    0f);
                return;
            }

            candidate.PredictedSamples.Clear();
            candidate.TotalDistanceWorld = 0f;
            candidate.EstimatedTimeSeconds = 0f;
            candidate.IsValid = false;
            candidate.TraversalMask = traversalMask;

            ShipRoutePoint firstPoint = candidate.ControlPoints[0];
            ShipRoutePoint finalPoint =
                candidate.ControlPoints[candidate.ControlPoints.Count - 1];

            finalPosition = finalPoint.Position;
            bestFinalDistanceWorld = Vector2.Distance(
                initialState.Position,
                finalPosition);

            candidate.PredictedSamples.Add(new ShipRouteSample(
                simulatedState.Position,
                simulatedState.SpeedKnots,
                firstPoint.SpeedLimitKnots,
                firstPoint.SegmentIntent));
        }

        public void Cancel()
        {
            IsRunning = false;
            candidate = null;
        }

        public void Advance(int maximumSteps)
        {
            if (!IsRunning)
            {
                return;
            }

            int batchEnd = Mathf.Min(
                MaximumPredictionSteps,
                steps + Mathf.Max(1, maximumSteps));

            while (steps < batchEnd)
            {
                MovementCommand command = ShipRouteFollower.BuildCommand(
                    simulatedState,
                    candidate.ControlPoints,
                    ref followState,
                    speedMode,
                    profile,
                    map,
                    traversalMask,
                    lookAheadDistance,
                    arrivalDistance,
                    steps == 0,
                    out RouteSegmentIntent intent);

                command = ShipRouteFollower.ApplyConfinedPivotIfNeeded(
                    simulatedState,
                    command,
                    arrivalDistance,
                    profile,
                    map,
                    traversalMask,
                    dt,
                    movementModel);

                MovementState nextState = movementModel.Step(
                    simulatedState,
                    command,
                    profile,
                    dt);

                if (!IsPredictedStepTraversable(
                    command,
                    map,
                    traversalMask,
                    simulatedState.Position,
                    nextState.Position))
                {
                    candidate.Reject(
                        ShipRouteFailureReason.PredictedTrackBlocked,
                        nextState.Position,
                        predictedTime + dt);
                    IsRunning = false;
                    return;
                }

                predictedDistance += Vector2.Distance(
                    simulatedState.Position,
                    nextState.Position);
                predictedTime += dt;
                steps++;

                AppendPredictedSample(
                    nextState.Position,
                    nextState.SpeedKnots,
                    command.SpeedLimitKnots,
                    intent,
                    followState.IsComplete);

                simulatedState = nextState;
                UpdateImprovementTracking(nextState.Position);

                if (followState.IsComplete)
                {
                    candidate.TotalDistanceWorld = predictedDistance;
                    candidate.EstimatedTimeSeconds = predictedTime;
                    candidate.IsValid = candidate.PredictedSamples.Count >= 2;
                    IsRunning = false;
                    return;
                }

                if (predictedTime > maximumPredictionSeconds)
                {
                    candidate.Reject(
                        ShipRouteFailureReason.PredictionBudgetExceeded,
                        simulatedState.Position,
                        predictedTime);
                    IsRunning = false;
                    return;
                }

                if (predictedTime - lastImprovementTime >= stagnationSeconds)
                {
                    candidate.Reject(
                        ShipRouteFailureReason.CandidateStalled,
                        simulatedState.Position,
                        predictedTime);
                    IsRunning = false;
                    return;
                }

                if (!float.IsPositiveInfinity(maximumUsefulArrivalTimeSeconds) &&
                    predictedTime > maximumUsefulArrivalTimeSeconds + 0.0001f)
                {
                    candidate.Reject(
                        ShipRouteFailureReason.SlowerThanBestArrival,
                        simulatedState.Position,
                        predictedTime);
                    IsRunning = false;
                    return;
                }
            }

            if (steps >= MaximumPredictionSteps)
            {
                candidate.Reject(
                    ShipRouteFailureReason.PredictionBudgetExceeded,
                    simulatedState.Position,
                    predictedTime);
                IsRunning = false;
            }
        }

        private static bool IsPredictedStepTraversable(
            MovementCommand command,
            HexMapRuntime map,
            NavigationTraversalMask traversalMask,
            Vector2 start,
            Vector2 end)
        {
            if (command.Intent == MovementIntent.Pivot)
            {
                return map != null &&
                       map.TryWorldToCell(start, out Vector2Int cell) &&
                       map.IsWalkable(cell.x, cell.y);
            }

            return RouteSegmentUtility.IsSegmentTraversable(
                map,
                traversalMask,
                start,
                end);
        }

        // Candidates that circle near a waypoint can keep burning simulation
        // steps without ever arriving. Track useful improvement and reject them
        // quickly once they stop getting closer or progressing along the route.
        private void UpdateImprovementTracking(Vector2 position)
        {
            bool improved = false;

            if (followState.ProgressWorld >
                bestProgressWorld + MinimumUsefulProgressWorld)
            {
                bestProgressWorld = followState.ProgressWorld;
                improved = true;
            }

            float finalDistance = Vector2.Distance(position, finalPosition);

            if (finalDistance <
                bestFinalDistanceWorld - MinimumUsefulFinalDistanceImprovementWorld)
            {
                bestFinalDistanceWorld = finalDistance;
                improved = true;
            }

            if (improved)
            {
                lastImprovementTime = predictedTime;
            }
        }

        private void AppendPredictedSample(
            Vector2 position,
            float speedKnots,
            float speedLimitKnots,
            RouteSegmentIntent intent,
            bool forceAppend)
        {
            if (candidate.PredictedSamples.Count > 0 && !forceAppend)
            {
                ShipRouteSample previous =
                    candidate.PredictedSamples[candidate.PredictedSamples.Count - 1];

                if (previous.SegmentIntent == intent &&
                    Vector2.Distance(previous.Position, position) <
                    PredictionSampleSpacing)
                {
                    return;
                }
            }

            candidate.PredictedSamples.Add(new ShipRouteSample(
                position,
                speedKnots,
                speedLimitKnots,
                intent));
        }
    }
}
