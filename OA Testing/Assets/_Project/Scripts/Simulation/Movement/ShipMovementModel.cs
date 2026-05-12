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

            float simDt = deltaTime * Mathf.Max(0.001f, profile.simulationSecondsPerRealSecond);
            float targetSpeedKnots = GetTargetSpeedKnots(command, profile);
            float safeSpeedKnots = GetNormalBrakingSafeSpeedKnots(command, profile);
            bool useMaxDeceleration = ShouldUseMaxDeceleration(state, command, profile, safeSpeedKnots, simDt);

            if (command.Intent == MovementIntent.Move)
            {
                targetSpeedKnots = Mathf.Min(targetSpeedKnots, safeSpeedKnots);
            }

            float speedRate = state.SpeedKnots < targetSpeedKnots
                ? profile.accelerationKnotsPerSecond
                : useMaxDeceleration
                    ? profile.maxDecelerationKnotsPerSecond
                    : profile.decelerationKnotsPerSecond;

            float nextSpeedKnots = Mathf.MoveTowards(
                state.SpeedKnots,
                targetSpeedKnots,
                Mathf.Max(0f, speedRate) * simDt);

            float nextHeading = state.HeadingDegrees;
            float turnIntensity = 0f;
            float yawRate = 0f;

            if (command.Intent == MovementIntent.Move)
            {
                Vector2 toTarget = command.SteeringTarget - state.Position;
                if (toTarget.sqrMagnitude > 0.000001f)
                {
                    float desiredHeading = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
                    float maxTurnRate = CalculateTurnRateDegreesPerSecond(nextSpeedKnots, profile);
                    float headingDelta = Mathf.DeltaAngle(state.HeadingDegrees, desiredHeading);
                    float maxDelta = maxTurnRate * simDt;
                    float appliedDelta = Mathf.Clamp(headingDelta, -maxDelta, maxDelta);

                    nextHeading = state.HeadingDegrees + appliedDelta;
                    yawRate = simDt > 0.0001f ? appliedDelta / simDt : 0f;
                    turnIntensity = maxDelta > 0.0001f ? Mathf.Clamp01(Mathf.Abs(appliedDelta) / maxDelta) : 0f;

                }
            }

            if (turnIntensity > 0f && profile.turnSpeedLossFactor > 0f)
            {
                float lossFraction = Mathf.Clamp01(profile.turnSpeedLossFactor * turnIntensity * simDt);
                nextSpeedKnots *= 1f - lossFraction;
            }

            if (targetSpeedKnots <= 0f && nextSpeedKnots <= 0.001f)
            {
                nextSpeedKnots = 0f;
            }

            Vector2 forward = MovementMath.HeadingDegreesToVector(nextHeading);
            float worldSpeed = MovementMath.KnotsToWorldUnitsPerSecond(nextSpeedKnots, profile.metersPerWorldUnit);
            Vector2 nextPosition = state.Position + forward * (worldSpeed * simDt);

            return new MovementState
            {
                Position = nextPosition,
                HeadingDegrees = nextHeading,
                SpeedKnots = nextSpeedKnots,
                YawRateDegreesPerSecond = yawRate
            };
        }

        private static float GetTargetSpeedKnots(MovementCommand command, MovementProfileDefinition profile)
        {
            if (command.Intent == MovementIntent.Stop || command.Intent == MovementIntent.Hold)
            {
                return 0f;
            }

            if (command.SpeedMode == MovementSpeedMode.Flank)
            {
                return Mathf.Max(profile.cruiseSpeedKnots, profile.flankSpeedKnots);
            }

            return Mathf.Max(0f, profile.cruiseSpeedKnots);
        }

        private static float GetNormalBrakingSafeSpeedKnots(MovementCommand command, MovementProfileDefinition profile)
        {
            if (command.Intent != MovementIntent.Move)
            {
                return 0f;
            }

            float decelWorld = MovementMath.KnotsPerSecondToWorldUnitsPerSecondSquared(
                profile.decelerationKnotsPerSecond,
                profile.metersPerWorldUnit);

            if(decelWorld < 0.00001f)
            {
                return 0f;
            }

            float usableDistance = Mathf.Max(0f, command.RemainingDistanceWorld - profile.stoppingDistance);
            float safeWorldSpeed = Mathf.Sqrt(2f * decelWorld * usableDistance);
            return MovementMath.WorldUnitsPerSecondToKnots(safeWorldSpeed, profile.metersPerWorldUnit);
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

            float toleranceKnots = Mathf.Max(0.05f, profile.decelerationKnotsPerSecond * simDt * 1.5f);
            return state.SpeedKnots > safeSpeedKnots + toleranceKnots;
        }

        private static float CalculateTurnRateDegreesPerSecond(float speedKnots, MovementProfileDefinition profile)
        {
            if (speedKnots <= 0.001f)
            {
                return 0f;
            }

            float flank = Mathf.Max(0.001f, profile.flankSpeedKnots);
            float cruise = Mathf.Clamp(profile.cruiseSpeedKnots, 0f, flank);
            float speed01 = flank > cruise
                ? Mathf.InverseLerp(cruise, flank, speedKnots)
                : Mathf.Clamp01(speedKnots / flank);

            float tacticalDiameterLengths = Mathf.Lerp(
                Mathf.Max(0.1f, profile.tacticalDiameterAtCruiseLengths),
                Mathf.Max(0.1f, profile.tacticalDiameterAtFlankLengths),
                speed01);

            float turnRadiusMeters = Mathf.Max(1f, profile.lengthMeters) * tacticalDiameterLengths * 0.5f;
            float turnRadiusWorld = turnRadiusMeters / Mathf.Max(0.001f, profile.metersPerWorldUnit);
            float speedWorld = MovementMath.KnotsToWorldUnitsPerSecond(speedKnots, profile.metersPerWorldUnit);

            return Mathf.Rad2Deg * (speedWorld / Mathf.Max(0.001f, turnRadiusWorld));
        }
    }
}
