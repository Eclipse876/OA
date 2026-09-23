using UnityEngine;

namespace OA.Simulation.Navigation
{
    public enum WaterDepthClass // Depth classes for water. Shallow is only usable by smaller boats and has a stealth debuff for subs.
    {
        Shallow = 0,
        Deep = 1
    }

    public enum  ShipDraftClass
    {
        Shallow = 0,
        Deep = 1
    }

    public struct NavigationProfile
    {
        public float SafetyRadiusWorld;
        public ShipDraftClass DraftClass;
        
        public NavigationProfile(float safetyRadiusWorld, ShipDraftClass draftClass)
        {
            SafetyRadiusWorld = safetyRadiusWorld;
            DraftClass = draftClass;
        }
    }
}