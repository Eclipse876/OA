// MovementProfileDefinition.cs:
// Movement knobs for units, including the safety bubble pathfinding uses to
// keep ships from plotting routes that scrape right along blocked hexes.
using OA.Simulation.Movement;
using OA.Simulation.Navigation;
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

            [Min(1f)] public float lengthMeters = 150f; // Length of the ship, used for pathfinding and turning radius. Longer ships get longer turning radius.
            [Min(0f)] public float displacementTons = 4000f; // Displacement in tons. Not currently used by movement; kept for stat cards and later systems.
            public ShipDraftClass draftClass = ShipDraftClass.Shallow; // Navigation category. Shallow ships can enter shallow water; deep ships require deep water.
            [Min(0)] public int depthMeters = 5; // Real draft shown on stat cards. Not currently used for navigation.

//Crew Complement-------------------------------------
        [Header("Ship Crew Complement")] // Potential stats for crew requirements to represent loss of crew during combat and alter ship performance as crew is lost.
                                         // Not currently being used.

            [Min(0)] public int crewRequired = 50; // Minimum crew needed. Maybe where debuffs start to kick in or where the ship is considered "disabled"?
            [Min(0)] public int crewComplement = 100; // Total crew complement. Used for stat cards and the crew loss mechanic.

//Ship Speeds-------------------------------------
        [Header("Ship Speeds")] // What it says on the tin. Movement speeds (normal and fast) and acceleration/deceleration rates.
                                // All in knots, which are converted to Unity units per second using the metersPerWorldUnit and simulationSecondsPerRealSecond values.

            [Min(0f)] public float cruiseSpeedKnots = 10f; // Cruise speed is the normal movement speed.
            [Min(0f)] public float flankSpeedKnots = 30f; // Flank speed is the "fast" movement speed.
            [Min(0f)] public float accelerationKnotsPerSecond = 0.08f; // How quickly the ship accelerates. Higher values = faster acceleration.
            [Min(0f)] public float decelerationKnotsPerSecond = 0.12f; // How quickly the ship normally decelerates for route planning and arrival.
            [Min(0f)] public float maxDecelerationKnotsPerSecond = 0.35f; // Hardest braking allowed: used for an explicit Stop command or unavoidable braking after a route change.

//Agility-------------------------------------
        [Header("Agility")] // Public handling category. It resolves into hidden rudder, yaw, slip, and turning-radius values.

            public ShipAgilityClass agility = ShipAgilityClass.Normal;

//Debug Handling Overrides-------------------------------------
        [Header("Debug Handling Overrides")] // Lets testing temporarily replace the preset values without creating a new agility class.

            public bool useDebugHandlingOverrides = false;
            [Min(0.01f)] public float debugRudderShiftTimeSeconds = 5f;
            [Min(0.01f)] public float debugYawAccelerationDegreesPerSecondSquared = 1f;
            [Min(0.01f)] public float debugYawDampingPerSecond = 1f;
            [Min(0.01f)] public float debugVelocityAlignmentTimeSeconds = 8f;
            [Min(0f)] public float debugMaxDriftAngleDegrees = 12f;
            [Min(0.1f)] public float debugTacticalDiameterAtCruiseLengths = 4f;
            [Min(0.1f)] public float debugTacticalDiameterAtFlankLengths = 5f;
            [Range(0f, 0.5f)] public float debugTurnSpeedLossFactor = 0.015f;
            [Range(0.1f, 1f)] public float debugTightTurnSpeedMultiplier = 0.65f;
            [Min(0.02f)] public float debugMinimumTacticalDiameterLengths = 0.25f;

//Pathing Safety-------------------------------------
        [Header("Pathing Safety")]

            [Min(0f)] public float safetyRadius = 1f; // The radius around the ship that pathfinding considers when plotting a course.
                                                      // Higher values = safer paths, but less efficient routes.

//Arrival-------------------------------------
        [Header("Arrival")]

            [Min(0f)] public float stoppingDistance = 0.1f; // The distance from the target at which the ship considers arrival complete.
                                                            // Higher values = more forgiving arrival, but less precise.

//Legacy Movement Stuffs-------------------------------------
        [Header("Legacy Movement Stuffs")] // Original movement settings retained for serialized assets while the new kinematics settle in.
                                           // The inertial movement model does not currently read these fields.

            [Min(0f)] public float maxSpeed = 10f;
            [Min(0f)] public float acceleration = 5f;
            [Min(0f)] public float deceleration = 10f;
            [Min(0f)] public float turnRateDegreesPerSecond = 180f;
            [Min(0f)] public float turningRadius = 2.5f;

//-------------------------------------
    }
}
