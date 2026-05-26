<<<<<<< Updated upstream
=======
// MovementProfileDefinition.cs:
// Movement knobs for units, including the safety bubble pathfinding uses to
// Keep ships from plotting routes that scrape right along blocked hexes.
using OA.Simulation.Movement;
using OA.Simulation.Navigation;
>>>>>>> Stashed changes
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

<<<<<<< Updated upstream
=======
//Ship Dimensions-------------------------------------
        [Header("Ship Dimensions")] // These are the "real" dimensions of a ship, that metersPerWorldUnit will convert to the Unity scale. 
            
            [Min(1f)] public float lengthMeters = 150f; //Length of the ship, used for pathfinding and turning radius. Longer ships get longer turning radius. 
            [Min(0f)] public float displacementTons = 4000f; // Displacement in tons. Not currently being used, I was thinking of using it for inertia, but for now its just for stat cards.
            public ShipDraftClass draftClass = ShipDraftClass.Shallow; // Depth classification of the ship, used for determining navigable waters.
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

//Agility-------------------------------------
        [Header("Agility")]
            public ShipAgilityClass agility = ShipAgilityClass.Normal; // Agility class of the ship

        //[Min(0.1f)] public float tacticalDiameterAtCruiseLengths = 4f; // The diameter of the turning circle at cruise speed. Measured in ship lengths (4f = 4 ship lengths). Higher values = wider turns.
        //[Min(0.1f)] public float tacticalDiameterAtFlankLengths = 5f; // The diameter of the turning circle at flank speed. Measured in ship lengths (5f = 5 ship lengths). Higher values = wider turns.
        //[Range(0f, 0.5f)] public float turnSpeedLossFactor = 0.015f; // How much the ship's turn rate is reduced at higher speeds. Higher values = more reduction in turn rate at higher speeds.

//Debug Handling Overrides-------------------------------------
        [Header("Debug Handling Overrides")]
            public bool useDebugHandlingOverrides = false;
            [Min(0.01f)] public float debugRudderShiftTimeSeconds = 5f;
            [Min(0.01f)] public float debugYawAccelerationDegreesPerSecondSquared = 1f;
            [Min(0.01f)] public float debugYawDampingPerSecond = 1f;
            [Min(0.01f)] public float debugVelocityAlignmentTimeSeconds = 8f;
            [Min(0f)]    public float debugMaxDriftAngleDegrees = 12f;
            [Min(0.1f)]  public float debugTacticalDiameterAtCruiseLengths = 4f;
            [Min(0.1f)]  public float debugTacticalDiameterAtFlankLengths = 5f;
            [Range(0f, 0.5f)] public float debugTurnSpeedLossFactor = 0.015f;
            [Range(0.1f, 1f)] public float debugTightTurnSpeedMultiplier = 0.65f;



        //Pathing Safety-------------------------------------
        [Header("Pathing Safety")]
            [Min(0f)] public float safetyRadius = 1f; // The radius around the ship that pathfinding considers when plotting a course. This is used to keep ships from plotting courses that scrape right along blocked hexes.
                                                      // Higher values = safer paths, but less efficient routes.

//Arrival-------------------------------------
>>>>>>> Stashed changes
        [Header("Arrival")]
        [Min(0f)] public float stoppingDistance = 0.1f;
    }

}