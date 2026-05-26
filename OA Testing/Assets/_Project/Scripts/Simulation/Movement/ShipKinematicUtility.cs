using OA.Simulation.Units;
using UnityEngine;

namespace OA.Simulation.Movement
{
    public static class ShipKinematicUtility
    {
        public static float GetTargetSpeedKnots(MovementSpeedMode mode, MovementProfileDefinition profile)
        {
            if (profile == null)
            {
                return 0f;
            }

            return mode == MovementSpeedMode.Flank
                ? Mathf.Max(profile.cruiseSpeedKnots, profile.flankSpeedKnots)
                : Mathf.Max(0f, profile.cruiseSpeedKnots);
        }

        public static float CalculateTurnRadiusWorld(
            float speedKnots,
            MovementProfileDefinition profile,
            ShipHandlingProfile handling)
        {
            if (profile == null)
            {
                return 0f;
            }

            float flank = Mathf.Max(0.001f, profile.flankSpeedKnots);
            float cruise = Mathf.Clamp(profile.cruiseSpeedKnots, 0f, flank);
            float speed01 = flank > cruise
                ? Mathf.InverseLerp(cruise, flank, speedKnots)
                : Mathf.Clamp01(speedKnots / flank);

            float tacticalDiameterLengths = Mathf.Lerp(
                handling.TacticalDiameterAtCruiseLengths,
                handling.TacticalDiameterAtFlankLengths,
                speed01);

            float radiusMeters = Mathf.Max(1f, profile.lengthMeters) * tacticalDiameterLengths * 0.5f;
            return radiusMeters / Mathf.Max(0.001f, profile.metersPerWorldUnit);
        }
    
        public static float CalculateTurnRateDegreesPerSecond(
            float speedKnots,
            MovementProfileDefinition profile,
            ShipHandlingProfile handling)
        {
            if (speedKnots <= 0.001f)
            {
                return 0f;
            }

            float radiusWorld = CalculateTurnRadiusWorld(speedKnots, profile, handling);
            float speedWorld = MovementMath.KnotsToWorldUnitsPerSecond(speedKnots, profile.metersPerWorldUnit);

            return Mathf.Rad2Deg * (speedWorld / Mathf.Max(0.001f, radiusWorld));
        }
    }
}