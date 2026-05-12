using OA.Simulation.Movement;
using UnityEngine;

namespace OA.Simulation.Orders
{
    public sealed class Order
    {
        public OrderType Type { get; }
        public Vector2 TargetPosition { get; }
        public MovementSpeedMode SpeedMode { get; }
        public bool AppendToQueue { get; }

        private Order(OrderType type, Vector2 targetPosition, MovementSpeedMode speedMode, bool appendToQueue)
        {
            Type = type;
            TargetPosition = targetPosition;
            SpeedMode = speedMode; 
            AppendToQueue = appendToQueue;
        }

        public static Order CreateMove(Vector2 targetPosition, bool appendToQueue = false)
        {
            return CreateMove(targetPosition, MovementSpeedMode.Cruise, appendToQueue);
        }

    
        public static Order  CreateMove (
            Vector2 targetPosition,
            MovementSpeedMode speedMode,
            bool appendToQueue = false)
        {
            return new Order(OrderType.Move, targetPosition, speedMode, appendToQueue);
        }

        public static Order CreateStop()
        {
            return new Order(OrderType.Stop, Vector2.zero, MovementSpeedMode.Cruise, false);
        }

        public override string ToString()
        {
            return $"Order(Type = {Type}, Target = {TargetPosition}, SpeedMode = {SpeedMode}, Append = {AppendToQueue})";
        }
    }
}
