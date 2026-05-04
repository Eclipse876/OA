using UnityEngine;
using TGS;

namespace TGSDemos {

    /// <summary>
    /// Demonstrates thread-safe async pathfinding with multiple concurrent requests.
    /// Press SPACE to launch multiple pathfinding requests simultaneously.
    /// </summary>
    public class Demo34 : MonoBehaviour {

        TerrainGridSystem tgs;
        GUIStyle labelStyle;

        int pendingRequests;
        int completedRequests;
        int totalPathsFound;
        float lastRequestTime;

        readonly Color[] pathColors = { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta };

        void Start() {
            tgs = TerrainGridSystem.instance;

            // Setup GUI
            GUIResizer.Init(800, 500);
            labelStyle = new GUIStyle {
                alignment = TextAnchor.MiddleLeft
            };
            labelStyle.normal.textColor = Color.white;

            // Create some obstacles
            CreateObstacles();
        }

        void CreateObstacles() {
            Random.InitState(42);
            for (int i = 0; i < 30; i++) {
                int row = Random.Range(2, tgs.rowCount - 3);
                int col = Random.Range(2, tgs.columnCount - 3);
                int cellIndex = tgs.CellGetIndex(row, col);
                if (cellIndex >= 0) {
                    tgs.CellSetCanCross(cellIndex, false);
                    tgs.CellSetColor(cellIndex, Color.gray);
                }
            }
        }

        void Update() {
            if (tgs.input.GetKeyDown("space")) {
                LaunchConcurrentPathRequests();
            }
        }

        void LaunchConcurrentPathRequests() {
            int numRequests = 6;
            pendingRequests = numRequests;
            completedRequests = 0;
            totalPathsFound = 0;
            lastRequestTime = Time.realtimeSinceStartup;

            Debug.Log($"[Demo34] Launching {numRequests} concurrent pathfinding requests...");

            for (int i = 0; i < numRequests; i++) {
                int startCell = Random.Range(0, tgs.numCells - 1);
                int endCell = Random.Range(0, tgs.numCells - 1);

                // Ensure start and end are different and crossable
                while (startCell == endCell || !tgs.cells[startCell].canCross) {
                    startCell = Random.Range(0, tgs.numCells - 1);
                }
                while (!tgs.cells[endCell].canCross) {
                    endCell = Random.Range(0, tgs.numCells - 1);
                }

                int colorIndex = i;
                int requestId = i + 1;

                // Launch async pathfinding - runs on background thread, callback on main thread
                tgs.FindPathAsync(startCell, endCell, result => {
                    OnPathComplete(result, colorIndex, requestId);
                });
            }
        }

        void OnPathComplete(FindPathAsyncResult result, int colorIndex, int requestId) {
            completedRequests++;
            pendingRequests--;

            if (result.success && result.path != null && result.path.Count > 0) {
                totalPathsFound++;
                Color pathColor = pathColors[colorIndex % pathColors.Length];

                // Visualize the path
                for (int k = 0; k < result.path.Count; k++) {
                    tgs.CellFadeOut(result.path[k], pathColor, 2f);
                }

                Debug.Log($"[Demo34] Request #{requestId} complete: Path found with {result.path.Count} cells, cost: {result.totalCost:F2}");
            } else {
                Debug.Log($"[Demo34] Request #{requestId} complete: No path found");
            }

            if (pendingRequests == 0) {
                float elapsed = Time.realtimeSinceStartup - lastRequestTime;
                Debug.Log($"[Demo34] All {completedRequests} requests completed in {elapsed * 1000:F1}ms. Paths found: {totalPathsFound}");
            }
        }

    }
}
