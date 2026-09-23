using UnityEngine;

namespace OA.Simulation.Navigation
{
    // Water depth classes. Integer values 0 and 1 preserve old baked assets:
    // existing Shallow stays Shallow, existing Deep stays Deep.
    public enum WaterDepthClass
    {
        Shallow = 0,
        Deep = 1,
        Coastal = 2,
        VeryDeep = 3,
        Abyssal = 4
    }

    public enum LandElevationClass
    {
        Land = 0,
        Hill = 1,
        LargeHill = 2,
        Mountain = 3,
        Peak = 4
    }

    public enum MapTileType
    {
        Shallow = 0,
        Deep = 1,
        Coastal = 2,
        VeryDeep = 3,
        Abyssal = 4,
        Land = 100,
        Hill = 101,
        LargeHill = 102,
        Mountain = 103,
        Peak = 104
    }

    public enum ShipDraftClass
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

    public static class NavigationTerrainRules
    {
        private const float DeepDraftCoastalMoveCostMultiplier = 1.15f;

        public static bool IsWater(MapTileType tileType)
        {
            return (int)tileType < 100;
        }

        public static bool IsLand(MapTileType tileType)
        {
            return !IsWater(tileType);
        }

        public static MapTileType ToMapTileType(WaterDepthClass depthClass)
        {
            switch (depthClass)
            {
                case WaterDepthClass.Shallow:
                    return MapTileType.Shallow;
                case WaterDepthClass.Coastal:
                    return MapTileType.Coastal;
                case WaterDepthClass.VeryDeep:
                    return MapTileType.VeryDeep;
                case WaterDepthClass.Abyssal:
                    return MapTileType.Abyssal;
                default:
                    return MapTileType.Deep;
            }
        }

        public static MapTileType ToMapTileType(LandElevationClass elevationClass)
        {
            switch (elevationClass)
            {
                case LandElevationClass.Hill:
                    return MapTileType.Hill;
                case LandElevationClass.LargeHill:
                    return MapTileType.LargeHill;
                case LandElevationClass.Mountain:
                    return MapTileType.Mountain;
                case LandElevationClass.Peak:
                    return MapTileType.Peak;
                default:
                    return MapTileType.Land;
            }
        }

        public static bool IsForbiddenForShip(
            HexMapRuntime map,
            int x,
            int y,
            ShipDraftClass draftClass)
        {
            if (map == null || !map.InBounds(x, y))
            {
                return true;
            }

            if (map.IsBlocked(x, y))
            {
                return true;
            }

            return !CanEnterShallow(draftClass) &&
                   map.GetDepthClass(x, y) == WaterDepthClass.Shallow;
        }

        public static float GetTraversalCostMultiplier(
            HexMapRuntime map,
            int x,
            int y,
            ShipDraftClass draftClass)
        {
            if (map == null || !map.InBounds(x, y))
            {
                return float.PositiveInfinity;
            }

            float cost = map.GetMoveCost(x, y);

            if (!CanEnterShallow(draftClass) &&
                map.GetDepthClass(x, y) == WaterDepthClass.Coastal)
            {
                cost *= DeepDraftCoastalMoveCostMultiplier;
            }

            return Mathf.Max(1f, cost);
        }

        public static float GetSpeedMultiplier(
            HexMapRuntime map,
            int x,
            int y,
            ShipDraftClass draftClass)
        {
            return 1f / GetTraversalCostMultiplier(map, x, y, draftClass);
        }

        public static float GetSubmarineStealthMultiplier(WaterDepthClass depthClass)
        {
            switch (depthClass)
            {
                case WaterDepthClass.Shallow:
                    return 0.35f;
                case WaterDepthClass.Coastal:
                    return 0.5f;
                case WaterDepthClass.VeryDeep:
                    return 1.25f;
                case WaterDepthClass.Abyssal:
                    return 1.6f;
                default:
                    return 1f;
            }
        }

        private static bool CanEnterShallow(ShipDraftClass draftClass)
        {
            return draftClass == ShipDraftClass.Shallow;
        }
    }
}
