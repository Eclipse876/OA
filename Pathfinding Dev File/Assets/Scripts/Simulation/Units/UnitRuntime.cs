using OA.Foundation;
using UnityEngine;

namespace OA.Simulation.Units
{
    public sealed class UnitRuntime
    {
        public EntityId Id { get; }
        public UnitArchetypeDefinition Archetype { get; }

        public Vector2 Position { get; private set; }
        public float HeadingDegrees { get; private set; }
        public float CurrentSpeed { get; private set; }

        public UnitRuntime(UnitArchetypeDefinition archetype, Vector2 startPosition)
        {
            Id = EntityId.Create();
            Archetype = archetype;
            Position = startPosition;
            HeadingDegrees = 0f;
            CurrentSpeed = 0f;
        }

        public void SetPosition(Vector2 position)
        {
            Position = position;
        }

        public void SetHeadingDegrees(float headingDegrees)
        {
            HeadingDegrees = headingDegrees;
        }

        public void SetCurrentSpeed(float speed)
        {
            CurrentSpeed = Mathf.Max(0f, speed);
        }
    }
}
