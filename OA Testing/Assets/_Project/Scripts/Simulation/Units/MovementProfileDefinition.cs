// MovementProfileDefinition.cs:
// Movement knobs for units, including the safety bubble pathfinding uses to
// Keep ships from plotting routes that scrape right along blocked hexes.
using UnityEngine;

namespace OA.Simulation.Units
{
    // ScriptableObject profile for speed, turning, arrival, and path-safety values.
    [CreateAssetMenu(
        fileName = "MovementProfile",
        menuName = "OA/Units/Movement Profile")]
        
    public sealed class MovementProfileDefinition : ScriptableObject
    {

//Prototype Scale-------------------------------------
        [Header("Prototype Scale")]
            
            [Min(0.001f)] public float metersPerWorldUnit = 100f; // Converts the real ship values to the Unity scale.
            [Min(0.001f)] public float simulationSecondsPerRealSecond = 60f; // Time compression. Higher values make the simulation run faster, lower makes it run slower.

//Ship Dimensions-------------------------------------
        [Header("Ship Dimensions")] // These are the "real" dimensions of a ship, that metersPerWorldUnit will convert to the Unity scale. 
            
            [Min(1f)] public float lengthMeters = 150f; //Length of the ship, used for pathfinding and turning radius. Longer ships get longer turning radius. 
            [Min(0f)] public float displacementTons = 4000f; // Displacement in tons. Not currently being used, I was thinking of using it for inertia, but for now its just for stat cards.
            [Min(0)]  public int   depthClass  = 0; // Depth classification of the ship, used for determining navigable waters.
            [Min(0)]  public int   depthMeters = 5; // Depth of the ship in meters, not used for navigation, just for stat cards.

//Crew Complement-------------------------------------
        [Header("Ship Crew Complement")] // Potential stats for crew requirements to represent loss of crew during combat and alter ship preformance as crew is lost.
                                         // Not currently being used.

            [Min(0)] public int crewRequired = 50; //Minimum crew needed. Maybe where debuffs start to kick in or where the ship is considered "disabled"?
            [Min(0)] public int crewComplement = 100; // Total crew complement. Used for stat cards and the crew loss mechanic.

//Ship Speeds-------------------------------------
        [Header("Ship Speeds")] // What it says on the tin. Movement speeds (normal and fast) and acceleration/deceleration rates.
                                // All in knots, which are converted to Unity units per second using the metersPerWorldUnit and simulationSecondsPerRealSecond values.

            [Min(0f)] public float cruiseSpeedKnots = 10f; // Cruise speed is the normal movement speed
            [Min(0f)] public float flankSpeedKnots  = 30f; // Flank speed is the "fast" movement speed
            [Min(0f)] public float accelerationKnotsPerSecond = 0.08f; // How quickly the ship accelerates. Higher values = faster acceleration.
            [Min(0f)] public float decelerationKnotsPerSecond = 0.12f; // How quickly the ship decelerates. Higher values = faster deceleration.
            [Min(0f)] public float maxDecelerationKnotsPerSecond = 0.35f; // The maximum deceleration the ship can achieve, used for emergency stops. Higher values = faster stops.

//Ship Turning-------------------------------------
        [Header("Ship Turning")]
            [Min(0.1f)] public float tacticalDiameterAtCruiseLengths = 4f; // The diameter of the turning circle at cruise speed. Measured in ship lengths (4f = 4 ship lengths). Higher values = wider turns.
            [Min(0.1f)] public float tacticalDiameterAtFlankLengths = 5f; // The diameter of the turning circle at flank speed. Measured in ship lengths (5f = 5 ship lengths). Higher values = wider turns.
            [Range(0f, 0.5f)] public float turnSpeedLossFactor = 0.015f; // How much the ship's turn rate is reduced at higher speeds. Higher values = more reduction in turn rate at higher speeds.

//Pathing Safety-------------------------------------
        [Header("Pathing Safety")]
            [Min(0f)] public float safetyRadius = 1f; // The radius around the ship that pathfinding considers when plotting a course. This is used to keep ships from plotting courses that scrape right along blocked hexes.
                                                      // Higher values = safer paths, but less efficient routes.

//Arrival-------------------------------------
        [Header("Arrival")]
            [Min(0f)] public float stoppingDistance = 0.1f; // The distance from the target at which the ship will consider itself to have "arrived" and stop moving.
                                                            // Higher values = more forgiving arrival, but less precise.

//Legacy Movement Stuffs-------------------------------------
        [Header("Legacy Movement Stuffs")] // The original movement settings I had. They are no longer being used, afaik, but I'm keeping them for now in case something is still relying on them for some reason.
                                           // They are all in Unity units per second, so they are already converted from knots using the metersPerWorldUnit and simulationSecondsPerRealSecond values.
            [Min(0f)] public float maxSpeed = 10f;
            [Min(0f)] public float acceleration = 5f;
            [Min(0f)] public float deceleration = 10f;
            [Min(0f)] public float turnRateDegreesPerSecond = 180f;
            [Min(0f)] public float turningRadius = 2.5f;

//-------------------------------------
    }
}