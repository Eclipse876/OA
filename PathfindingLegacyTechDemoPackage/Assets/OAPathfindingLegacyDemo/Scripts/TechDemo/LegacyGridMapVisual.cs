using UnityEngine;
using UnityEngine.Rendering;

namespace OA.LegacyTechDemo
{
    public sealed class LegacyGridMapVisual : MonoBehaviour
    {
        private const float WalkableHeight = 0.08f;
        private const float BlockedHeight = 1.4f;

        private TileVisual[] _tiles;
        private MaterialPropertyBlock _propertyBlock;

        public Color DeepWaterColor { get; set; } = new Color(0.09f, 0.2f, 0.3f);
        public Color RoughWaterColor { get; set; } = new Color(0.12f, 0.31f, 0.41f);
        public Color ObstacleColor { get; set; } = new Color(0.26f, 0.25f, 0.22f);

        public void Build(LegacyGridMap map)
        {
            ClearTiles();

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            _tiles = new TileVisual[map.Width * map.Height];

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = $"LegacyTile_{x}_{y}";
                    tile.transform.SetParent(transform, false);

                    var collider = tile.GetComponent<Collider>();
                    if (collider != null)
                    {
                        Destroy(collider);
                    }

                    var renderer = tile.GetComponent<Renderer>();
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;

                    _tiles[map.GetIndex(x, y)] = new TileVisual
                    {
                        transform = tile.transform,
                        renderer = renderer
                    };
                }
            }

            Refresh(map);
        }

        public void Refresh(LegacyGridMap map)
        {
            if (_tiles == null || _tiles.Length != map.Width * map.Height)
            {
                Build(map);
                return;
            }

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int index = map.GetIndex(x, y);
                    bool blocked = map.IsBlocked(x, y);
                    TileVisual tile = _tiles[index];

                    float height = blocked ? BlockedHeight : WalkableHeight;
                    tile.transform.position = map.GridToWorld(x, y, height * 0.5f);
                    tile.transform.localScale = new Vector3(map.CellSize * 0.95f, height, map.CellSize * 0.95f);

                    Color color = blocked
                        ? ObstacleColor
                        : Color.Lerp(DeepWaterColor, RoughWaterColor, Mathf.InverseLerp(1f, 1.75f, map.GetMoveCost(x, y)));

                    _propertyBlock.Clear();
                    _propertyBlock.SetColor("_Color", color);
                    _propertyBlock.SetColor("_BaseColor", color);
                    tile.renderer.SetPropertyBlock(_propertyBlock);
                }
            }
        }

        private void ClearTiles()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
        }

        private struct TileVisual
        {
            public Transform transform;
            public Renderer renderer;
        }
    }
}
