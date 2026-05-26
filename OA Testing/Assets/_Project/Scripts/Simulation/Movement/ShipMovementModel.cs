using OA.Simulation.Movement;
using OA.Simulation.Units;
using UnityEngine;

namespace OA.Simulation.Movement
{
    public sealed class ShipMovementModel
    {
        public MovementState Step(
               MovementState state,
               MovementCommand command,
               MovementProfileDefinition profile,
               float deltaTime)
        {
            if (profile == null || deltaTime <= 0f)
            {
                return state;
            }

            ShipHandlingProfile handling = ShipAgilityPresets.Resolve(profile);
            float simDt = deltaTime * Mathf.Max(0.001f, profile.simulationSecondsPerRealSecond);

            float targetSpeedKnots = GetCommandTargetSpeedKnots(command, profile);
            float safeSpeedKnots = GetNormalBrakingSafeSpeedKnots(command, profile);

            if (command.Intent == MovementIntent.Move)
            {
                targetSpeedKnots = Mathf.Min(targetSpeedKnots, safeSpeedKnots);

                if (command.SpeedLimitKnots > 0.0001f)
                {
                    targetSpeedKnots = Mathf.Min(targetSpeedKnots, command.SpeedLimitKnots);
                }

            }

            bool useMaxDeceleration = ShouldUseMaxDeceleration(state, command, profile, safeSpeedKnots, simDt);

            float speedRate = state.SpeedKnots < targetSpeedKnots
                ? profile.accelerationKnotsPerSecond
                : useMaxDeceleration
                    ? profile.maxDecelerationKnotsPerSecond
                    : profile.decelerationKnotsPerSecond;

            float nextSpeedKnots = Mathf.MoveTowards(
                state.SpeedKnots, targetSpeedKnots, Mathf.Max(0f, speedRate) * simDt);

            float desiredHeading = state.HeadingDegrees;

            bool hasSteeringTarget = false;

            if (command.Intent == MovementIntent.Move)
            {
                Vector2 toTarget = command.SteeringTarget - state.Position;

                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    desiredHeading = MovementMath.DirectionToHeadingDegrees(toTarget);
                    hasSteeringTarget = true;
                }
            }

            float desiredRudder = 0f;
            if (hasSteeringTarget)
            {
                float headingDelta = Mathf.DeltaAngle(state.HeadingDegrees, desiredHeading);
                desiredRudder = Mathf.Clamp(headingDelta / 45f, -1f, 1f);
            }

            float rudderRate = 2f / Mathf.Max(0.001f, handling.RudderShiftTimeSeconds);
            float nextRudder = Mathf.MoveTowards(state.Rudder, desiredRudder, rudderRate * simDt);

            float maxTurnRate = ShipKinematicUtility.CalculateTurnRateDegreesPerSecond(
                nextSpeedKnots,
                profile,
                handling);

            float targetYawRate = nextRudder * maxTurnRate;
            float nextYawRate = Mathf.MoveTowards(
                state.YawRateDegreesPerSecond,
                targetYawRate,
                handling.YawAccelerationDegreesPerSecondSquared * simDt);

            if (Mathf.Abs(desiredRudder) < 0.001f)
            {
                float dampingAlpha = 1f - Mathf.Exp(-handling.YawDampingPerSecond * simDt);
                nextYawRate = Mathf.Lerp(nextYawRate, 0f, dampingAlpha);
            }

            float nextHeading = state.HeadingDegrees + nextYawRate * simDt;

            float turnIntensity = maxTurnRate > 0.001f
                ? Mathf.Clamp01(Mathf.Abs(nextYawRate) / maxTurnRate)
                : 0f;

            if (turnIntensity > 0f && handling.TurnSpeedLossFactor > 0f)
            {
                float lossFraction = Mathf.Clamp01(handling.TurnSpeedLossFactor * turnIntensity * simDt);
                nextSpeedKnots *= 1f - lossFraction;
            }

