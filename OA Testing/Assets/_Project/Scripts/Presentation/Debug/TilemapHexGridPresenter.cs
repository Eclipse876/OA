// TilemapHexGridPresenter.cs:
// Plain Unity Tilemap version of the hex grid. Good for when we want to see
// the ocean without depending on TGS magic, and for translating clicks back into map cells.
using System.Collections.Generic;
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

        // Optional texture sprites. Colored textures paint at full strength; fallback tiles use tint colors.
        [Header("Texture Sprites")]
            [SerializeField] private bool useAssignedTextureSprites = false;
            [SerializeField] private Sprite[] oceanTextureSprites;
            [SerializeField] private Sprite[] shallowWaterTextureSprites;
            [SerializeField] private Sprite[] coastalWaterTextureSprites;
            [SerializeField] private Sprite[] deepWaterTextureSprites;
            [SerializeField] private Sprite[] veryDeepWaterTextureSprites;
            [SerializeField] private Sprite[] abyssalWaterTextureSprites;
            [SerializeField] private Sprite[] roughWaterTextureSprites;
            [SerializeField] private Sprite[] landTextureSprites;
            [SerializeField] private Sprite[] hillTextureSprites;
            [SerializeField] private Sprite[] largeHillTextureSprites;
            [SerializeField] private Sprite[] mountainTextureSprites;
            [SerializeField] private Sprite[] peakTextureSprites;

        [Header("Procedural Texture Overlay")]
            [SerializeField] private bool useProceduralTextureOverlay = true;
            [SerializeField, Range(16, 256)] private int proceduralTextureSize = 128;
            [SerializeField, Range(1, 16)] private int proceduralTextureVariants = 10;
            [SerializeField, Range(0f, 1f)] private float proceduralTextureOpacity = 0.75f;
            [SerializeField] private int proceduralOverlaySortingOrderOffset = 1;

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
        private Dictionary<Sprite, TileBase> generatedTextureTiles;
        private readonly Dictionary<int, TileBase> generatedProceduralOverlayTiles =
            new Dictionary<int, TileBase>();
        private readonly List<UnityEngine.Object> generatedProceduralOverlayResources =
            new List<UnityEngine.Object>();
        private Tilemap proceduralOverlayTilemap;

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
            bool paintProceduralOverlay = useProceduralTextureOverlay;
            if (paintProceduralOverlay)
            {
                EnsureProceduralOverlayTilemap();
            }
            else if (proceduralOverlayTilemap != null)
            {
                proceduralOverlayTilemap.ClearAllTiles();
            }

            // Optional clear keeps old oversized maps from leaving stray tiles behind.
            if (clearBeforePaint)
            {
                tilemap.ClearAllTiles();
                if (proceduralOverlayTilemap != null)
                {
                    proceduralOverlayTilemap.ClearAllTiles();
                }
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
                        LandElevationClass elevationClass = map.GetLandElevationClass(x, y);
                        if (useAssignedTextureSprites)
                        {
                            Sprite[] landSprites = GetLandTextureSprites(elevationClass);
                            chosenTile = PickSpriteTile(
                                landSprites,
                                obstacleTile,
                                x,
                                y,
                                GetLandTextureSalt(elevationClass));
                            chosenColor = HasAnySprite(landSprites)
                                ? Color.white
                                : GetLandColor(elevationClass);
                        }
                        else
                        {
                            chosenTile = obstacleTile;
                            chosenColor = GetLandColor(elevationClass);
                        }
                    }
                    else
                    {
                        if (useAssignedTextureSprites)
                        {
                            chosenTile = GetWaterTile(depthClass, rough, x, y);
                            chosenColor = HasAnySprite(GetWaterTextureSprites(depthClass, rough))
                                ? Color.white
                                : GetWaterColor(depthClass);
                        }
                        else
                        {
                            chosenTile = GetFallbackWaterTile(depthClass, rough);
                            chosenColor = GetWaterColor(depthClass);
                        }

                        if (rough)
                        {
                            if (!useAssignedTextureSprites ||
                                !HasAnySprite(GetWaterTextureSprites(depthClass, rough)))
                            {
                                chosenColor = Color.Lerp(
                                    chosenColor,
                                    roughWaterColor,
                                    Mathf.InverseLerp(1f, 6.5f, map.GetMoveCost(x, y)));
                            }
                        }
                    }

                    tilemap.SetTile(cell, chosenTile);

                    // Important: unlock per-cell color so SetColor works.
                    tilemap.SetTileFlags(cell, TileFlags.None);
                    tilemap.SetColor(cell, chosenColor);

                    if (paintProceduralOverlay && proceduralOverlayTilemap != null)
                    {
                        TileBase overlayTile = GetProceduralOverlayTile(
                            terrainBlocked,
                            depthClass,
                            rough,
                            terrainBlocked
                                ? map.GetLandElevationClass(x, y)
                                : LandElevationClass.Land,
                            x,
                            y);

                        proceduralOverlayTilemap.SetTile(cell, overlayTile);
                        proceduralOverlayTilemap.SetTileFlags(cell, TileFlags.None);
                        proceduralOverlayTilemap.SetColor(cell, Color.white);
                    }

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

        private void OnDestroy()
        {
            ClearGeneratedTextureTiles();
            ClearGeneratedProceduralOverlayTiles();
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

        private TileBase GetWaterTile(WaterDepthClass depthClass, bool rough, int x, int y)
        {
            Sprite[] sprites = GetWaterTextureSprites(depthClass, rough);
            TileBase fallbackTile = GetFallbackWaterTile(depthClass, rough);
            return PickSpriteTile(
                sprites,
                fallbackTile,
                x,
                y,
                GetWaterTextureSalt(depthClass, rough));
        }

        private Sprite[] GetWaterTextureSprites(WaterDepthClass depthClass, bool rough)
        {
            if (rough && HasAnySprite(roughWaterTextureSprites))
            {
                return roughWaterTextureSprites;
            }

            switch (depthClass)
            {
                case WaterDepthClass.Shallow:
                    return shallowWaterTextureSprites;
                case WaterDepthClass.Coastal:
                    return coastalWaterTextureSprites;
                case WaterDepthClass.VeryDeep:
                    return veryDeepWaterTextureSprites;
                case WaterDepthClass.Abyssal:
                    return abyssalWaterTextureSprites;
                case WaterDepthClass.Deep:
                default:
                    return HasAnySprite(deepWaterTextureSprites)
                        ? deepWaterTextureSprites
                        : oceanTextureSprites;
            }
        }

        private TileBase GetFallbackWaterTile(WaterDepthClass depthClass, bool rough)
        {
            if (rough && roughWaterTile != null)
            {
                return roughWaterTile;
            }

            return depthClass == WaterDepthClass.Shallow && shallowWaterTile != null
                ? shallowWaterTile
                : deepWaterTile;
        }

        private Sprite[] GetLandTextureSprites(LandElevationClass elevationClass)
        {
            switch (elevationClass)
            {
                case LandElevationClass.Hill:
                    return hillTextureSprites;
                case LandElevationClass.LargeHill:
                    return largeHillTextureSprites;
                case LandElevationClass.Mountain:
                    return mountainTextureSprites;
                case LandElevationClass.Peak:
                    return peakTextureSprites;
                default:
                    return landTextureSprites;
            }
        }

        private TileBase PickSpriteTile(Sprite[] sprites, TileBase fallbackTile, int x, int y, int salt)
        {
            if (sprites == null || sprites.Length == 0)
            {
                return fallbackTile;
            }

            int startIndex = GetStableVariantIndex(x, y, sprites.Length, salt);
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite sprite = sprites[(startIndex + i) % sprites.Length];
                if (sprite == null)
                {
                    continue;
                }

                TileBase generatedTile = GetGeneratedTextureTile(sprite);
                if (generatedTile != null)
                {
                    return generatedTile;
                }
            }

            return fallbackTile;
        }

        private static bool HasAnySprite(Sprite[] sprites)
        {
            if (sprites == null)
            {
                return false;
            }

            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetWaterTextureSalt(WaterDepthClass depthClass, bool rough)
        {
            if (rough)
            {
                return 131;
            }

            switch (depthClass)
            {
                case WaterDepthClass.Shallow:
                    return 17;
                case WaterDepthClass.Coastal:
                    return 31;
                case WaterDepthClass.VeryDeep:
                    return 53;
                case WaterDepthClass.Abyssal:
                    return 61;
                default:
                    return 73;
            }
        }

        private static int GetLandTextureSalt(LandElevationClass elevationClass)
        {
            switch (elevationClass)
            {
                case LandElevationClass.Hill:
                    return 89;
                case LandElevationClass.LargeHill:
                    return 97;
                case LandElevationClass.Mountain:
                    return 109;
                case LandElevationClass.Peak:
                    return 113;
                default:
                    return 83;
            }
        }

        private TileBase GetGeneratedTextureTile(Sprite sprite)
        {
            if (sprite == null)
            {
                return null;
            }

            generatedTextureTiles ??= new Dictionary<Sprite, TileBase>();

            if (generatedTextureTiles.TryGetValue(sprite, out TileBase cachedTile) &&
                cachedTile != null)
            {
                return cachedTile;
            }

            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = $"{sprite.name}_RuntimeTile";
            tile.hideFlags = HideFlags.HideAndDontSave;
            tile.sprite = sprite;
            tile.color = Color.white;
            tile.transform = GetCenteredSpriteTransform(sprite);
            tile.colliderType = Tile.ColliderType.None;

            generatedTextureTiles[sprite] = tile;
            return tile;
        }

        private static int GetStableVariantIndex(int x, int y, int count, int salt)
        {
            if (count <= 1)
            {
                return 0;
            }

            unchecked
            {
                int hash = x * 73856093 ^ y * 19349663 ^ salt * 83492791;
                return (hash & int.MaxValue) % count;
            }
        }

        private static Matrix4x4 GetCenteredSpriteTransform(Sprite sprite)
        {
            Vector2 centerPivot = new Vector2(sprite.rect.width * 0.5f, sprite.rect.height * 0.5f);
            Vector2 pivotOffset = (sprite.pivot - centerPivot) / sprite.pixelsPerUnit;
            return Matrix4x4.TRS(pivotOffset, Quaternion.identity, Vector3.one);
        }

        private void ClearGeneratedTextureTiles()
        {
            if (generatedTextureTiles == null)
            {
                return;
            }

            foreach (TileBase tile in generatedTextureTiles.Values)
            {
                if (tile == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(tile);
                }
                else
                {
                    DestroyImmediate(tile);
                }
            }

            generatedTextureTiles.Clear();
        }

        private void EnsureProceduralOverlayTilemap()
        {
            if (tilemap == null)
            {
                return;
            }

            if (proceduralOverlayTilemap != null)
            {
                ConfigureProceduralOverlayRenderer();
                return;
            }

            Transform overlayParent = tilemap.transform.parent != null
                ? tilemap.transform.parent
                : transform;
            Transform existing = overlayParent.Find("ProceduralTextureOverlay");

            GameObject overlayObject = existing != null
                ? existing.gameObject
                : new GameObject("ProceduralTextureOverlay");

            overlayObject.transform.SetParent(overlayParent, false);
            overlayObject.transform.localPosition = tilemap.transform.localPosition;
            overlayObject.transform.localRotation = tilemap.transform.localRotation;
            overlayObject.transform.localScale = tilemap.transform.localScale;

            proceduralOverlayTilemap = overlayObject.GetComponent<Tilemap>();
            if (proceduralOverlayTilemap == null)
            {
                proceduralOverlayTilemap = overlayObject.AddComponent<Tilemap>();
            }

            proceduralOverlayTilemap.tileAnchor = tilemap.tileAnchor;
            proceduralOverlayTilemap.orientation = tilemap.orientation;
            proceduralOverlayTilemap.orientationMatrix = tilemap.orientationMatrix;

            if (overlayObject.GetComponent<TilemapRenderer>() == null)
            {
                overlayObject.AddComponent<TilemapRenderer>();
            }

            ConfigureProceduralOverlayRenderer();
        }

        private void ConfigureProceduralOverlayRenderer()
        {
            if (proceduralOverlayTilemap == null || tilemap == null)
            {
                return;
            }

            TilemapRenderer overlayRenderer =
                proceduralOverlayTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer baseRenderer = tilemap.GetComponent<TilemapRenderer>();

            if (overlayRenderer == null)
            {
                return;
            }

            if (baseRenderer != null)
            {
                overlayRenderer.sortingLayerID = baseRenderer.sortingLayerID;
                overlayRenderer.sortingOrder =
                    baseRenderer.sortingOrder + proceduralOverlaySortingOrderOffset;
                overlayRenderer.mode = baseRenderer.mode;
            }
        }

        private TileBase GetProceduralOverlayTile(
            bool terrainBlocked,
            WaterDepthClass depthClass,
            bool rough,
            LandElevationClass elevationClass,
            int x,
            int y)
        {
            ProceduralOverlayKind kind = terrainBlocked
                ? GetLandOverlayKind(elevationClass)
                : GetWaterOverlayKind(depthClass, rough);

            int variantCount = Mathf.Clamp(proceduralTextureVariants, 1, 16);
            int variant = GetStableVariantIndex(
                x,
                y,
                variantCount,
                GetProceduralOverlaySalt(kind));
            int cacheKey = GetProceduralOverlayCacheKey(kind, variant);

            if (generatedProceduralOverlayTiles.TryGetValue(
                    cacheKey,
                    out TileBase cachedTile) &&
                cachedTile != null)
            {
                return cachedTile;
            }

            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = $"{kind}_Overlay_{variant + 1:00}";
            tile.hideFlags = HideFlags.HideAndDontSave;
            tile.sprite = CreateProceduralOverlaySprite(kind, variant);
            tile.color = Color.white;
            tile.colliderType = Tile.ColliderType.None;

            generatedProceduralOverlayTiles[cacheKey] = tile;
            generatedProceduralOverlayResources.Add(tile);
            return tile;
        }

        private Sprite CreateProceduralOverlaySprite(ProceduralOverlayKind kind, int variant)
        {
            int size = GetProceduralTextureSize();
            Texture2D texture = CreateProceduralOverlayTexture(kind, variant, size);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size,
                0u,
                SpriteMeshType.FullRect);

            sprite.name = $"{kind}_OverlaySprite_{variant + 1:00}";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            generatedProceduralOverlayResources.Add(sprite);
            return sprite;
        }

        private Texture2D CreateProceduralOverlayTexture(
            ProceduralOverlayKind kind,
            int variant,
            int size)
        {
            Color32[] pixels = new Color32[size * size];
            OverlayPalette palette = GetOverlayPalette(kind);

            DrawOverlayNoise(pixels, size, kind, variant, palette);

            switch (kind)
            {
                case ProceduralOverlayKind.RoughWater:
                    DrawWaterStreaks(pixels, size, kind, variant, palette, true);
                    DrawRoughWaterCaps(pixels, size, variant, palette);
                    break;
                case ProceduralOverlayKind.ShallowWater:
                case ProceduralOverlayKind.CoastalWater:
                case ProceduralOverlayKind.DeepWater:
                case ProceduralOverlayKind.VeryDeepWater:
                case ProceduralOverlayKind.AbyssalWater:
                    DrawWaterStreaks(pixels, size, kind, variant, palette, false);
                    break;
                case ProceduralOverlayKind.Hill:
                case ProceduralOverlayKind.LargeHill:
                    DrawLandFlecks(pixels, size, kind, variant, palette);
                    DrawContourBands(pixels, size, kind, variant, palette);
                    break;
                case ProceduralOverlayKind.Mountain:
                case ProceduralOverlayKind.Peak:
                    DrawLandFlecks(pixels, size, kind, variant, palette);
                    DrawRidgeMarks(pixels, size, kind, variant, palette);
                    break;
                default:
                    DrawLandFlecks(pixels, size, kind, variant, palette);
                    break;
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = $"{kind}_OverlayTexture_{variant + 1:00}_{size}";
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            generatedProceduralOverlayResources.Add(texture);
            return texture;
        }

        private void DrawOverlayNoise(
            Color32[] pixels,
            int size,
            ProceduralOverlayKind kind,
            int variant,
            OverlayPalette palette)
        {
            int density = IsWaterOverlayKind(kind) ? 7 : 13;
            if (kind == ProceduralOverlayKind.AbyssalWater)
            {
                density = 4;
            }
            else if (kind == ProceduralOverlayKind.RoughWater)
            {
                density = 10;
            }

            int salt = GetProceduralOverlaySalt(kind) + variant * 41;
            for (int y = 0; y < size; y += 2)
            {
                for (int x = 0; x < size; x += 2)
                {
                    uint hash = HashPixel(x, y, salt);
                    if (hash % 100u >= density)
                    {
                        continue;
                    }

                    Color32 color = (hash & 1u) == 0u ? palette.Dark : palette.Light;
                    SetPixelBlock(pixels, size, x, y, 1, 1, color);
                }
            }
        }

        private void DrawWaterStreaks(
            Color32[] pixels,
            int size,
            ProceduralOverlayKind kind,
            int variant,
            OverlayPalette palette,
            bool choppy)
        {
            int streakCount = choppy ? 22 : 12;
            if (kind == ProceduralOverlayKind.AbyssalWater)
            {
                streakCount = 6;
            }

            int salt = GetProceduralOverlaySalt(kind) + variant * 97;
            for (int i = 0; i < streakCount; i++)
            {
                uint hash = HashPixel(i, variant, salt);
                int y = 12 + (int)(hash % (uint)(size - 24));
                int x = 8 + (int)((hash >> 7) % (uint)(size - 28));
                int length = choppy
                    ? 5 + (int)((hash >> 14) % 13u)
                    : 6 + (int)((hash >> 14) % 18u);

                Color32 color = (hash & 4u) == 0u ? palette.Light : palette.Dark;
                for (int step = 0; step < length; step++)
                {
                    int wobble = (int)((HashPixel(step, i, salt) >> 5) % 3u) - 1;
                    int px = x + step;
                    int py = y + (choppy ? wobble : wobble / 2);
                    SetPixelBlock(pixels, size, px, py, 1, 1, color);

                    if (!choppy && step % 4 == 0)
                    {
                        SetPixelBlock(pixels, size, px, py + 1, 1, 1, palette.Dark);
                    }
                }
            }
        }

        private void DrawRoughWaterCaps(
            Color32[] pixels,
            int size,
            int variant,
            OverlayPalette palette)
        {
            int salt = GetProceduralOverlaySalt(ProceduralOverlayKind.RoughWater) +
                       variant * 127;

            for (int i = 0; i < 18; i++)
            {
                uint hash = HashPixel(i, variant, salt);
                int x = 8 + (int)(hash % (uint)(size - 20));
                int y = 10 + (int)((hash >> 9) % (uint)(size - 20));
                int length = 4 + (int)((hash >> 18) % 8u);

                for (int step = 0; step < length; step++)
                {
                    SetPixelBlock(
                        pixels,
                        size,
                        x + step,
                        y - step / 2,
                        1,
                        1,
                        palette.Accent);
                }
            }
        }

        private void DrawLandFlecks(
            Color32[] pixels,
            int size,
            ProceduralOverlayKind kind,
            int variant,
            OverlayPalette palette)
        {
            int salt = GetProceduralOverlaySalt(kind) + variant * 71;
            int count = kind == ProceduralOverlayKind.Land ? 42 : 56;

            for (int i = 0; i < count; i++)
            {
                uint hash = HashPixel(i, variant, salt);
                int x = 6 + (int)(hash % (uint)(size - 12));
                int y = 8 + (int)((hash >> 8) % (uint)(size - 16));
                int width = 1 + (int)((hash >> 16) % 3u);
                int height = 1 + (int)((hash >> 20) % 2u);
                Color32 color = (hash & 2u) == 0u ? palette.Dark : palette.Light;

                SetPixelBlock(pixels, size, x, y, width, height, color);
            }
        }

        private void DrawContourBands(
            Color32[] pixels,
            int size,
            ProceduralOverlayKind kind,
            int variant,
            OverlayPalette palette)
        {
            int salt = GetProceduralOverlaySalt(kind) + variant * 53;
            int bands = kind == ProceduralOverlayKind.LargeHill ? 7 : 5;

            for (int i = 0; i < bands; i++)
            {
                uint hash = HashPixel(i, variant, salt);
                int y = 22 + i * 12 + (int)(hash % 5u) - 2;
                int x = 12 + (int)((hash >> 6) % 18u);
                int length = 36 + (int)((hash >> 12) % 42u);
                Color32 color = (i & 1) == 0 ? palette.Dark : palette.Light;

                for (int step = 0; step < length; step++)
                {
                    int py = y + ((step / 7) & 1);
                    SetPixelBlock(pixels, size, x + step, py, 1, 1, color);
                }
            }
        }

        private void DrawRidgeMarks(
            Color32[] pixels,
            int size,
            ProceduralOverlayKind kind,
            int variant,
            OverlayPalette palette)
        {
            int salt = GetProceduralOverlaySalt(kind) + variant * 37;
            int ridges = kind == ProceduralOverlayKind.Peak ? 4 : 3;

            for (int i = 0; i < ridges; i++)
            {
                uint hash = HashPixel(i, variant, salt);
                int startX = 20 + (int)(hash % 44u);
                int startY = 18 + (int)((hash >> 8) % 20u);
                int length = 28 + (int)((hash >> 16) % 22u);
                Color32 color = i == 0 && kind == ProceduralOverlayKind.Peak
                    ? palette.Accent
                    : palette.Dark;

                for (int step = 0; step < length; step++)
                {
                    int px = startX + step / 2;
                    int py = startY + step;
                    SetPixelBlock(pixels, size, px, py, 1, 1, color);

                    if (step % 5 == 0)
                    {
                        SetPixelBlock(pixels, size, px + 1, py, 1, 1, palette.Light);
                    }
                }
            }
        }

        private static void SetPixelBlock(
            Color32[] pixels,
            int size,
            int x,
            int y,
            int width,
            int height,
            Color32 color)
        {
            for (int yy = 0; yy < height; yy++)
            {
                int py = y + yy;
                for (int xx = 0; xx < width; xx++)
                {
                    int px = x + xx;
                    if (!IsInsideProceduralHex(px, py, size))
                    {
                        continue;
                    }

                    pixels[py * size + px] = color;
                }
            }
        }

        private static bool IsInsideProceduralHex(int x, int y, int size)
        {
            if (x < 0 || y < 0 || x >= size || y >= size)
            {
                return false;
            }

            float center = (size - 1) * 0.5f;
            float halfHeight = size * 0.44140625f;
            float dy = Mathf.Abs(y - center);
            if (dy > halfHeight)
            {
                return false;
            }

            float edgeInset = (dy / halfHeight) * size * 0.25f;
            return x >= edgeInset && x <= size - 1 - edgeInset;
        }

        private OverlayPalette GetOverlayPalette(ProceduralOverlayKind kind)
        {
            float opacity = Mathf.Clamp01(proceduralTextureOpacity);
            switch (kind)
            {
                case ProceduralOverlayKind.ShallowWater:
                    return new OverlayPalette(
                        WithAlpha(12, 91, 112, 54, opacity),
                        WithAlpha(135, 231, 226, 76, opacity),
                        WithAlpha(201, 184, 112, 58, opacity));
                case ProceduralOverlayKind.CoastalWater:
                    return new OverlayPalette(
                        WithAlpha(7, 62, 91, 58, opacity),
                        WithAlpha(96, 202, 199, 72, opacity),
                        WithAlpha(170, 160, 92, 48, opacity));
                case ProceduralOverlayKind.VeryDeepWater:
                    return new OverlayPalette(
                        WithAlpha(3, 13, 44, 64, opacity),
                        WithAlpha(54, 106, 177, 56, opacity),
                        WithAlpha(79, 148, 210, 42, opacity));
                case ProceduralOverlayKind.AbyssalWater:
                    return new OverlayPalette(
                        WithAlpha(1, 5, 20, 70, opacity),
                        WithAlpha(36, 76, 140, 38, opacity),
                        WithAlpha(50, 92, 160, 34, opacity));
                case ProceduralOverlayKind.RoughWater:
                    return new OverlayPalette(
                        WithAlpha(13, 48, 81, 68, opacity),
                        WithAlpha(128, 198, 216, 84, opacity),
                        WithAlpha(224, 241, 232, 150, opacity));
                case ProceduralOverlayKind.Land:
                    return new OverlayPalette(
                        WithAlpha(34, 69, 33, 72, opacity),
                        WithAlpha(130, 161, 77, 58, opacity),
                        WithAlpha(107, 79, 42, 64, opacity));
                case ProceduralOverlayKind.Hill:
                    return new OverlayPalette(
                        WithAlpha(50, 70, 36, 78, opacity),
                        WithAlpha(156, 158, 83, 60, opacity),
                        WithAlpha(118, 84, 43, 64, opacity));
                case ProceduralOverlayKind.LargeHill:
                    return new OverlayPalette(
                        WithAlpha(75, 59, 39, 82, opacity),
                        WithAlpha(177, 147, 82, 66, opacity),
                        WithAlpha(96, 68, 43, 70, opacity));
                case ProceduralOverlayKind.Mountain:
                    return new OverlayPalette(
                        WithAlpha(34, 36, 40, 96, opacity),
                        WithAlpha(154, 148, 132, 64, opacity),
                        WithAlpha(89, 72, 55, 70, opacity));
                case ProceduralOverlayKind.Peak:
                    return new OverlayPalette(
                        WithAlpha(20, 23, 28, 104, opacity),
                        WithAlpha(120, 125, 128, 70, opacity),
                        WithAlpha(222, 229, 221, 120, opacity));
                case ProceduralOverlayKind.DeepWater:
                default:
                    return new OverlayPalette(
                        WithAlpha(4, 24, 69, 62, opacity),
                        WithAlpha(75, 144, 205, 64, opacity),
                        WithAlpha(100, 176, 228, 46, opacity));
            }
        }

        private static Color32 WithAlpha(
            byte r,
            byte g,
            byte b,
            byte alpha,
            float opacity)
        {
            return new Color32(r, g, b, (byte)Mathf.RoundToInt(alpha * opacity));
        }

        private static uint HashPixel(int x, int y, int salt)
        {
            unchecked
            {
                uint hash = (uint)(x * 374761393 + y * 668265263 + salt * 362437);
                hash = (hash ^ (hash >> 13)) * 1274126177u;
                return hash ^ (hash >> 16);
            }
        }

        private static bool IsWaterOverlayKind(ProceduralOverlayKind kind)
        {
            return kind == ProceduralOverlayKind.ShallowWater ||
                   kind == ProceduralOverlayKind.CoastalWater ||
                   kind == ProceduralOverlayKind.DeepWater ||
                   kind == ProceduralOverlayKind.VeryDeepWater ||
                   kind == ProceduralOverlayKind.AbyssalWater ||
                   kind == ProceduralOverlayKind.RoughWater;
        }

        private static ProceduralOverlayKind GetWaterOverlayKind(
            WaterDepthClass depthClass,
            bool rough)
        {
            if (rough)
            {
                return ProceduralOverlayKind.RoughWater;
            }

            switch (depthClass)
            {
                case WaterDepthClass.Shallow:
                    return ProceduralOverlayKind.ShallowWater;
                case WaterDepthClass.Coastal:
                    return ProceduralOverlayKind.CoastalWater;
                case WaterDepthClass.VeryDeep:
                    return ProceduralOverlayKind.VeryDeepWater;
                case WaterDepthClass.Abyssal:
                    return ProceduralOverlayKind.AbyssalWater;
                default:
                    return ProceduralOverlayKind.DeepWater;
            }
        }

        private static ProceduralOverlayKind GetLandOverlayKind(
            LandElevationClass elevationClass)
        {
            switch (elevationClass)
            {
                case LandElevationClass.Hill:
                    return ProceduralOverlayKind.Hill;
                case LandElevationClass.LargeHill:
                    return ProceduralOverlayKind.LargeHill;
                case LandElevationClass.Mountain:
                    return ProceduralOverlayKind.Mountain;
                case LandElevationClass.Peak:
                    return ProceduralOverlayKind.Peak;
                default:
                    return ProceduralOverlayKind.Land;
            }
        }

        private static int GetProceduralOverlaySalt(ProceduralOverlayKind kind)
        {
            return 1009 + (int)kind * 131;
        }

        private int GetProceduralOverlayCacheKey(
            ProceduralOverlayKind kind,
            int variant)
        {
            int size = GetProceduralTextureSize();
            int opacityKey =
                Mathf.RoundToInt(Mathf.Clamp01(proceduralTextureOpacity) * 255f);

            return ((int)kind << 24) ^
                   (size << 12) ^
                   (opacityKey << 4) ^
                   variant;
        }

        private int GetProceduralTextureSize()
        {
            return Mathf.Clamp(proceduralTextureSize, 16, 256);
        }

        private void ClearGeneratedProceduralOverlayTiles()
        {
            for (int i = 0; i < generatedProceduralOverlayResources.Count; i++)
            {
                UnityEngine.Object resource = generatedProceduralOverlayResources[i];
                if (resource == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(resource);
                }
                else
                {
                    DestroyImmediate(resource);
                }
            }

            generatedProceduralOverlayTiles.Clear();
            generatedProceduralOverlayResources.Clear();
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

        private enum ProceduralOverlayKind
        {
            ShallowWater = 0,
            CoastalWater = 1,
            DeepWater = 2,
            VeryDeepWater = 3,
            AbyssalWater = 4,
            RoughWater = 5,
            Land = 6,
            Hill = 7,
            LargeHill = 8,
            Mountain = 9,
            Peak = 10
        }

        private struct OverlayPalette
        {
            public readonly Color32 Dark;
            public readonly Color32 Light;
            public readonly Color32 Accent;

            public OverlayPalette(Color32 dark, Color32 light, Color32 accent)
            {
                Dark = dark;
                Light = light;
                Accent = accent;
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
