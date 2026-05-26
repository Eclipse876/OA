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
            [SerializeField] private Color shallowWaterColor = new Color(0.06f, 0.28f, 0.32f);
            [SerializeField] private Color deepWaterColor = new Color(0.01f, 0.04f, 0.16f);
            [SerializeField] private Color roughWaterColor = new Color(0.06f, 0.16f, 0.34f);
            [SerializeField] private Color obstacleColor = new Color(0.15f, 0.22f, 0.35f);
            [SerializeField] private Color borderColor = new Color(0.12f, 0.22f, 0.4f, 0.95f);
            [SerializeField, Range(0f, 1f)] private float restrictedTintStrength = 0.35f;

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
                    int index = map.GetIndex(x, y);

                    bool terrainBlocked = map.IsBlocked(x, y);

                    bool routeBlocked = blockedMask != null && index < blockedMask.Length
                        ? blockedMask[index]
                        : terrainBlocked;

                    bool shallow = map.GetDepthClass(x, y) == WaterDepthClass.Shallow;
                    bool rough = map.GetMoveCost(x, y) > roughThreshold;

                    Vector3Int cell = new Vector3Int(x, y, 0);

                    TileBase chosenTile;
                    Color chosenColor;

                     if (terrainBlocked)
                    {
                        chosenTile = obstacleTile;
                        chosenColor = obstacleColor;
                    }
                    else if (shallow)
                    {
                        chosenTile = shallowWaterTile != null ? shallowWaterTile : deepWaterTile;
                        chosenColor = shallowWaterColor;
                    }
                    else if (rough)
                    {
                        chosenTile = roughWaterTile != null ? roughWaterTile : deepWaterTile;
                        chosenColor = roughWaterColor;
                    }
                    else
                    {
                        chosenTile = deepWaterTile;
                        chosenColor = deepWaterColor;
                    }
                    
                    if (routeBlocked && !terrainBlocked)
                    {
                        chosenColor = Color.Lerp(chosenColor, obstacleColor, restructedTintStrength);
                    }

                    tilemap.SetTile(cell, chosenTile);

                    tilemap.SetTileFlags(cell, TileFlags.None);
                    tilemap.SetColor(cell, chosenColor);

                    Vector3 world = tilemap.GetCellCenterWorld(cell);
                    map.SetWorldCenter(x, y, new Vector2(world.x, world.y));

                }
            }
            
            map.MarkWorldCenterReady();
            tilemap.CompressBounds();
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

                    path terrainBlocked = map.IsBlocked(x, y);

                    bool routeBlocked = blockedMask != null && mapIndex < blockedMask.Length
                        ? blockedMask[mapIndex]
                        : map.IsBlocked(x, y);

                    tgs.CellSetCanCross(tgsCellIndex, !routeBlocked);

                    Color cellColor;

                    if (terrainBlocked)
                    {
                        cellColor = obstacleColor;
                    }
                    else if (shallow)
                    {
                        cellColor = shallowWaterColor;
                    }
                    else
                    {
                        cellColor = Color.Lerp(
                            deepWaterColor,
                            roughWaterColor,
                            Mathf.InverseLerp(1f, 3.2f, map.GetMoveCost(x, y)));
                    }

                    if (routeBlocked && !terrainBlocked)
                    {
                        cellColor = Color.Lerp(cellColor, obstacleColor, restrictedTintStrength);
                    }

                    tgs.CellSetColor(tgsCellIndex, cellColor);

                    Vector3 world = tgs.CellGetPosition(tgsCellIndex, true, 0f);
                    map.SetWorldCenter(x, y, new Vector2(world.x, world.y));
                }
            }

            map.MarkWorldCentersReady();
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
