using EntityId = OA.Foundation.EntityId;
using OA.Simulation.Orders;
using UnityEngine;

namespace OA.Simulation.Units
{
    public sealed class UnitRuntime
    {
        public EntityId Id { get; }
        public UnitArchetypeDefinition Archetype { get; }

        public Vector2 Position          { get; private set; }
        public float HeadingDegrees      { get; private set; }
        public float CurrentSpeed => CurrentSpeedKnots;
        public float CurrentSpeedKnots   { get; private set; }

        public OrderQueue Orders { get; } = new OrderQueue();

        public UnitRuntime(UnitArchetypeDefinition archetype, Vector2 startPosition)
        {
            Id = EntityId.Create();
            Archetype = archetype;
            Position = startPosition;
            HeadingDegrees = 0f;
            CurrentSpeedKnots = 0f;
        }

        public void SetPosition(Vector2 newPosition)
        {
            Position = newPosition;
        }

        public void SetHeadingDegrees(float newHeadingDegrees)
        {
            HeadingDegrees = newHeadingDegrees;
        }

        public void SetCurrentSpeed(float newSpeedKnots)
        {
            CurrentSpeedKnots = Mathf.Max(0f, newSpeedKnots);
        }

        public void IssueOrder(Order order)
        {
            if (order == null)
                return;

            if (!order.AppendToQueue)
            {
                Orders.Clear();
            }

            Orders.Enqueue(order);
        }

        public override string ToString()
        {
            string archetypeName = Archetype != null ? Archetype.displayName : "NullArchetype";
            return $"UnitRuntime(Id = {Id}, Archetype = {archetypeName}, Position = {Position})";
        }
    }
}
