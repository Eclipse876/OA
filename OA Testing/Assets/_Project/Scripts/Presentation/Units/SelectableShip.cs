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
        private Sprite pickSprite;
        private Vector2[] pickVertices;
        private ushort[] pickTriangles;
        private Vector2[] projectedPickVertices;
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
            if (camera == null) return float.PositiveInfinity;
            Sprite sprite = hull != null ? hull.sprite : null;
            if (sprite != pickSprite)
            {
                pickSprite = sprite;
                pickVertices = sprite != null ? sprite.vertices : null;
                pickTriangles = sprite != null ? sprite.triangles : null;
                projectedPickVertices = pickVertices != null ? new Vector2[pickVertices.Length] : null;
            }

            // Tight sprite triangles cover the visible beam at close zoom while
            // excluding the transparent padding around the source image. The
            // caller retains its 12-pixel minimum tolerance outside this mesh.
            if (pickVertices != null && pickVertices.Length >= 3 &&
                pickTriangles != null && pickTriangles.Length >= 3)
            {
                for (int i = 0; i < pickVertices.Length; i++)
                {
                    Vector2 vertex = pickVertices[i];
                    if (hull.flipX) vertex.x = -vertex.x;
                    if (hull.flipY) vertex.y = -vertex.y;
                    Vector3 projected = camera.WorldToScreenPoint(hull.transform.TransformPoint(vertex));
                    if (projected.z <= 0f) return float.PositiveInfinity;
                    projectedPickVertices[i] = projected;
                }

                float nearest = float.PositiveInfinity;
                for (int i = 0; i + 2 < pickTriangles.Length; i += 3)
                {
                    Vector2 a = projectedPickVertices[pickTriangles[i]];
                    Vector2 b = projectedPickVertices[pickTriangles[i + 1]];
                    Vector2 c = projectedPickVertices[pickTriangles[i + 2]];
                    float area = Cross(b - a, c - a);
                    float ab = Cross(b - a, pointer - a);
                    float bc = Cross(c - b, pointer - b);
                    float ca = Cross(a - c, pointer - c);
                    if (Mathf.Abs(area) > 0.0001f &&
                        ((ab >= 0f && bc >= 0f && ca >= 0f) ||
                         (ab <= 0f && bc <= 0f && ca <= 0f))) return 0f;
                    nearest = Mathf.Min(nearest, DistanceToSegmentSquared(pointer, a, b));
                    nearest = Mathf.Min(nearest, DistanceToSegmentSquared(pointer, b, c));
                    nearest = Mathf.Min(nearest, DistanceToSegmentSquared(pointer, c, a));
                }
                return Mathf.Sqrt(nearest);
            }

            // Missing/custom sprite geometry still gets a physically sized
            // capsule rather than a widthless centerline or padded art bounds.
            Vector2 direction = MovementMath.HeadingDegreesToVector(agent.CurrentMovementState.HeadingDegrees);
            float halfLength = archetype.movementProfile.HalfLengthWorldUnits;
            float halfBeam = halfLength * 0.22f;
            Vector2 position = transform.position;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            Vector2 start = camera.WorldToScreenPoint(position - direction * (halfLength - halfBeam));
            Vector2 end = camera.WorldToScreenPoint(position + direction * (halfLength - halfBeam));
            Vector2 center = camera.WorldToScreenPoint(position);
            Vector2 beamEdge = camera.WorldToScreenPoint(position + normal * halfBeam);
            return Mathf.Max(0f, Mathf.Sqrt(DistanceToSegmentSquared(pointer, start, end)) -
                Vector2.Distance(center, beamEdge));
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static float DistanceToSegmentSquared(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 segment = b - a;
            float t = segment.sqrMagnitude > 0.001f
                ? Mathf.Clamp01(Vector2.Dot(point - a, segment) / segment.sqrMagnitude) : 0f;
            return (point - (a + segment * t)).sqrMagnitude;
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
