using UnityEngine;

namespace OA.Simulation.Movement
{
    public static class MovementMath
    {
        public const float MetersPerSecondPerKnot = 0.514444f;

        public static float KnotsToMetersPerSecond(float knots)
        {
            return Mathf.Max(0f, knots) * MetersPerSecondPerKnot;
        }

        public static float KnotsToWorldUnitsPerSecond(float knots, float metersPerWorldUnit)
        {
            return KnotsToMetersPerSecond(knots) / Mathf.Max(0.001f, metersPerWorldUnit);
        }

        public static float WorldUnitsPerSecondToKnots(float worldUnitsPerSecond, float meterPerWorldUnit)
        {
            float metersPerSecond = Mathf.Max(0f, worldUnitsPerSecond) * Mathf.Max(0.001f, meterPerWorldUnit);
            return metersPerSecond / MetersPerSecondPerKnot;
        }

        public static float KnotsPerSecondToWorldUnitsPerSecondSquared(float knotsPerSecond, float metersPerWorldUnit)
        {
            return Mathf.Max(0f, knotsPerSecond) * MetersPerSecondPerKnot / Mathf.Max(0.001f, metersPerWorldUnit);
        }

        public static Vector2 HeadingDegreesToVector(float headingDegrees)
        {
            float radians = headingDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        public static float DirectionToHeadingDegrees(Vector2 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
            {
                return 0f; // Default heading when direction is near zero to avoid NaN from Atan2
            }

            return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        }

        public static float ExpSmoothing(float deltaTime, float timeSeconds)
        {
            if (timeSeconds <= 0.00001f)
            {
                return 1f; // Immediate change when time is zero or negative
            }
            return 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / timeSeconds);
        }
    }
}