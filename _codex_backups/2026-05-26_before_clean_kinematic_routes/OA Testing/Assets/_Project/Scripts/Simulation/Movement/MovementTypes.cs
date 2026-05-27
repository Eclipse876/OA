using UnityEngine;

namespace OA.Simulation.Movement
{
    public enum MovementSpeedMode
    {
        Cruise = 0,
        Flank = 1
    }

    public enum MovementIntent
    {
        Hold = 0,
        Move = 1,
        Stop = 2
    }

    public struct MovementState
    {
        public Vector2 Position;
        public Vector2 VelocityWorld;
        public float HeadingDegrees;
        public float SpeedKnots;
        public float YawRateDegreesPerSecond;
        public float Rudder;

        public static MovementState Create(Vector2 position, float headingDegrees)
        {
            return new MovementState
            {
                Position = position,
                VelocityWorld = Vector2.zero,
                HeadingDegrees = headingDegrees,
                SpeedKnots = 0f,
                YawRateDegreesPerSecond = 0f,
                Rudder = 0f
            };
        }
    }

    public struct MovementCommand
    {
        public MovementIntent Intent;
        public Vector2 SteeringTarget;
        public float RemainingDistanceWorld;
        public MovementSpeedMode SpeedMode;
        public float SpeedLimitKnots; // Optional speed limit for the command, used for speed-restricted maneuvers like tight turns or emergency stops.
                                      // If 0 or less, no additional speed limit is applied beyond the normal cruise/flank speeds.
        public bool RouteChanged;

        public static MovementCommand Hold(Vector2 position, bool routeChanged = false)
        {
            return new MovementCommand
            {
                Intent = MovementIntent.Hold,
                SteeringTarget = position,
                RemainingDistanceWorld = 0f,
                SpeedMode = MovementSpeedMode.Cruise,
                SpeedLimitKnots = 0f,
                RouteChanged = routeChanged
            };
        }

        public static MovementCommand Move(
            Vector2 steeringTarget,
            float remainingDistanceWorld,
            MovementSpeedMode speedMode,
            float speedLimitKnots,
            bool routeChanged = false)
        {
            return new MovementCommand
            {
                Intent = MovementIntent.Move,
                SteeringTarget = steeringTarget,
                RemainingDistanceWorld = Mathf.Max(0f, remainingDistanceWorld),
                SpeedMode = speedMode,
                SpeedLimitKnots = Mathf.Max(0f, speedLimitKnots),
                RouteChanged = routeChanged
            };
        }

        public static MovementCommand Stop(Vector2 position)
        {
            return new MovementCommand
            {
                Intent = MovementIntent.Stop,
                SteeringTarget = position,
                RemainingDistanceWorld = 0f,
                SpeedMode = MovementSpeedMode.Cruise,
                SpeedLimitKnots = 0f,
                RouteChanged = true
            };
        }
    }
}