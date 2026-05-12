using OA.Simulation.Movement;
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
        public float HeadingDegrees;
        public float SpeedKnots;
        public float YawRateDegreesPerSecond;

        public static MovementState Create(Vector2 position, float headingDegrees)
        {
            return new MovementState
            {
                Position = position,
                HeadingDegrees = headingDegrees,
                SpeedKnots = 0f,
                YawRateDegreesPerSecond = 0f
            };
        }
    }


    public struct MovementCommand
    {
        public MovementIntent Intent;
        public Vector2 SteeringTarget;
        public float RemainingDistanceWorld;
        public MovementSpeedMode SpeedMode;
        public bool RouteChanged;

        public static MovementCommand Hold(Vector2 position, bool routeChanged = false)
        {
            return new MovementCommand
            {
                Intent = MovementIntent.Hold,
                SteeringTarget = position,
                RemainingDistanceWorld = 0f,
                SpeedMode = MovementSpeedMode.Cruise,
                RouteChanged = routeChanged
            };
        }

        public static MovementCommand Move(
            Vector2 steeringTarget,
            float remainingDistanceWorld,
            MovementSpeedMode speedMode,
            bool routeChanged = false)
        {
            return new MovementCommand
            {
                Intent = MovementIntent.Move,
                SteeringTarget = steeringTarget,
                RemainingDistanceWorld = Mathf.Max(0f, remainingDistanceWorld),
                SpeedMode = speedMode,
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
                RouteChanged = true
            };
        }
    }
}
