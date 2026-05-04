using OA.Simulation.Navigation;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace OA.Presentation.Debug
{
    public sealed class TilemapHexGridPresenter : MonoBehaviour, INavigationGridPresenter
    {
        [Header("References")]
            [SerializeField] private Tilemap tilemap;
            [SerializeField] private HexMapDefinition previewDefinition;

        [Header("Tiles")]
            [SerializeField] private TileBase deepWaterTile;
            [SerializeField] private TileBase roughWaterTile;
            [SerializeField] private TileBase obstacleTile;

        [Header("Colors")]
            [SerializeField] private Color deepWaterColor = new Color(0.01f, 0.04f, 0.16f);
            [SerializeField] private Color roughWaterColor = new Color(0.06f, 0.16f, 0.34f);
            [SerializeField] private Color obstacleColor = new Color(0.15f, 0.22f, 0.35f);

        [Header("Behavior")]
            [SerializeField] private bool clearBeforePaint = true;
            [SerializeField] private float roughThreshold = 1.01f;

        private HexMapRuntime lastMap;

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

            if (clearBeforePaint)
            {
                tilemap.ClearAllTiles();
            }

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int index = map.GetIndex(x, y);
                    bool blocked = blockedMask != null && index < blockedMask.Length
                        ? blockedMask[index]
                        : map.IsBlocked(x, y);

                    Vector3Int cell = new Vector3Int(x, y, 0);

                    TileBase chosenTile;
                    Color chosenColor;

                    if (blocked)
                    {
                        chosenTile = obstacleTile;
                        chosenColor = obstacleColor;
                    }
                    else
                    {
                        bool rough = map.GetMoveCost(x, y) > roughThreshold;
                        chosenTile = rough ? roughWaterTile : deepWaterTile;
                        chosenColor = rough ? roughWaterColor : deepWaterColor;
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

        public bool TryWorldToCell(Vector2 worldPosition, out Vector2Int cell)
        {
            if (tilemap == null)
            {
                cell = default;
                return false;
            }

            Vector3Int raw = tilemap.WorldToCell(new Vector3(worldPosition.x, worldPosition.y, 0f));
            if (lastMap != null && lastMap.InBounds(raw.x, raw.y))
            {
                cell = new Vector2Int(raw.x, raw.y);
                return true;
            }

            if (lastMap != null)
            {
                return lastMap.TryWorldToCell(worldPosition, out cell);
            }

            cell = default;
            return false;
        }

        [ContextMenu("Refresh Grid Preview From Definition")]
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
