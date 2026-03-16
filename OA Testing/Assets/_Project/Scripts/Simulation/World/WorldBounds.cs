using System;
using System.Numerics;
using UnityEngine;

namespace OA.Simulation.World
{
    [System.Serializable]
    public struct WorldBounds
    {
        public Vector2 Min;
        public Vector2 Max;

        public WorldBounds(Vector2 min, Vector2 max)
        {
            Min = min;
            Max = max;
        }

        public Vector2 Size => Max - Min;
        public Vector2 Center => (Min + Max) * 0.5f;

        public bool Contains(Vector2 point)
        {
            return point.x >= Min.x &&
                   point.x <= Max.x &&
                   point.y >= Min.y &&
                   point.y <= Max.y;
        }

        public Vector2 Clamp(Vector2 point)
        {
            return new Vector2(Mathf.Clamp(point.x, Min.x, Max.x), 
                               Mathf.Clamp(point.y, Min.y, Max.y));
        }

        public override string ToString()
        {
            return $"WorldBounds(Min={Min}, Max={Max})";
        }
    }
}