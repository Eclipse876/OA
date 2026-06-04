// TilemapHexGridPresenter.cs:
// Plain Unity Tilemap version of the hex grid. Good for when we want to see
// the ocean without depending on TGS magic, and for translating clicks back into map cells.
using OA.Simulation.Navigation;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace OA.Presentation.Debug
{
    // Presenter that paints a Tilemap from HexMapRuntime and handles world-to-cell lookup.
    public sealed class TilemapHexGridPresenter : MonoBehaviour, INavigationGridPresenter
    {
        // Scene/asset references needed for drawing and editor preview refreshes.
        [Header("References")]
            [SerializeField] private Tilemap tilemap;
            [SerializeField] private HexMapDefinition previewDefinition;

        // Tiles used for normal water, rough water, and blocked cells.
        [Header("Tiles")]
            [SerializeField] private TileBase shallowWaterTile;
            [SerializeField] private TileBase deepWaterTile;
            [SerializeField] private TileBase roughWaterTile;
            [SerializeField] private TileBase obstacleTile;

        // Tint colors applied per cell after the tile is placed.
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

        // Small behavior knobs for repainting and deciding when water counts as rough.
        [Header("Behavior")]
            [SerializeField] private bool clearBeforePaint = true;
            [SerializeField] private float roughThreshold = 1.01f;

        private HexMapRuntime lastMap;

        // Repaints the whole Tilemap and caches each cell center back into the runtime map.
        public void BuildOrRefresh(HexMapRuntime map, bool[] blockedMask = null)
        {
            if (map == null)
            {
                return;
            }

            if (tilemap == null)
            {
                UnityEngine.Debug.LogError("[TilemapHexGridPresenter] Missing Tilemap reference.");
                return;
            }

            lastMap = map;
            ApplyDefaultPalette();
            _ = blockedMask; // Safety masks affect pathing, not the base terrain palette.

            // Optional clear keeps old oversized maps from leaving stray tiles behind.
            if (clearBeforePaint)
            {
                tilemap.ClearAllTiles();
            }

            // Paint every runtime cell into matching tilemap coordinates.
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    bool terrainBlocked = map.IsBlocked(x, y);
                    
                    WaterDepthClass depthClass = map.GetDepthClass(x, y);
                    bool rough = map.GetMoveCost(x, y) > roughThreshold;

                    Vector3Int cell = new Vector3Int(x, y, 0);

                    TileBase chosenTile;
                    Color chosenColor;

                    if (terrainBlocked)
                    {
                        chosenTile = obstacleTile;
                        chosenColor = GetLandColor(map.GetLandElevationClass(x, y));
                    }
                    else
                    {
                        chosenTile = GetWaterTile(depthClass, rough);
                        chosenColor = GetWaterColor(depthClass);

                        if (rough)
                        {
                            chosenColor = Color.Lerp(
                                chosenColor,
                                roughWaterColor,
                                Mathf.InverseLerp(1f, 6.5f, map.GetMoveCost(x, y)));
                        }
                    }

                    tilemap.SetTile(cell, chosenTile);

                    // Important: unlock per-cell color so SetColor works.
                    tilemap.SetTileFlags(cell, TileFlags.None);
                    tilemap.SetColor(cell, chosenColor);

                    Vector3 world = tilemap.GetCellCenterWorld(cell);
                    map.SetWorldCenter(x, y, new Vector2(world.x, world.y));
                }
            }

            map.MarkWorldCentersReady();
            tilemap.CompressBounds();
        }

        private void OnEnable()
        {
            ApplyDefaultPalette();
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
        }

        private TileBase GetWaterTile(WaterDepthClass depthClass, bool rough)
        {
            if (rough && roughWaterTile != null)
            {
                return roughWaterTile;
            }

            return depthClass == WaterDepthClass.Shallow && shallowWaterTile != null
                ? shallowWaterTile
                : deepWaterTile;
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

        // Converts a world click into the nearest rendered map cell.
        public bool TryWorldToCell(Vector2 worldPosition, out Vector2Int cell)
        {
            if (lastMap != null &&
                lastMap.TryWorldToCell(worldPosition, out cell))
            {
                return true;
            }

            if (tilemap != null)
            {
                Vector3Int raw = tilemap.WorldToCell(
                    new Vector3(worldPosition.x, worldPosition.y, 0f));

                if (lastMap != null && lastMap.InBounds(raw.x, raw.y))
                {
                    cell = new Vector2Int(raw.x, raw.y);
                    return true;
                }
            }

            cell = default;
            return false;
        }

        [ContextMenu("Refresh Grid Preview From Definition")]
        // Editor context helper for previewing a HexMapDefinition without entering play mode.
        private void RefreshGridPreviewFromDefinition()
        {
            if (previewDefinition == null)
            {
                UnityEngine.Debug.LogWarning("[TilemapHexGridPresenter] Assign Preview Definition first.");
                return;
            }

            HexMapRuntime map = previewDefinition.CreateRuntimeCopy();
            BuildOrRefresh(map, null);
        }
    }
}
