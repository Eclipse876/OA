// MovementProfileDefinition.cs:
// Movement knobs for units, including the safety bubble pathfinding uses to
// keep ships from plotting routes that scrape right along blocked hexes.
using UnityEngine;

namespace OA.Simulation.Units
{
    // ScriptableObject profile for speed, turning, arrival, and path-safety values.
    [CreateAssetMenu(
        fileName = "MovementProfile",
        menuName = "OA/Units/Movement Profile")]
        
    public sealed class MovementProfileDefinition : ScriptableObject
    {

        [Header("Prototype Scale")]
        [Min(0.001f)] public float metersPerWorldUnit = 100f;
        [Min(0.001f)] public float simulationSecondsPerRealSecond = 60f;

        [Header("Ship Dimensions")]
        [Min(1f)] public float lengthMeters = 150f;
        [Min(0f)] public float displacementTons = 4000f;
        [Min(0f)] public float draftMeters = 5f;

        [Header("Ship Speeds")]
        [Min(0f)] public float cruiseSpeedKnots = 10f;
        [Min(0f)] public float flankSpeedKnots  = 30f;
        [Min(0f)] public float accelerationKnotsPerSecond = 0.08f;
        [Min(0f)] public float decelerationKnotsPerSecond = 0.12f;
        [Min(0f)] public float maxDecelerationKnotsPerSecond = 0.35f;

        [Header("Ship Turning")]
        [Min(0.1f)] public float tacticalDiameterAtCruiseLengths = 4f;
        [Min(0.1f)] public float tacticalDiameterAtFlankLengths = 5f;
        [Range(0f, 0.5f)] public float turnSpeedLossFactor = 0.015f;

        [Header("Pathing Safety")]
        [Min(0f)] public float safetyRadius = 1f;

        [Header("Arrival")]
        [Min(0f)] public float stoppingDistance = 0.1f;

        [Header("Legacy Movement Stuffs")]
        [Min(0f)] public float maxSpeed = 10f;
        [Min(0f)] public float acceleration = 5f;
        [Min(0f)] public float deceleration = 10f;
        [Min(0f)] public float turnRateDegreesPerSecond = 180f;
        [Min(0f)] public float turningRadius = 2.5f;
    }
}