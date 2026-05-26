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
        private const int MaximumGradientKeys = 8;

        [SerializeField] private LineRenderer segmentPrefab;


        [SerializeField] private Color cruiseColor = Color.green;
        [SerializeField] private Color flankColor = Color.cyan;
        [SerializeField] private Color slowColor = Color.yellow;
        [SerializeField] private Color stopColor = Color.red;
        [SerializeField, Range(0.05f, 0.95f)] private float slowColorAtCruiseFraction = 0.5f;

        [SerializeField, Min(0.001f)] private float width = 0.05f;

        private readonly List<LineRenderer> pool = new List<LineRenderer>(256);

        private void Awake()
        {
            if (segmentPrefab != null)
            {
                segmentPrefab.gameObject.SetActive(false);
            }
        }

        // Splits prediction into short gradient runs because Unity accepts eight
        // gradient keys. Each retained physical sample receives its real speed color.
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
            if (segmentPrefab == null)
            {
                return; // Can't draw without a prefab.
            }

            if (samples == null ||
                samples.Count < 2 ||
                firstVisibleSampleIndex >= samples.Count - 1)
            {
                Clear();
                return; // Not enough points to draw a line.
            }

            int usedLines = 0;
            int groupStart = Mathf.Clamp(
                firstVisibleSampleIndex,
                0,
                samples.Count - 2);

            while (groupStart < samples.Count - 1)
            {
                int groupEnd = Mathf.Min(
                    samples.Count - 1,
                    groupStart + MaximumGradientKeys - 1);

                DrawSpeedGroup(
                    usedLines,
                    samples,
                    groupStart,
                    groupEnd,
                    usedLines == 0,
                    leadingPosition,
                    cruiseSpeedKnots,
                    flankSpeedKnots);

                usedLines++;
                groupStart = groupEnd;
            }

            for (int i = usedLines; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(false);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(false);
            }
        }

        private void DrawSpeedGroup(
            int poolIndex,
            IReadOnlyList<ShipRouteSample> samples,
            int startIndex,
            int endIndex,
            bool replaceStartPosition,
            Vector2 startPosition,
            float cruiseSpeedKnots,
            float flankSpeedKnots)
        {
            if (endIndex <= startIndex)
            {
                return; // Not enough points to draw a line.
            }

            EnsurePool(poolIndex + 1);

            LineRenderer line = pool[poolIndex];

            line.gameObject.SetActive(true);
            line.positionCount = endIndex - startIndex + 1;
            line.startWidth = width;
            line.endWidth = width;
            line.colorGradient = BuildSpeedGradient(
                samples,
                startIndex,
                endIndex,
                cruiseSpeedKnots,
                flankSpeedKnots);

            for (int i = startIndex; i <= endIndex; i++)
            {
                Vector2 p = replaceStartPosition && i == startIndex
                    ? startPosition
                    : samples[i].Position;

                line.SetPosition(i - startIndex, new Vector3(p.x, p.y, 0f));
            }
        }

        private Gradient BuildSpeedGradient(
            IReadOnlyList<ShipRouteSample> samples,
            int startIndex,
            int endIndex,
            float cruiseSpeedKnots,
            float flankSpeedKnots)
        {
            int count = endIndex - startIndex + 1;
            float totalDistance = 0f;

            for (int i = startIndex + 1; i <= endIndex; i++)
            {
                totalDistance += Vector2.Distance(
                    samples[i - 1].Position,
                    samples[i].Position);
            }

            totalDistance = Mathf.Max(0.0001f, totalDistance);

            GradientColorKey[] colorKeys = new GradientColorKey[count];
            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[count];
            float distance = 0f;

            for (int i = startIndex; i <= endIndex; i++)
            {
                if (i > startIndex)
                {
                    distance += Vector2.Distance(
                        samples[i - 1].Position,
                        samples[i].Position);
                }

                float time = Mathf.Clamp01(distance / totalDistance);
                Color color = GetSpeedColor(
                    samples[i].SpeedKnots,
                    cruiseSpeedKnots,
                    flankSpeedKnots);

                int keyIndex = i - startIndex;
                colorKeys[keyIndex] = new GradientColorKey(color, time);
                alphaKeys[keyIndex] = new GradientAlphaKey(color.a, time);
            }

            Gradient gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
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

        // Grows the segment pool only when a route needs another gradient chunk.
        private void EnsurePool(int count)
        {
            while (pool.Count < count)
            {
                LineRenderer segment = Instantiate(segmentPrefab, transform);
                segment.gameObject.SetActive(false);
                pool.Add(segment);
            }
        }
    }
}
