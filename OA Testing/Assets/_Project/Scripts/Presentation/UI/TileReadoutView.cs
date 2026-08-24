using UnityEngine;
using UnityEngine.UIElements;
using OA.Simulation.Navigation;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OA.Presentation.UI
{
    public sealed class TileReadoutView
    {
        private readonly Label coordinatesLabel;
        private readonly Label typeLabel;
        private readonly Label depthLabel;
        private readonly Label effectsLabel;

        public TileReadoutView(VisualElement root)
        {
            coordinatesLabel = root?.Q<Label>("tileCoordinatesLabel");
            typeLabel = root?.Q<Label>("tileTypeLabel");
            depthLabel = root?.Q<Label>("tileDepthLabel");
            effectsLabel = root?.Q<Label>("tileEffectsLabel");
        }

        public void Refresh(HexMapRuntime map, Camera camera)
        {
            if (map == null || camera == null)
            {
                SetEmpty("No map linked");
                return;
            }

            if (!TryGetPointerScreenPosition(out Vector2 screenPosition) ||
                !TryScreenToWorldCell(map, camera, screenPosition, out Vector2Int cell))
            {
                SetEmpty("Hover a tile");
                return;
            }

            MapTileType tileType = map.GetTileType(cell.x, cell.y);
            WaterDepthClass depthClass = map.GetDepthClass(cell.x, cell.y);

            SetLabel(coordinatesLabel, $"Coordinates: {cell.x}, {cell.y}");
            SetLabel(typeLabel, $"Type: {FormatTileType(tileType)}");
            SetLabel(
                depthLabel,
                $"Depth: {(map.IsBlocked(cell.x, cell.y) ? "N/A" : FormatDepthType(depthClass))}");
            SetLabel(effectsLabel, $"Effects: {DescribeTileEffects(map, cell, tileType, depthClass)}");
        }

        private void SetEmpty(string message)
        {
            SetLabel(coordinatesLabel, $"Coordinates: {message}");
            SetLabel(typeLabel, "Type: -");
            SetLabel(depthLabel, "Depth: -");
            SetLabel(effectsLabel, "Effects: -");
        }

        private static bool TryGetPointerScreenPosition(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
            {
                screenPosition = default;
                return false;
            }

            screenPosition = Mouse.current.position.ReadValue();
            return true;
#else
            screenPosition = Input.mousePosition;
            return true;
#endif
        }

        private static bool TryScreenToWorldCell(
            HexMapRuntime map,
            Camera camera,
            Vector2 screenPosition,
            out Vector2Int cell)
        {
            cell = default;

            float distance = Mathf.Abs(camera.transform.position.z);
            Vector3 world = camera.ScreenToWorldPoint(
                new Vector3(screenPosition.x, screenPosition.y, distance));

            Vector2 world2 = new Vector2(world.x, world.y);
            return map.TryWorldToCell(world2, out cell);
        }

        private static string FormatTileType(MapTileType tileType)
        {
            switch (tileType)
            {
                case MapTileType.VeryDeep:
                    return "Very Deep";
                case MapTileType.LargeHill:
                    return "Large Hill";
                default:
                    return tileType.ToString();
            }
        }

        private static string FormatDepthType(WaterDepthClass depthClass)
        {
            return depthClass == WaterDepthClass.VeryDeep
                ? "Very Deep"
                : depthClass.ToString();
        }

        private static string DescribeTileEffects(
            HexMapRuntime map,
            Vector2Int cell,
            MapTileType tileType,
            WaterDepthClass depthClass)
        {
            if (NavigationTerrainRules.IsLand(tileType))
            {
                return "Blocked for ships";
            }

            float moveCost = map.GetMoveCost(cell.x, cell.y);
            string roughSuffix = moveCost > 1.01f
                ? $"; rough speed x{1f / moveCost:0.00}"
                : string.Empty;

            switch (depthClass)
            {
                case WaterDepthClass.Shallow:
                    return $"Shallow draft only; sub stealth x{NavigationTerrainRules.GetSubmarineStealthMultiplier(depthClass):0.00}{roughSuffix}";
                case WaterDepthClass.Coastal:
                    return $"Deep draft speed x{NavigationTerrainRules.GetSpeedMultiplier(map, cell.x, cell.y, ShipDraftClass.Deep):0.00}; sub stealth x{NavigationTerrainRules.GetSubmarineStealthMultiplier(depthClass):0.00}{roughSuffix}";
                case WaterDepthClass.VeryDeep:
                    return $"Sub stealth x{NavigationTerrainRules.GetSubmarineStealthMultiplier(depthClass):0.00}{roughSuffix}";
                case WaterDepthClass.Abyssal:
                    return $"Sub loiter; stealth x{NavigationTerrainRules.GetSubmarineStealthMultiplier(depthClass):0.00}{roughSuffix}";
                default:
                    return $"Normal ocean; sub stealth x{NavigationTerrainRules.GetSubmarineStealthMultiplier(depthClass):0.00}{roughSuffix}";
            }
        }

        private static void SetLabel(Label label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }
    }
}
