// ShipMovementModel.cs:
// Physical movement for a ship after navigation has supplied a steering target.
// Speed is controlled in knots, while heading changes, rudder reversal, and
// sideways slip create the slow heavy motion the player actually sees.
using OA.Simulation.Units;
using UnityEngine;

namespace OA.Simulation.Movement
{
    public sealed class ShipMovementModel
    {
        // Advances one ship for one frame. The route planner uses this exact method
        // during prediction so rendered routes and live movement share one authority.
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
            float simDt = deltaTime *
                          Mathf.Max(0.001f, profile.simulationSecondsPerRealSecond);

            // Velocity is the real motion of the unit, so it must count when braking.
            // This matters after a turn when the bow and travel direction are not aligned.
            float physicalSpeedKnots = MovementMath.WorldUnitsPerSecondToKnots(
                state.VelocityWorld.magnitude,
                profile.metersPerWorldUnit);

            float currentSpeedKnots = Mathf.Max(
                Mathf.Max(0f, state.SpeedKnots),
                physicalSpeedKnots);

            float targetSpeedKnots = GetCommandTargetSpeedKnots(command, profile);

            if (command.Intent == MovementIntent.Move)
            {
                float terrainLimitedSpeed = GetCommandTargetSpeedKnots(
                    command,
                    profile) * Mathf.Clamp(
                        command.TerrainSpeedMultiplier,
                        0.001f,
                        1f);

                targetSpeedKnots = Mathf.Min(
                    targetSpeedKnots,
                    terrainLimitedSpeed);

                float arrivalSafeSpeed = GetNormalBrakingSafeSpeedKnots(
                    command,
                    profile);

                targetSpeedKnots = Mathf.Min(targetSpeedKnots, arrivalSafeSpeed);

                if (command.SpeedLimitKnots > 0.0001f)
                {
                    targetSpeedKnots = Mathf.Min(
                        targetSpeedKnots,
                        command.SpeedLimitKnots);
                }
            }

            bool useMaxDeceleration = ShouldUseMaxDeceleration(
                currentSpeedKnots,
                targetSpeedKnots,
                command,
                profile,
                simDt);

            float speedRate = currentSpeedKnots < targetSpeedKnots
                ? profile.accelerationKnotsPerSecond
                : useMaxDeceleration
                    ? profile.maxDecelerationKnotsPerSecond
                    : profile.decelerationKnotsPerSecond;

            float nextSpeedKnots = Mathf.MoveTowards(
                currentSpeedKnots,
                targetSpeedKnots,
                Mathf.Max(0f, speedRate) * simDt);

            bool hasSteeringTarget = false;
            float desiredHeading = state.HeadingDegrees;

            if (command.Intent == MovementIntent.Move)
            {
                Vector2 toTarget = command.SteeringTarget - state.Position;

                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    desiredHeading = MovementMath.DirectionToHeadingDegrees(toTarget);
                    hasSteeringTarget = true;
                }
            }

            // Rudder does not teleport from port to starboard. Reversing a turn takes time.
            float desiredRudder = 0f;

            if (hasSteeringTarget)
            {
                float headingDelta = Mathf.DeltaAngle(
                    state.HeadingDegrees,
                    desiredHeading);

                desiredRudder = Mathf.Clamp(headingDelta / 45f, -1f, 1f);
            }

            float rudderRate = 2f /
                               Mathf.Max(0.001f, handling.RudderShiftTimeSeconds);

            float nextRudder = Mathf.MoveTowards(
                state.Rudder,
                desiredRudder,
                rudderRate * simDt);

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
                float dampingAlpha =
                    1f - Mathf.Exp(-handling.YawDampingPerSecond * simDt);

