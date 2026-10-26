using OA.Presentation.Debug;
using OA.Simulation.Movement;
using OA.Simulation.Navigation;
using OA.Simulation.Units;
using UnityEngine;

namespace OA.Presentation.Units
{
    [RequireComponent(typeof(ShipNavigationAgent))]
    public sealed class SelectableShip : MonoBehaviour
    {
        [SerializeField] private UnitArchetypeDefinition archetype;
        [SerializeField] private ShipNavigationAgent agent;
        [SerializeField] private PathfindingSandboxController planner;
        [SerializeField] private SpriteRenderer hull;

        private LineRenderer ring;
        private Material lineMaterial;
        private Camera worldCamera;
        public bool IsSelected { get; private set; }
        public UnitArchetypeDefinition Archetype => archetype;
        public ShipNavigationAgent Agent => agent;
        public PathfindingSandboxController Planner => planner;
        public SpriteRenderer Hull => hull;
        public float SeparationRadius => archetype != null && archetype.movementProfile != null
            ? archetype.movementProfile.HalfLengthWorldUnits + 1.5f : 3f;

        public static SelectableShip Create(UnitArchetypeDefinition definition, Transform parent)
        {
            var root = new GameObject(definition.displayName);
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            var visual = new GameObject("Hull");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            var sprite = visual.AddComponent<SpriteRenderer>();
            sprite.sprite = definition.presentationProfile.worldSprite;
            sprite.color = definition.presentationProfile.tint;
            sprite.sortingOrder = 10;
            if (sprite.sprite != null)
            {
                Vector2 size = sprite.sprite.bounds.size;
                float scale = definition.movementProfile.LengthWorldUnits / Mathf.Max(size.x, size.y);
                visual.transform.localScale = Vector3.one * scale;
            }
            var navigation = root.AddComponent<ShipNavigationAgent>();
            var routes = root.AddComponent<PathfindingSandboxController>();
            routes.PrepareExternalControl();
            var unit = root.AddComponent<SelectableShip>();
            unit.archetype = definition;
            unit.agent = navigation;
            unit.planner = routes;
            unit.hull = sprite;
            // Also serializes the assigned archetype for scene-placed ships.
            navigation.Initialize(definition, Vector2.zero);
            root.SetActive(true);
            return unit;
        }

        public void Initialize(HexMapRuntime map, INavigationPathService service,
            INavigationGridPresenter presenter, Camera camera, Vector2 position)
        {
            worldCamera = camera;
            agent.Initialize(archetype, position);
            agent.SetSteeringDistances(Mathf.Max(8f, archetype.movementProfile.LengthWorldUnits * 2f),
                Mathf.Max(0.9f, archetype.movementProfile.HalfLengthWorldUnits * 0.3f));
            agent.SetNavigationMap(map);
            if (lineMaterial == null)
                lineMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Ship selection and route" };

            if (ring == null)
            {
                var ringObject = new GameObject("Selection Ring");
                ringObject.transform.SetParent(transform, false);
                ring = ringObject.AddComponent<LineRenderer>();
                ring.sharedMaterial = lineMaterial;
                ring.useWorldSpace = true;
                ring.loop = true;
                ring.positionCount = 48;
                ring.sortingOrder = 40;
                ring.startColor = ring.endColor = new Color(0.35f, 1f, 0.7f);
            }

            var styleObject = new GameObject("Route Style");
            styleObject.transform.SetParent(transform, false);
            styleObject.SetActive(false);
            var style = styleObject.AddComponent<LineRenderer>();
            style.sharedMaterial = lineMaterial;
            style.sortingOrder = 5;
            var routeLine = gameObject.AddComponent<RouteLineRenderer>();
            routeLine.Configure(style, camera, 2.5f);
            planner.ConfigureExternal(map, service, presenter, agent, camera, routeLine);
            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (ring != null) ring.enabled = selected;
            planner?.SetSelectionVisible(selected);
        }

        public float ScreenPickDistance(Camera camera, Vector2 pointer)
        {
            Vector2 direction = MovementMath.HeadingDegreesToVector(agent.CurrentMovementState.HeadingDegrees);
            float halfLength = archetype.movementProfile.HalfLengthWorldUnits;
            Vector2 position = transform.position;
            Vector2 a = camera.WorldToScreenPoint(position - direction * halfLength);
            Vector2 b = camera.WorldToScreenPoint(position + direction * halfLength);
            Vector2 segment = b - a;
            float t = segment.sqrMagnitude > 0.001f
                ? Mathf.Clamp01(Vector2.Dot(pointer - a, segment) / segment.sqrMagnitude) : 0f;
            return Vector2.Distance(pointer, a + segment * t);
        }

        private void LateUpdate()
        {
            if (!IsSelected || ring == null || worldCamera == null) return;
            float worldPerPixel = 2f * worldCamera.orthographicSize / Mathf.Max(1, worldCamera.pixelHeight);
            float radius = Mathf.Max(SeparationRadius, 13f * worldPerPixel);
            ring.startWidth = ring.endWidth = Mathf.Max(0.03f, 1.5f * worldPerPixel);
            Vector3 center = transform.position;
            for (int i = 0; i < ring.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / ring.positionCount;
                ring.SetPosition(i, center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }
        }

        private void OnDestroy()
        {
            if (lineMaterial == null) return;
            if (Application.isPlaying) Destroy(lineMaterial);
            else DestroyImmediate(lineMaterial);
        }
    }
}
