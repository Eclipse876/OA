// RouteLineRenderer.cs:
// Draws the ship's predicted physical path using its achieved speed.
// Color changes now show acceleration and deceleration directly:
// red through yellow and green to cyan as the ship approaches flank speed.
using System.Collections.Generic;
using OA.Simulation.Movement;
using UnityEngine;

namespace OA.Presentation.Debug
{
    public sealed class RouteLineRenderer : MonoBehaviour
    {
        [SerializeField] private LineRenderer segmentPrefab;


        [SerializeField] private Color cruiseColor = Color.green;
        [SerializeField] private Color flankColor = Color.cyan;
        [SerializeField] private Color slowColor = Color.yellow;
        [SerializeField] private Color stopColor = Color.red;
        [SerializeField, Range(0.05f, 0.95f)] private float slowColorAtCruiseFraction = 0.5f;

        [SerializeField, Min(0.001f)] private float width = 0.05f;

        private readonly List<Vector3> vertices = new List<Vector3>(2048);
        private readonly List<Color> colors = new List<Color>(2048);
        private readonly List<Vector2> uv = new List<Vector2>(2048);
        private readonly List<int> triangles = new List<int>(3072);

        private Mesh routeMesh;
        private MeshRenderer routeRenderer;

        public double LastBuildMilliseconds { get; private set; }

        private void Awake()
        {
            EnsureMeshOutput();

            if (segmentPrefab != null)
            {
                // The assigned LineRenderer remains the style/material template.
                // Geometry is drawn by one reusable mesh instead of cloned chunks.
                segmentPrefab.gameObject.SetActive(false);
            }
        }

        // Draws the entire prediction once when a new route is accepted.
        public void Draw(
            IReadOnlyList<ShipRouteSample> samples,
            float cruiseSpeedKnots,
            float flankSpeedKnots)
        {
            Vector2 leadingPosition = samples != null && samples.Count > 0
                ? samples[0].Position
                : default;

            DrawRemaining(
                samples,
                0,
                leadingPosition,
                cruiseSpeedKnots,
                flankSpeedKnots);
        }

        // Redraws only the untraveled prediction, beginning at the live ship position.
        // The leading point follows the vessel so no completed line is left behind it.
        public void DrawRemaining(
            IReadOnlyList<ShipRouteSample> samples,
            int firstVisibleSampleIndex,
            Vector2 leadingPosition,
            float cruiseSpeedKnots,
            float flankSpeedKnots)
        {
            if (!EnsureMeshOutput())
            {
                return;
            }

            if (samples == null ||
                samples.Count < 2 ||
                firstVisibleSampleIndex >= samples.Count - 1)
            {
                Clear();
                return;
            }

            System.Diagnostics.Stopwatch timer =
                System.Diagnostics.Stopwatch.StartNew();

            vertices.Clear();
            colors.Clear();
            uv.Clear();
            triangles.Clear();

            int first = Mathf.Clamp(
                firstVisibleSampleIndex,
                0,
                samples.Count - 2);

            float accumulatedDistance = 0f;
            Vector2 previousPoint = leadingPosition;

            for (int i = first; i < samples.Count; i++)
            {
                Vector2 point = i == first
                    ? leadingPosition
                    : samples[i].Position;

                if (i > first)
                {
                    accumulatedDistance += Vector2.Distance(
                        previousPoint,
                        point);
                }

                Vector2 before = i == first
                    ? point
                    : i == first + 1
                        ? leadingPosition
                        : samples[i - 1].Position;

                Vector2 after = i == samples.Count - 1
                    ? point
                    : samples[i + 1].Position;

                Vector2 tangent = after - before;
                if (tangent.sqrMagnitude <= 0.000001f)
                {
                    tangent = Vector2.right;
                }

                tangent.Normalize();
                Vector2 normal = new Vector2(-tangent.y, tangent.x);
                Vector2 offset = normal * (width * 0.5f);
                Color color = GetSpeedColor(
                    samples[i].SpeedKnots,
                    cruiseSpeedKnots,
                    flankSpeedKnots);

                AddVertex(point - offset, color, 0f, accumulatedDistance);
                AddVertex(point + offset, color, 1f, accumulatedDistance);

                if (i > first)
                {
                    int index = vertices.Count - 4;
                    triangles.Add(index);
                    triangles.Add(index + 2);
                    triangles.Add(index + 1);
                    triangles.Add(index + 1);
                    triangles.Add(index + 2);
                    triangles.Add(index + 3);
                }

                previousPoint = point;
            }

            routeMesh.Clear();
            routeMesh.SetVertices(vertices);
            routeMesh.SetColors(colors);
            routeMesh.SetUVs(0, uv);
            routeMesh.SetTriangles(triangles, 0);
            routeMesh.RecalculateBounds();
            routeRenderer.gameObject.SetActive(vertices.Count >= 4);

            timer.Stop();
            LastBuildMilliseconds = timer.Elapsed.TotalMilliseconds;
        }

        public void Clear()
        {
            if (routeMesh != null)
            {
                routeMesh.Clear();
            }

            if (routeRenderer != null)
            {
                routeRenderer.gameObject.SetActive(false);
            }

            LastBuildMilliseconds = 0d;
        }

        private bool EnsureMeshOutput()
        {
            if (routeRenderer != null && routeMesh != null)
            {
                return true;
            }

            if (segmentPrefab == null)
            {
                return false; // Can't draw without a material/style template.
            }

            GameObject routeObject = new GameObject("RouteMesh");
            routeObject.transform.SetParent(transform, false);

            MeshFilter meshFilter = routeObject.AddComponent<MeshFilter>();
            routeRenderer = routeObject.AddComponent<MeshRenderer>();
            routeRenderer.sharedMaterial = segmentPrefab.sharedMaterial;
            routeRenderer.sortingLayerID = segmentPrefab.sortingLayerID;
            routeRenderer.sortingOrder = segmentPrefab.sortingOrder;

            routeMesh = new Mesh
            {
                name = "Predicted Ship Route Mesh"
            };

            routeMesh.MarkDynamic();
            meshFilter.sharedMesh = routeMesh;
            routeObject.SetActive(false);
            return true;
        }

        private void AddVertex(
            Vector2 worldPosition,
            Color color,
            float acrossLine,
            float alongLine)
        {
            Vector3 local = transform.InverseTransformPoint(
                new Vector3(worldPosition.x, worldPosition.y, 0f));

            vertices.Add(local);
            colors.Add(color);
            uv.Add(new Vector2(acrossLine, alongLine));
        }

        private Color GetSpeedColor(
            float speedKnots,
            float cruiseSpeedKnots,
            float flankSpeedKnots)
        {
            float cruise = Mathf.Max(0.001f, cruiseSpeedKnots);
            float flank = Mathf.Max(cruise, flankSpeedKnots);
            float slowAnchor = cruise * slowColorAtCruiseFraction;

            if (speedKnots <= slowAnchor)
            {
                return Color.Lerp(
                    stopColor,
                    slowColor,
                    Mathf.InverseLerp(0f, slowAnchor, speedKnots));
            }

            if (speedKnots <= cruise)
            {
                return Color.Lerp(
                    slowColor,
                    cruiseColor,
                    Mathf.InverseLerp(slowAnchor, cruise, speedKnots));
            }

            return Color.Lerp(
                cruiseColor,
                flankColor,
                Mathf.InverseLerp(cruise, flank, speedKnots));
        }

        private void OnDestroy()
        {
            if (routeMesh != null)
            {
                Destroy(routeMesh);
            }
        }
    }
}
