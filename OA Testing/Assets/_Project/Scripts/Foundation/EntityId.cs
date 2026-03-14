using UnityEngine;
using System;

namespace OA.Foundation
{

    [Serializable]
    public readonly struct EntityId : IEquatable<EntityId>
    {
        private static int s_nextValue = 1;

        public int Value { get; }

        private EntityId(int value)
        {
            Value = value;
        }

        public static EntityId Create()
        {
            return new EntityId(s_nextValue++);
        }

        public bool Equals(EntityId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is EntityId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return $"EntityId({Value})";
        }

        public static bool operator == (EntityId left, EntityId right) => left.Equals(right);
        public static bool operator != (EntityId left, EntityId right) => !left.Equals(right);
    }

}
