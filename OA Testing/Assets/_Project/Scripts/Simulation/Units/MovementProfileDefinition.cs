using UnityEngine;

namespace OA.Simulation.Units
{
    [CreateAssetMenu(
        fileName = "MovementProfile",
        menuName = "OA/Units/Movement Profile")]
        
    public sealed class MovementProfileDefinition : ScriptableObject
    {
        [Header("Speed")]
        [Min(0f)] public float maxSpeed = 10f;
        [Min(0f)] public float acceleration = 5f;
        [Min(0f)] public float deceleration = 10f;

        [Header("Turning")]
        [Min(0f)] public float turnRateDegreesPerSecond = 180f;

        [Header("Arrival")]
        [Min(0f)] public float stoppingDistance = 0.1f;
    }

}