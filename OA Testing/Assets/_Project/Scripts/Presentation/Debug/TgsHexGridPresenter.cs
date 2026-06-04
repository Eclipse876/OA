// TgsHexGridPresenter.cs:
// TGS version of drawing the hex map. If TerrainGridSystem is in the project,
// this paints the ocean cells and teaches the map where each hex ended up in world space.
#if OA_USE_TGS
using OA.Simulation.Navigation;
using TGS;
using UnityEngine;

namespace OA.Presentation.Debug
{
    // Presenter that uses TerrainGridSystem for visuals and click-to-cell lookup.
    public sealed class TgsHexGridPresenter : MonoBehaviour, INavigationGridPresenter
    {
        [Header("References")]
        [SerializeField] private TerrainGridSystem tgs;
        [SerializeField] private HexMapDefinition previewDefinition;

        [Header("Colors")]
        [SerializeField] private Color shallowWaterColor = new Color(0.22f, 0.68f, 0.66f);
        [SerializeField] private Color coastalWaterColor = new Color(0.11f, 0.43f, 0.58f);
        [SerializeField] private Color deepWaterColor = new Color(0.07f, 0.2f, 0.46f);
        [SerializeField] private Color veryDeepWaterColor = new Color(0.04f, 0.11f, 0.3f);
        [SerializeField] private Color abyssalWaterColor = new Color(0.03f, 0.04f, 0.13f);
        [SerializeField] private Color roughWaterColor = new Color(0.37f, 0.52f, 0.62f);
        [SerializeField] private Color landColor = new Color(0.32f, 0.5f, 0.25f);
        [SerializeField] private Color hillColor = new Color(0.42f, 0.47f, 0.24f);
        [SerializeField] private Color largeHillColor = new Color(0.52f, 0.37f, 0.2f);
        [SerializeField] private Color mountainColor = new Color(0.36f, 0.29f, 0.27f);
        [SerializeField] private Color peakColor = new Color(0.2f, 0.2f, 0.23f);
        [SerializeField] private Color borderColor = new Color(0.09f, 0.16f, 0.27f, 0.95f);

        private bool configured;
        private HexMapRuntime lastMap;

        // Configures TGS if needed, then repaints cells from the latest map data.
        public void BuildOrRefresh(HexMapRuntime map, bool[] blockedMask = null)
        {
            if (map == null)
            {
                return;
            }

            if (tgs == null)
            {
                UnityEngine.Debug.LogError("[TgsHexGridPresenter] Missing TerrainGridSystem reference.");
                return;
            }

            lastMap = map;
            ApplyDefaultPalette();
            ConfigureIfNeeded(map);
            ApplyMapToGrid(map, blockedMask);
        }

        private void OnEnable()
        {
            ApplyDefaultPalette();
        }

        // Asks TGS which cell is under a world position.
        public bool TryWorldToCell(Vector2 worldPosition, out Vector2Int cell)
        {
            if (tgs != null)
            {
                Cell clicked = tgs.CellGetAtWorldPosition(new Vector3(worldPosition.x, worldPosition.y, 0f));
                if (clicked != null)
                {
                    cell = new Vector2Int(clicked.column, clicked.row);
                    return true;
                }
            }

            // Should a TGS click land outside its painted grid, use the same nearest-center
            // fallback as the Tilemap presenter so input and pathfinding agree.
            if (lastMap != null)
            {
                return lastMap.TryWorldToCell(worldPosition, out cell);
            }

            cell = default;
            return false;
        }

        [ContextMenu("Refresh Grid Preview From Definition")]
        // Editor context helper for repainting the preview from a HexMapDefinition asset.
        private void RefreshGridPreviewFromDefinition()
        {
            if (previewDefinition == null)
            {
                UnityEngine.Debug.LogWarning("[TgsHexGridPresenter] Assign Preview Definition first.");
                return;
            }

            HexMapRuntime map = previewDefinition.CreateRuntimeCopy();
            BuildOrRefresh(map, null);
        }

        // Applies one-time TGS grid settings when the map size changes or first initializes.
        private void ConfigureIfNeeded(HexMapRuntime map)
        {
            if (configured &&
                tgs.rowCount == map.Height &&
                tgs.columnCount == map.Width)
            {
                return;
            }

            tgs.cameraMain = Camera.main;
            tgs.respectOtherUI = true;
            tgs.highlightMode = HighlightMode.None;
            tgs.allowHighlightWhileDragging = false;
            tgs.showTerritories = false;
            tgs.colorizeTerritories = false;
            tgs.numTerritories = 1;
            tgs.showCells = true;
            tgs.transparentBackground = false;
            tgs.pointyTopHexagons = false;
            tgs.evenLayout = false;
            tgs.gridTopology = GridTopology.Hexagonal;
            tgs.cellBorderThickness = 1.35f;
            tgs.cellFillPadding = 0f;
            tgs.cellBorderColor = borderColor;

            tgs.SetDimensionsAndType(map.Height, map.Width, GridTopology.Hexagonal, false);
            tgs.Redraw();

            configured = true;
        }

