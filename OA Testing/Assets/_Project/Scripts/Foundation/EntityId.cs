using UnityEngine;
using System;
using System.Threading;

namespace OA.Foundation
{

    [Serializable]
    public readonly struct EntityId : IEquatable<EntityId>
    {
        private static long s_nextValue = 0;

        public long Value { get; }

        private EntityId(long value)
        {
            Value = value;
        }

        public static EntityId Create()
        {
            return new EntityId(Interlocked.Increment(ref s_nextValue));
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
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return $"EntityId({Value})";
        }

        public static bool operator == (EntityId left, EntityId right) => left.Equals(right);
        public static bool operator != (EntityId left, EntityId right) => !left.Equals(right);
    }

}
