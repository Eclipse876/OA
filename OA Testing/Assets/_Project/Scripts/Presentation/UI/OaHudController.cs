using UnityEngine;
using UnityEngine.UIElements;
using OA.Presentation.Debug;
using OA.Simulation.Navigation;

namespace OA.Presentation.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class OaHudController : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private PathfindingSandboxController sandboxController;
        [SerializeField] private Camera worldCamera;

        [Header("Options")]
        [SerializeField] private bool hideLegacyHoverReadout = true;

        private UIDocument document;
        private MiniMapView miniMapView;
        private TileReadoutView tileReadoutView;

        private Label mapStatusLabel;
        private Label cameraStatusLabel;
        private Label routeStatusLabel;
        private Label miniMapStatusLabel;
        private Label hudStatusLabel;

        private Button rerollMapButton;
        private Button rebuildNavigationButton;
        private Button toggleSpeedButton;

        private readonly System.Collections.Generic.List<VisualElement> blockingElements =
            new System.Collections.Generic.List<VisualElement>(8);

        private void Awake()
        {
            document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            ResolveSceneReferences();
            BindDocument();
        }

        private void OnDisable()
        {
            if (rerollMapButton != null)
            {
                rerollMapButton.clicked -= HandleRerollMapClicked;
            }

            if (rebuildNavigationButton != null)
            {
                rebuildNavigationButton.clicked -= HandleRebuildNavigationClicked;
            }

            if (toggleSpeedButton != null)
            {
                toggleSpeedButton.clicked -= HandleToggleSpeedClicked;
            }

            for (int i = 0; i < blockingElements.Count; i++)
            {
                UiInputBlocker.UnregisterBlockingElement(blockingElements[i]);
            }

            blockingElements.Clear();
        }

        private void Update()
        {
            ResolveSceneReferences();

            HexMapRuntime map = sandboxController != null
                ? sandboxController.CurrentMap
                : null;

            miniMapView?.RefreshMap(map);
            miniMapView?.RefreshCamera(worldCamera);
            tileReadoutView?.Refresh(map, worldCamera);

            UpdateStatusLabels(map);
        }

        private void ResolveSceneReferences()
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            if (sandboxController == null)
            {
                sandboxController = FindFirstObjectByType<PathfindingSandboxController>();
            }

            if (hideLegacyHoverReadout && sandboxController != null)
            {
                sandboxController.SetLegacyHoverTileReadoutVisible(false);
            }
        }

        private void BindDocument()
        {
            if (document == null || document.rootVisualElement == null)
            {
                return;
            }

            VisualElement root = document.rootVisualElement.Q<VisualElement>("oa-hud-root");
            if (root == null)
            {
                UnityEngine.Debug.LogWarning("[OaHudController] OaHud.uxml is missing oa-hud-root.");
                return;
            }

            root.pickingMode = PickingMode.Ignore;

            mapStatusLabel = root.Q<Label>("mapStatusLabel");
            cameraStatusLabel = root.Q<Label>("cameraStatusLabel");
            routeStatusLabel = root.Q<Label>("routeStatusLabel");
            miniMapStatusLabel = root.Q<Label>("miniMapStatusLabel");
            hudStatusLabel = root.Q<Label>("hudStatusLabel");

            rerollMapButton = root.Q<Button>("rerollMapButton");
            rebuildNavigationButton = root.Q<Button>("rebuildNavigationButton");
            toggleSpeedButton = root.Q<Button>("toggleSpeedButton");

            if (rerollMapButton != null)
            {
                rerollMapButton.clicked += HandleRerollMapClicked;
            }

            if (rebuildNavigationButton != null)
            {
                rebuildNavigationButton.clicked += HandleRebuildNavigationClicked;
            }

            if (toggleSpeedButton != null)
            {
                toggleSpeedButton.clicked += HandleToggleSpeedClicked;
            }

            VisualElement miniMapRoot = root.Q<VisualElement>("mini-map");
            miniMapView = miniMapRoot != null ? new MiniMapView(miniMapRoot) : null;
            tileReadoutView = new TileReadoutView(root);

            RegisterBlockingElements(root);
        }

        private void RegisterBlockingElements(VisualElement root)
        {
            root.Query<VisualElement>().ForEach(element =>
            {
                if (!element.ClassListContains("ui-blocker"))
                {
                    return;
                }

                element.pickingMode = PickingMode.Position;
                blockingElements.Add(element);
                UiInputBlocker.RegisterBlockingElement(element);
            });
        }

        private void UpdateStatusLabels(HexMapRuntime map)
        {
            if (mapStatusLabel != null)
            {
                mapStatusLabel.text = map != null
                    ? $"{map.Width} x {map.Height} | v{map.Version}"
                    : "Waiting for map";
            }

            if (cameraStatusLabel != null)
            {
                cameraStatusLabel.text = worldCamera != null
                    ? $"{worldCamera.transform.position.x:0.0}, {worldCamera.transform.position.y:0.0} | zoom {worldCamera.orthographicSize:0.0}"
                    : "Not linked";
            }

            if (routeStatusLabel != null)
            {
                routeStatusLabel.text = sandboxController != null
                    ? "Sandbox linked"
                    : "No controller";
            }

            if (miniMapStatusLabel != null)
            {
                miniMapStatusLabel.text = map != null && worldCamera != null
                    ? "online"
                    : "offline";
            }

            if (hudStatusLabel != null)
            {
                hudStatusLabel.text = sandboxController != null
                    ? "HUD linked to pathfinding sandbox"
                    : "HUD waiting for scene controller";
            }
        }

        private void HandleRerollMapClicked()
        {
            sandboxController?.GenerateRuntimeDebugMap();
        }

        private void HandleRebuildNavigationClicked()
        {
            sandboxController?.RebuildFromMapDefinition();
        }

        private void HandleToggleSpeedClicked()
        {
            sandboxController?.RequestSpeedModeToggle();
        }
    }

    public sealed class MiniMapView
    {
        private readonly VisualElement root;
        private readonly VisualElement cameraFrame;

        private Texture2D mapTexture;
        private HexMapRuntime currentMap;
        private int currentMapVersion = int.MinValue;
        private Rect worldBounds;
        private bool hasWorldBounds;

        public MiniMapView(VisualElement root)
        {
            this.root = root;

            if (root == null)
            {
                return;
            }

            root.pickingMode = PickingMode.Position;
            root.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;

            cameraFrame = new VisualElement
            {
                name = "mini-map-camera-frame",
                pickingMode = PickingMode.Ignore
            };
            cameraFrame.AddToClassList("oa-mini-map-frame");
            cameraFrame.style.display = DisplayStyle.None;
            root.Add(cameraFrame);
        }

        public void RefreshMap(HexMapRuntime map)
        {
            if (root == null)
            {
                return;
            }

            if (map == null)
            {
                Clear();
                return;
            }

            if (ReferenceEquals(map, currentMap) &&
                currentMapVersion == map.Version)
            {
                return;
            }

            currentMap = map;
            currentMapVersion = map.Version;
            worldBounds = CalculateWorldBounds(map);
            hasWorldBounds = worldBounds.width > 0.001f &&
                             worldBounds.height > 0.001f;

            RebuildTexture(map);
        }

        public void RefreshCamera(Camera camera)
        {
            if (root == null ||
                cameraFrame == null ||
                camera == null ||
                currentMap == null ||
                !hasWorldBounds)
            {
                SetFrameVisible(false);
                return;
            }

            Rect cameraBounds = CalculateCameraBounds(camera);
            Rect renderedMapRect = CalculateRenderedMapRect();

            if (renderedMapRect.width <= 0.001f ||
                renderedMapRect.height <= 0.001f)
            {
                SetFrameVisible(false);
                return;
            }

            float normalizedLeft = Mathf.InverseLerp(
                worldBounds.xMin,
                worldBounds.xMax,
                cameraBounds.xMin);

            float normalizedRight = Mathf.InverseLerp(
                worldBounds.xMin,
                worldBounds.xMax,
                cameraBounds.xMax);

            float normalizedTop = Mathf.InverseLerp(
                worldBounds.yMax,
                worldBounds.yMin,
                cameraBounds.yMax);

            float normalizedBottom = Mathf.InverseLerp(
                worldBounds.yMax,
                worldBounds.yMin,
                cameraBounds.yMin);

            float left = Mathf.Clamp01(normalizedLeft);
            float right = Mathf.Clamp01(normalizedRight);
            float top = Mathf.Clamp01(normalizedTop);
            float bottom = Mathf.Clamp01(normalizedBottom);

            if (right <= left || bottom <= top)
            {
                SetFrameVisible(false);
                return;
            }

            cameraFrame.style.left = renderedMapRect.x + left * renderedMapRect.width;
            cameraFrame.style.top = renderedMapRect.y + top * renderedMapRect.height;
            cameraFrame.style.width = Mathf.Max(3f, (right - left) * renderedMapRect.width);
            cameraFrame.style.height = Mathf.Max(3f, (bottom - top) * renderedMapRect.height);
            SetFrameVisible(true);
        }

        private void RebuildTexture(HexMapRuntime map)
        {
            if (mapTexture == null ||
                mapTexture.width != map.Width ||
                mapTexture.height != map.Height)
            {
                if (mapTexture != null)
                {
                    Object.Destroy(mapTexture);
                }

                mapTexture = new Texture2D(
                    map.Width,
                    map.Height,
                    TextureFormat.RGBA32,
                    false);

                mapTexture.name = "OA Runtime MiniMap";
                mapTexture.filterMode = FilterMode.Point;
                mapTexture.wrapMode = TextureWrapMode.Clamp;
            }

            Color32[] pixels = new Color32[map.Width * map.Height];

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    pixels[map.GetIndex(x, y)] = GetTileColor(map, x, y);
                }
            }

            mapTexture.SetPixels32(pixels);
            mapTexture.Apply(false, false);
            root.style.backgroundImage = new StyleBackground(mapTexture);
        }

        private static Color32 GetTileColor(HexMapRuntime map, int x, int y)
        {
            if (map.IsBlocked(x, y))
            {
                switch (map.GetLandElevationClass(x, y))
                {
                    case LandElevationClass.Hill:
                        return new Color32(94, 103, 75, 255);
                    case LandElevationClass.LargeHill:
                        return new Color32(115, 109, 78, 255);
                    case LandElevationClass.Mountain:
                        return new Color32(132, 124, 109, 255);
                    case LandElevationClass.Peak:
                        return new Color32(198, 195, 181, 255);
                    default:
                        return new Color32(68, 91, 62, 255);
                }
            }

            switch (map.GetDepthClass(x, y))
            {
                case WaterDepthClass.Shallow:
                    return new Color32(52, 143, 149, 255);
                case WaterDepthClass.Coastal:
                    return new Color32(38, 118, 145, 255);
                case WaterDepthClass.VeryDeep:
                    return new Color32(17, 65, 108, 255);
                case WaterDepthClass.Abyssal:
                    return new Color32(9, 34, 69, 255);
                default:
                    return map.GetMoveCost(x, y) > 1.01f
                        ? new Color32(31, 92, 119, 255)
                        : new Color32(23, 88, 132, 255);
            }
        }

        private Rect CalculateRenderedMapRect()
        {
            Rect content = root.contentRect;
            if (content.width <= 0.001f || content.height <= 0.001f)
            {
                content = root.layout;
            }

            if (content.width <= 0.001f ||
                content.height <= 0.001f ||
                worldBounds.height <= 0.001f)
            {
                return Rect.zero;
            }

            float mapAspect = worldBounds.width / worldBounds.height;
            float contentAspect = content.width / content.height;

            if (contentAspect > mapAspect)
            {
                float width = content.height * mapAspect;
                return new Rect(
                    content.x + (content.width - width) * 0.5f,
                    content.y,
                    width,
                    content.height);
            }

            float height = content.width / mapAspect;
            return new Rect(
                content.x,
                content.y + (content.height - height) * 0.5f,
                content.width,
                height);
        }

        private static Rect CalculateWorldBounds(HexMapRuntime map)
        {
            if (map == null)
            {
                return Rect.zero;
            }

            if (!map.HasWorldCenters)
            {
                return new Rect(
                    0f,
                    0f,
                    map.Width * map.CellSize,
                    map.Height * map.CellSize);
            }

            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    Vector2 center = map.GetWorldCenter(x, y);
                    min = Vector2.Min(min, center);
                    max = Vector2.Max(max, center);
                }
            }

            float padding = Mathf.Max(0.05f, map.CellSize * 0.55f);
            return Rect.MinMaxRect(
                min.x - padding,
                min.y - padding,
                max.x + padding,
                max.y + padding);
        }

        private static Rect CalculateCameraBounds(Camera camera)
        {
            if (camera.orthographic)
            {
                float height = camera.orthographicSize * 2f;
                float width = height * camera.aspect;
                Vector3 position = camera.transform.position;

                return new Rect(
                    position.x - width * 0.5f,
                    position.y - height * 0.5f,
                    width,
                    height);
            }

            float distance = Mathf.Abs(camera.transform.position.z);
            Vector3 bottomLeft = camera.ViewportToWorldPoint(new Vector3(0f, 0f, distance));
            Vector3 topRight = camera.ViewportToWorldPoint(new Vector3(1f, 1f, distance));

            return Rect.MinMaxRect(
                Mathf.Min(bottomLeft.x, topRight.x),
                Mathf.Min(bottomLeft.y, topRight.y),
                Mathf.Max(bottomLeft.x, topRight.x),
                Mathf.Max(bottomLeft.y, topRight.y));
        }

        private void SetFrameVisible(bool visible)
        {
            if (cameraFrame != null)
            {
                cameraFrame.style.display = visible
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
        }

        private void Clear()
        {
            currentMap = null;
            currentMapVersion = int.MinValue;
            hasWorldBounds = false;
            root.style.backgroundImage = StyleKeyword.None;
            SetFrameVisible(false);
        }
    }
}
