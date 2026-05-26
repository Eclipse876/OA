using System.Collections.Generic;
using OA.Simulation.Movement;
using UnityEngine;

namespace OA.Presentation.Debug
{
    public sealed class RouteLineRenderer : MonoBehaviour
    {
        [SerializeField] private LineRenderer segmentPrefab;


        [SerializeField] private Color cruiseColor = new Color(0.65f, 1f, 1f, 1f);
        [SerializeField] private Color flankColor = new Color(0f, 0.45f, 0.45f, 1f);
        [SerializeField] private Color slowColor = Color.yellow;
        [SerializeField] private Color stopColor = Color.red;

        [SerializeField, Min(0.001f)] private float width = 0.05f;

        private readonly List<LineRenderer> pool = new List<LineRenderer>(256);

        public void Draw(IReadOnlyList<ShipRouteSample> samples)
        {
            if (segmentPrefab == null) { return; } // Can't draw without a prefab

            if (samples == null || samples.Count < 2) { Clear(); return; } // Not enough points to draw a line

            int usedLines = 0;
            int groupStart = 0;
            RouteSegmentIntent currentIntent = samples[1].SegmentIntent;

            for (int i = 2; i < samples.Count; i++)
            {
                RouteSegmentIntent nextIntent = samples[i].SegmentIntent;
                if (nextIntent == currentIntent) { continue; }

                DrawSampleGroup(usedLines, samples, groupStart, i - 1, currentIntent);
                usedLines++;

                groupStart = i - 1;
                currentIntent = nextIntent;
            }

            DrawSampleGroup(usedLines, samples, groupStart, samples.Count - 1, currentIntent);
            usedLines++;

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

        private void DrawSampleGroup(
            int poolIndex,
            IReadOnlyList<ShipRouteSample> samples,
            int startIndex,
            int endIndex,
            RouteSegmentIntent intent)
        {
            if (endIndex <= startIndex) { return; } // Not enough points to draw a line

            EnsurePool(poolIndex + 1);

            LineRenderer line = pool[poolIndex];
            Color color = GetColor(intent);

            line.gameObject.SetActive(true);
            line.positionCount = endIndex - startIndex + 1;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;

            for (int i = startIndex; i <= endIndex; i++)
            {
                Vector2 p = samples[i].Position;
                line.SetPosition(i - startIndex, new Vector3(p.x, p.y, 0f));
            }
        }

        private Color GetColor(RouteSegmentIntent intent)
        {
            switch (intent)
            {
                case RouteSegmentIntent.Flank: return flankColor;
                case RouteSegmentIntent.Cruise: return cruiseColor;
                case RouteSegmentIntent.Slow: return slowColor;
                case RouteSegmentIntent.Stop: return stopColor;
                default: return Color.hotPink;
            }
        }

        private void EnsurePool(int count)
        {
            while(pool.Count < count)
            {
                LineRenderer segment = Instantiate(segmentPrefab, transform);
                segmentPrefab.gameObject.SetActive(false);
                pool.Add(segment);
            }

            segmentPrefab.gameObject.SetActive(false);
        }
    }
}