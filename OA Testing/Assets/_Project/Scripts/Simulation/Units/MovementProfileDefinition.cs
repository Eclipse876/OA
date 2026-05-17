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
            // Converts the real ship values to the Unity scale.
            [Min(0.001f)] public float metersPerWorldUnit = 100f;

            // Time compression. Higher values make the simulation run faster, lower makes it run slower.
            [Min(0.001f)] public float simulationSecondsPerRealSecond = 60f;

        [Header("Ship Dimensions")] // These are the "real" dimensions of a ship, that metersPerWorldUnit will convert to the Unity scale. 
            //Length of the ship, used for pathfinding and turning radius. Longer ships get longer turning radius. 
            [Min(1f)] public float lengthMeters = 150f;
        
            // Displacement in tons. Not currently being used, I was thinking of using it for inertia, but for now its just for stat cards.
            [Min(0f)] public float displacementTons = 4000f;
        
            // Depth classification of the ship, used for determining navigable waters.
            [Min(0)]  public int   depthClass  = 0;
            
            // Depth of the ship in meters, not used for navigation, just for stat cards.
            [Min(0)]  public int   depthMeters = 5;

        [Header("Ship Crew Complement")] // Potential stats for crew requirements to represent loss of crew during combat and alter ship preformance as crew is lost.
                                         // Not currently being used.

            //Minimum crew needed. Maybe where debuffs start to kick in or where the ship is considered "disabled"?
            [Min(0)] public int crewRequired = 50;

             // Total crew complement. Used for stat cards and the crew loss mechanic.
            [Min(0)] public int crewComplement = 100;

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