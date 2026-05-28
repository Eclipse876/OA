using UnityEngine;

namespace OA.Simulation.Orders
{
    public sealed class Order
    {
        public OrderType Type { get; }
        public Vector2 TargetPosition { get; }
        public bool AppendToQueue { get; }

        private Order(OrderType type, Vector2 targetPosition, bool appendToQueue)
        {
            Type = type;
            TargetPosition = targetPosition;
            AppendToQueue = appendToQueue;
        }

        public static Order CreateMove(Vector2 targetPosition, bool appendToQueue = false)
        {
            return new (OrderType.Move, targetPosition, appendToQueue);
        }

        public override string ToString()
        {
            return $"Order(Type = {Type}, Target = {TargetPosition}, Append = {AppendToQueue})";
        }
    }
}