            if (targetSpeedKnots <= 0f && nextSpeedKnots <= 0.001f)
            {
                nextSpeedKnots = 0f;
            }

            Vector2 headingVector = MovementMath.HeadingDegreesToVector(nextHeading);
            float targetWorldSpeed = MovementMath.KnotsToWorldUnitsPerSecond(nextSpeedKnots, profile.metersPerWorldUnit);
            Vector2 desiredVelocity = headingVector * targetWorldSpeed;

            float velocityAlpha = MovementMath.ExpSmoothing(simDt, handling.VelocityAlignmentTimeSeconds);
            Vector2 nextVelocity = Vector2.Lerp(state.VelocityWorld, desiredVelocity, velocityAlpha);

            nextVelocity = ClampDrift(nextVelocity, headingVector, nextHeading, handling.MaxDriftAngleDegrees);

            if (nextSpeedKnots <= 0.001f && nextVelocity.sqrMagnitude <= 0.0001f)
            {
                nextVelocity = Vector2.zero;
            }

            Vector2 nextPosition = state.Position + nextVelocity * simDt;

            return new MovementState
            {
                Position = nextPosition,
                VelocityWorld = nextVelocity,
                HeadingDegrees = nextHeading,
                SpeedKnots = nextSpeedKnots,
                YawRateDegreesPerSecond = nextYawRate,
                Rudder = nextRudder
            };
        }

        private static float GetCommandTargetSpeedKnots(MovementCommand command, MovementProfileDefinition profile)
        {
            if (command.Intent == MovementIntent.Stop || command.Intent == MovementIntent.Hold)
            {
                return 0f;
            }

            return ShipKinematicUtility.GetTargetSpeedKnots(command.SpeedMode, profile);
        }

        private static float GetNormalBrakingSafeSpeedKnots(MovementCommand command, MovementProfileDefinition profile)
        {
            if (command.Intent != MovementIntent.Move)
            {
                return 0f;
            }
            float decelWorld = 
                MovementMath.KnotsPerSecondToWorldUnitsPerSecondSquared(
                profile.decelerationKnotsPerSecond, 
                profile.metersPerWorldUnit);

            if (decelWorld < 0.00001f)
            {
                return 0f;
            }

            float useableDistance = Mathf.Max(0f, command.RemainingDistanceWorld - profile.stoppingDistance);
            float safeSpeedWorld = Mathf.Sqrt(2f * decelWorld * useableDistance);
            return MovementMath.WorldUnitsPerSecondToKnots(safeSpeedWorld, profile.metersPerWorldUnit);
        }
        
        private static bool ShouldUseMaxDeceleration(
            MovementState state,
            MovementCommand command,
            MovementProfileDefinition profile,
            float safeSpeedKnots,
            float simDt)
        {
            if (command.Intent == MovementIntent.Stop)
            {
                return true;
            }

            if (command.Intent == MovementIntent.Hold)
            {
                return command.RouteChanged && state.SpeedKnots > 0.001f;
            }

            float toleraceKnots = Mathf.Max(0.05f, profile.decelerationKnotsPerSecond * simDt * 1.5f);
            return state.SpeedKnots > safeSpeedKnots + toleraceKnots;
        }

        private static Vector2 ClampDrift(
            Vector2 velocity,
            Vector2 headingVector,
            float headingDegrees,
            float maxDriftAngleDegrees)
        { 
            if (velocity.sqrMagnitude < 0.0001f || maxDriftAngleDegrees <= 0f)
            {
                return velocity;
            }

            float speed = velocity.magnitude;
            float velocityHeading = MovementMath.DirectionToHeadingDegrees(velocity);
            float drift = Mathf.DeltaAngle(headingDegrees, velocityHeading);
            float clampedDrift = Mathf.Clamp(drift, -maxDriftAngleDegrees, maxDriftAngleDegrees);
            
            if (Mathf.Approximately(drift, clampedDrift))
            {
                return velocity;
            }

            return MovementMath.HeadingDegreesToVector(headingDegrees + clampedDrift) * speed;
        }
    } 
}