                nextYawRate = Mathf.Lerp(nextYawRate, 0f, dampingAlpha);
            }

            float nextHeading = state.HeadingDegrees + nextYawRate * simDt;

            float turnIntensity = maxTurnRate > 0.001f
                ? Mathf.Clamp01(Mathf.Abs(nextYawRate) / maxTurnRate)
                : 0f;

            // Turning eats speed. This is intentionally mild; turn speed planning
            // still provides the clear tactical Slow/Cruise/Flank behavior.
            if (turnIntensity > 0f && handling.TurnSpeedLossFactor > 0f)
            {
                float lossFraction = Mathf.Clamp01(
                    handling.TurnSpeedLossFactor * turnIntensity * simDt);

                nextSpeedKnots *= 1f - lossFraction;
            }

            if (nextSpeedKnots <= 0.001f)
            {
                nextSpeedKnots = 0f;
            }

            Vector2 headingVector =
                MovementMath.HeadingDegreesToVector(nextHeading);

            Vector2 currentDirection = state.VelocityWorld.sqrMagnitude > 0.000001f
                ? state.VelocityWorld.normalized
                : headingVector;

            // The travel direction eases toward the bow rather than snapping to it.
            // Larger alignment times are the visible sideways slip of larger hulls.
            float velocityAlpha = MovementMath.ExpSmoothing(
                simDt,
                handling.VelocityAlignmentTimeSeconds);

            Vector2 nextDirection = Vector2.Lerp(
                currentDirection,
                headingVector,
                velocityAlpha);

            if (nextDirection.sqrMagnitude <= 0.000001f)
            {
                nextDirection = headingVector;
            }
            else
            {
                nextDirection.Normalize();
            }

            float targetWorldSpeed = MovementMath.KnotsToWorldUnitsPerSecond(
                nextSpeedKnots,
                profile.metersPerWorldUnit);

            Vector2 nextVelocity = nextDirection * targetWorldSpeed;

            nextVelocity = ClampDrift(
                nextVelocity,
                nextHeading,
                handling.MaxDriftAngleDegrees);

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

        private static float GetCommandTargetSpeedKnots(
            MovementCommand command,
            MovementProfileDefinition profile)
        {
            if (command.Intent == MovementIntent.Stop ||
                command.Intent == MovementIntent.Hold)
            {
                return 0f;
            }

            return ShipKinematicUtility.GetTargetSpeedKnots(
                command.SpeedMode,
                profile);
        }

        // Normal arrival braking computes how fast the ship may safely be moving
        // if it should arrive gently at the final waypoint.
        private static float GetNormalBrakingSafeSpeedKnots(
            MovementCommand command,
            MovementProfileDefinition profile)
        {
            if (command.Intent != MovementIntent.Move)
            {
                return 0f;
            }

            float decelerationWorld =
                MovementMath.KnotsPerSecondToWorldUnitsPerSecondSquared(
                    profile.decelerationKnotsPerSecond,
                    profile.metersPerWorldUnit);

            if (decelerationWorld <= 0.00001f)
            {
                return 0f;
            }

            float usableDistance = Mathf.Max(
                0f,
                command.RemainingDistanceWorld - profile.stoppingDistance);

            float safeSpeedWorld = Mathf.Sqrt(
                2f * decelerationWorld * usableDistance);

            return MovementMath.WorldUnitsPerSecondToKnots(
                safeSpeedWorld,
                profile.metersPerWorldUnit);
        }

        // Maximum deceleration is not a movement mode. It is only permitted for
        // an explicit stop, or for the first response to a changed route whose
        // newly required speed cannot be reached using normal deceleration.
        private static bool ShouldUseMaxDeceleration(
            float currentSpeedKnots,
            float targetSpeedKnots,
            MovementCommand command,
            MovementProfileDefinition profile,
            float simDt)
        {
            if (command.Intent == MovementIntent.Stop)
            {
                return true;
            }

            if (!command.RouteChanged || currentSpeedKnots <= targetSpeedKnots)
            {
                return false;
            }

            float normalNextSpeed = Mathf.MoveTowards(
                currentSpeedKnots,
                targetSpeedKnots,
                Mathf.Max(0f, profile.decelerationKnotsPerSecond) * simDt);

            float tolerance = Mathf.Max(
                0.05f,
                profile.decelerationKnotsPerSecond * simDt * 0.25f);

            return normalNextSpeed > targetSpeedKnots + tolerance;
        }

        // Caps visual sideslip so velocity can lag behind the bow without a ship
        // drifting broadside forever.
        private static Vector2 ClampDrift(
            Vector2 velocity,
            float headingDegrees,
            float maxDriftAngleDegrees)
        {
            if (velocity.sqrMagnitude < 0.0001f ||
                maxDriftAngleDegrees <= 0f)
            {
                return velocity;
            }

            float speed = velocity.magnitude;
            float velocityHeading =
                MovementMath.DirectionToHeadingDegrees(velocity);

            float drift = Mathf.DeltaAngle(
                headingDegrees,
                velocityHeading);

            float clampedDrift = Mathf.Clamp(
                drift,
                -maxDriftAngleDegrees,
                maxDriftAngleDegrees);

            if (Mathf.Approximately(drift, clampedDrift))
            {
                return velocity;
            }

            return MovementMath.HeadingDegreesToVector(
                headingDegrees + clampedDrift) * speed;
        }
    }
}