        // Paints each TGS cell, sets traversability, and caches world centers on the runtime map.
        private void ApplyMapToGrid(HexMapRuntime map, bool[] blockedMask)
        {
            if (tgs.cells == null || tgs.cells.Count == 0)
            {
                return;
            }

            tgs.cellBorderColor = borderColor;

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int tgsCellIndex = tgs.CellGetIndex(y, x);
                    if (tgsCellIndex < 0)
                    {
                        continue;
                    }

                    // Prefer safety-expanded mask when present so visuals match the actual path graph.
                    int mapIndex = map.GetIndex(x, y);
                    bool terrainBlocked = map.IsBlocked(x, y);
                    bool routeBlocked = blockedMask != null && mapIndex < blockedMask.Length
                        ? blockedMask[mapIndex]
                        : terrainBlocked;

                    tgs.CellSetCanCross(tgsCellIndex, !routeBlocked);

                    Color cellColor;

                    if (terrainBlocked)
                    {
                        cellColor = GetLandColor(map.GetLandElevationClass(x, y));
                    }
                    else
                    {
                        cellColor = GetWaterColor(map.GetDepthClass(x, y));
                        cellColor = Color.Lerp(
                            cellColor,
                            roughWaterColor,
                            Mathf.InverseLerp(1f, 6.5f, map.GetMoveCost(x, y)));
                    }

                    tgs.CellSetColor(tgsCellIndex, cellColor);

                    Vector3 world = tgs.CellGetPosition(tgsCellIndex, true, 0f);
                    map.SetWorldCenter(x, y, new Vector2(world.x, world.y));
                }
            }

            map.MarkWorldCentersReady();
        }

        private Color GetWaterColor(WaterDepthClass depthClass)
        {
            switch (depthClass)
            {
                case WaterDepthClass.Shallow:
                    return shallowWaterColor;
                case WaterDepthClass.Coastal:
                    return coastalWaterColor;
                case WaterDepthClass.VeryDeep:
                    return veryDeepWaterColor;
                case WaterDepthClass.Abyssal:
                    return abyssalWaterColor;
                default:
                    return deepWaterColor;
            }
        }

        private Color GetLandColor(LandElevationClass elevationClass)
        {
            switch (elevationClass)
            {
                case LandElevationClass.Hill:
                    return hillColor;
                case LandElevationClass.LargeHill:
                    return largeHillColor;
                case LandElevationClass.Mountain:
                    return mountainColor;
                case LandElevationClass.Peak:
                    return peakColor;
                default:
                    return landColor;
            }
        }

        // Keeps stale scene/Inspector color values from surviving code palette changes.
        private void ApplyDefaultPalette()
        {
            shallowWaterColor = new Color(0.22f, 0.68f, 0.66f);
            coastalWaterColor = new Color(0.11f, 0.43f, 0.58f);
            deepWaterColor = new Color(0.07f, 0.2f, 0.46f);
            veryDeepWaterColor = new Color(0.04f, 0.11f, 0.3f);
            abyssalWaterColor = new Color(0.03f, 0.04f, 0.13f);
            roughWaterColor = new Color(0.37f, 0.52f, 0.62f);
            landColor = new Color(0.32f, 0.5f, 0.25f);
            hillColor = new Color(0.42f, 0.47f, 0.24f);
            largeHillColor = new Color(0.52f, 0.37f, 0.2f);
            mountainColor = new Color(0.36f, 0.29f, 0.27f);
            peakColor = new Color(0.2f, 0.2f, 0.23f);
            borderColor = new Color(0.09f, 0.16f, 0.27f, 0.95f);
        }
    }
}
#else
using UnityEngine;

namespace OA.Presentation.Debug
{
    // Fallback stub that yells during play if the TGS scripting define is missing.
    public sealed class TgsHexGridPresenter : MonoBehaviour
    {
        // Reports the missing compile define as soon as this placeholder wakes up.
        private void Awake()
        {
            UnityEngine.Debug.LogError(
                "[TgsHexGridPresenter] OA_USE_TGS is not defined. " +
                "Define OA_USE_TGS in Player Settings to compile TGS integration.");
        }
    }
}
#endif
