using OA.Simulation.Units;
using UnityEngine;

namespace OA.Simulation.Movement
{
    public enum ShipAgilityClass
    {
        VeryLow = 0,
        Low = 1,
        Normal = 2,
        High = 3,
        VeryHigh = 4,
        Exceptional = 5
    }

    public struct ShipHandlingProfile
    {
        public float RudderShiftTimeSeconds; // Time it takes for the ship to shift its rudder from hard over to hard over in the opposite direction.
                                             // Lower values = faster rudder shifts and quicker turns.
        
        public float YawAccelerationDegreesPerSecondSquared; // How quickly the ship can change its heading. Higher values = faster turns.
        
        public float YawDampingPerSecond; // Damping factor for yaw changes. Higher values = quicker return to straight heading after a turn.
        
        public float VelocityAlignmentTimeSeconds; // Time it takes for the ship's velocity vector to align with its heading after a turn.
                                                   // Lower values = quicker alignment and less "slipping" during turns.
        
        public float MaxDriftAngleDegrees; // Maximum angle between the ship's velocity vector and its heading before it is considered "drifting".
                                           // Higher values = more drift allowed during turns.
        
        public float TacticalDiameterAtCruiseLengths; // The diameter of the turning circle at cruise speed.
                                                      // Measured in ship lengths (4f = 4 ship lengths). Higher values = wider turns.
        
        public float TacticalDiameterAtFlankLengths; // The diameter of the turning circle at flank speed.
                                                     // Measured in ship lengths (5f = 5 ship lengths). Higher values = wider turns.
        
        public float TurnSpeedLossFactor; // How much the ship's turn rate is reduced at higher speeds.
                                          // Higher values = more reduction in turn rate at higher speeds.
        
        public float TightTurnSpeedMultiplier; // Multiplier for the ship's speed during tight turns (when turning radius is below a certain threshold).
                                               // Lower values = more speed reduction during tight turns.

        public float MinimumTacticalDiameterLengths; // Near-pivot diameter available only at very low maneuvering speed.
                                                     // Larger hulls still pivot broadly; more agile craft tighten more sharply.
    }

    public static class ShipAgilityPresets
    {
        public static ShipHandlingProfile Resolve(MovementProfileDefinition profile)
        {
            if (profile != null && profile.useDebugHandlingOverrides)
            {
                return new ShipHandlingProfile
                {
                    RudderShiftTimeSeconds = profile.debugRudderShiftTimeSeconds,
                    YawAccelerationDegreesPerSecondSquared = profile.debugYawAccelerationDegreesPerSecondSquared,
                    YawDampingPerSecond = profile.debugYawDampingPerSecond,
                    VelocityAlignmentTimeSeconds = profile.debugVelocityAlignmentTimeSeconds,
                    MaxDriftAngleDegrees = profile.debugMaxDriftAngleDegrees,
                    TacticalDiameterAtCruiseLengths = profile.debugTacticalDiameterAtCruiseLengths,
                    TacticalDiameterAtFlankLengths = profile.debugTacticalDiameterAtFlankLengths,
                    TurnSpeedLossFactor = profile.debugTurnSpeedLossFactor,
                    TightTurnSpeedMultiplier = profile.debugTightTurnSpeedMultiplier,
                    MinimumTacticalDiameterLengths = profile.debugMinimumTacticalDiameterLengths
                };
            }

            switch (profile != null ? profile.agility : ShipAgilityClass.Normal)
            {
                case ShipAgilityClass.VeryLow:
                    return Preset(14f, 0.35f, 0.5f, 24f, 8f, 5.5f, 7.0f, 0.012f, 0.55f, 0.45f);

                case ShipAgilityClass.Low:
                    return Preset(9f, 0.65f, 0.75f, 16f, 10f, 4.8f, 6.0f, 0.015f, 0.60f, 0.35f);

                case ShipAgilityClass.High:
                    return Preset(3f, 2.3f, 1.4f, 6f, 18f, 3.0f, 3.8f, 0.026f, 0.72f, 0.18f);

                case ShipAgilityClass.VeryHigh:
                    return Preset(1.8f, 4.0f, 1.8f, 3.5f, 25f, 2.2f, 3.0f, 0.035f, 0.80f, 0.12f);

                case ShipAgilityClass.Exceptional:
                    return Preset(1.0f, 6.5f, 2.2f, 2.2f, 32f, 1.6f, 2.3f, 0.045f, 0.90f, 0.08f);

                default:
                    return Preset(5.5f, 1.2f, 1.0f, 10f, 13f, 4.0f, 5.0f, 0.020f, 0.65f, 0.25f);
            }
        }

        private static ShipHandlingProfile Preset(
            float rudderShift,
            float yawAcceleration,
            float yawDamping,
            float velocityAlignment,
            float maxDrift,
            float cruiseDiameter,
            float flankDiameter,
            float speedLoss,
            float tightTurnSpeedMultiplier,
            float minimumTacticalDiameterLengths)
        {
            return new ShipHandlingProfile
            {
                RudderShiftTimeSeconds = Mathf.Max(0.01f, rudderShift),
                YawAccelerationDegreesPerSecondSquared = Mathf.Max(0.01f, yawAcceleration),
                YawDampingPerSecond = Mathf.Max(0.01f, yawDamping),
                VelocityAlignmentTimeSeconds = Mathf.Max(0.01f, velocityAlignment),
                MaxDriftAngleDegrees = Mathf.Max(0f, maxDrift),
                TacticalDiameterAtCruiseLengths = Mathf.Max(0.1f, cruiseDiameter),
                TacticalDiameterAtFlankLengths = Mathf.Max(0.1f, flankDiameter),
                TurnSpeedLossFactor = Mathf.Clamp(speedLoss, 0f, 0.5f),
                TightTurnSpeedMultiplier = Mathf.Clamp(tightTurnSpeedMultiplier, 0.1f, 1f),
                MinimumTacticalDiameterLengths = Mathf.Max(0.02f, minimumTacticalDiameterLengths)
            };
        }
    }
}
