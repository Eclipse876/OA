using UnityEngine;

namespace OA.Simulation.Units
{
    [CreateAssetMenu(fileName = "MovementProfile", menuName = "OA/Units/Movement Profile")]
    public sealed class MovementProfileDefinition : ScriptableObject
    {
        [Header("Speed")]
        [Min(0f)] public float maxSpeed = 8f;
        [Min(0f)] public float acceleration = 7f;
        [Min(0f)] public float deceleration = 10f;

        [Header("Turning")]
        [Min(0f)] public float turnRateDegreesPerSecond = 120f;
        [Min(0f)] public float turningRadius = 2.5f;

        [Header("Pathing Safety")]
        [Min(0f)] public float safetyRadius = 1f;

        [Header("Arrival")]
        [Min(0.01f)] public float stoppingDistance = 0.2f;
    }
}
