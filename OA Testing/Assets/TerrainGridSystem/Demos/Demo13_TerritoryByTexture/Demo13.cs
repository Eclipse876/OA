using System.Collections.Generic;
using UnityEngine;
using TGS;

namespace TGSDemos {

    public class Demo13 : MonoBehaviour {

        const string FRONTIER_PARENT_NAME = "_Demo13_FrontierLines";
        const float FRONTIER_LINE_WIDTH = 2f;
        const float FRONTIER_LIFETIME = 30f;

        TerrainGridSystem tgs;
        readonly List<Vector2> frontierVertices = new List<Vector2>();
        readonly List<Territory> neighbourBuffer = new List<Territory>();
        Material frontierMat;
        Transform frontierParent;

        void Start () {
            tgs = TerrainGridSystem.instance;
            tgs.OnTerritoryClick += Tgs_OnTerritoryClick;

            // Mark the centroid of each territory region (the centroid always lays inside the polygon)
            foreach (Territory territory in tgs.territories) {
                if (!territory.isEmpty) {
                    Vector2 centroid = territory.GetCentroid();
                    GameObject o = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    o.transform.position = tgs.transform.TransformPoint(centroid);
                    o.transform.localScale = Vector3.one * 10f;
                }
            }
        }

        private void Tgs_OnTerritoryClick (TerrainGridSystem tgs, int territoryIndex, int regionIndex, int buttonIndex) {
            // if (buttonIndex == 1) {
            //     DrawTerritoryFrontier(territoryIndex);
            //     return;
            // }
            if (buttonIndex == 1) {
                DrawTerritoryGridEdgeFrontier(-1);
                return;
            }
            Debug.Log("Hiding territory index " + territoryIndex);
            tgs.TerritorySetVisible(territoryIndex, false);
        }

        // Right-click: draws the full frontier of the clicked territory in yellow (incl. grid edges),
        // then overlays the shared frontier with each neighbour in a distinct color.
        void DrawTerritoryFrontier (int territoryIndex) {
            ClearFrontierLines();

            tgs.TerritoryGetFrontierVertices(territoryIndex, frontierVertices, includeGridEdges: true);
            SpawnLines(frontierVertices, Color.yellow);

            tgs.TerritoryGetNeighbours(territoryIndex, neighbourBuffer);
            int count = neighbourBuffer.Count;
            for (int n = 0; n < count; n++) {
                int otherIndex = tgs.TerritoryGetIndex(neighbourBuffer[n]);
                if (otherIndex < 0) continue;
                tgs.TerritoryGetFrontierVertices(territoryIndex, otherIndex, frontierVertices);
                Color c = Color.HSVToRGB((float)n / Mathf.Max(1, count), 0.85f, 1f);
                SpawnLines(frontierVertices, c);
            }
        }

        // Middle-click: draws only the segments where the territory touches the outer edge of the grid (in cyan).
        void DrawTerritoryGridEdgeFrontier (int territoryIndex) {
            ClearFrontierLines();

            int n = tgs.TerritoryGetGridEdgeVertices(territoryIndex, frontierVertices);
            Debug.Log("Territory " + territoryIndex + " grid-edge frontier vertices: " + n);
            SpawnLines(frontierVertices, Color.cyan);
        }

        void ClearFrontierLines () {
            if (frontierParent != null) Destroy(frontierParent.gameObject);
            frontierParent = new GameObject(FRONTIER_PARENT_NAME).transform;
            Destroy(frontierParent.gameObject, FRONTIER_LIFETIME);
        }

        // Splits a flat segment-pair list into continuous polylines (chain breaks where v[2k+1] != v[2k+2])
        // and spawns one LineRenderer per chain so the result is visible in the Game view.
        void SpawnLines (List<Vector2> vertices, Color color) {
            Transform t = tgs.transform;
            int n = vertices.Count;
            if (n < 2) return;

            if (frontierMat == null) {
                Shader s = Shader.Find("Sprites/Default");
                frontierMat = new Material(s);
            }

            int i = 0;
            while (i + 1 < n) {
                List<Vector3> chain = new List<Vector3> {
                    t.TransformPoint(vertices[i]),
                    t.TransformPoint(vertices[i + 1])
                };
                i += 2;
                while (i + 1 < n && Vector2.SqrMagnitude(vertices[i] - vertices[i - 1]) < 1e-8f) {
                    chain.Add(t.TransformPoint(vertices[i + 1]));
                    i += 2;
                }
                CreateLineRenderer(chain, color);
            }
        }

        void CreateLineRenderer (List<Vector3> points, Color color) {
            GameObject go = new GameObject("FrontierLine");
            go.transform.SetParent(frontierParent, worldPositionStays: true);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = frontierMat;
            lr.startColor = color;
            lr.endColor = color;
            lr.startWidth = FRONTIER_LINE_WIDTH;
            lr.endWidth = FRONTIER_LINE_WIDTH;
            lr.useWorldSpace = true;
            lr.positionCount = points.Count;
            for (int k = 0; k < points.Count; k++) lr.SetPosition(k, points[k]);
        }

    }

}