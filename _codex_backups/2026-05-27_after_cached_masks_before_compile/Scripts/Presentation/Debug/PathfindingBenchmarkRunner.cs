// PathfindingBenchmarkRunner.cs:
// Tiny stopwatch button for pathfinding. Point it at the sandbox and it spams
// random routes so we can see if the path service is chill or secretly on fire.
using System.Collections.Generic;
using System.Diagnostics;
using OA.Simulation.Navigation;
using UnityEngine;

namespace OA.Presentation.Debug
{
    // Runs repeat path queries against the current sandbox map and logs timing/memory numbers.
    public sealed class PathfindingBenchmarkRunner : MonoBehaviour
    {
        [SerializeField] private PathfindingSandboxController controller;
        [SerializeField, Min(10)] private int queryCount = 2000;
        [SerializeField, Min(1)] private int randomSeed = 1337;

        [ContextMenu("Run Pathfinding Benchmark")]
        // Context-menu entry point for hammering the current path service with random routes.
        public void RunBenchmark()
        {
            if (controller == null)
            {
                UnityEngine.Debug.LogError("[Benchmark] Missing PathfindingSandboxController");
                return;
            }

            HexMapRuntime map = controller.CurrentMap;
            INavigationPathService pathService = controller.CurrentPathService;

            if(map == null || pathService == null || !pathService.IsReady)
            {
                UnityEngine.Debug.LogError("[Benchmark] Map or PathService not ready");
                return;
            }

            // Build a pool of legal start/end cells so the benchmark measures pathfinding, not bad input.
            List<Vector2Int> traversableCells = BuildTraversableList(map, controller);
            if(traversableCells.Count < 2)
            {
                UnityEngine.Debug.LogError("[Benchmark] Not enough traversable cells for benchmarking");
                return;
            }

            System.Random rng = new System.Random(randomSeed);

            List<Vector2Int> pathBuffer = new List<Vector2Int>(1024);

            // Clear old GC noise before measuring so the numbers are less fake.
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();

            long memoryBefore = System.GC.GetTotalMemory(true);

            Stopwatch sw = Stopwatch.StartNew();
            int succeeded = 0;

            // Fire random path requests and count how many succeed.
            for (int i = 0; i < queryCount; i++)
            {
                Vector2Int start = traversableCells[rng.Next(traversableCells.Count)];
                Vector2Int goal  = traversableCells[rng.Next(traversableCells.Count)];

                if (pathService.TryFindPath(start, goal, pathBuffer))
                {
                    succeeded++;
                }
            }

            sw.Stop();
            long memoryAfter = System.GC.GetTotalMemory(false);

            double avgMicroseconds = (sw.Elapsed.TotalMilliseconds * 1000.0) / Mathf.Max(1, queryCount);
            long memoryUsed = memoryAfter - memoryBefore;

            UnityEngine.Debug.Log(
                $"[Benchmark] Queries={queryCount}, Success={succeeded}, TotalMs = {sw.Elapsed.TotalMilliseconds:F3}, " +
                $"AvgUs={avgMicroseconds:F2}, MemDeltaBytes={memoryUsed}");
        }

        // Collects every cell the controller says is valid for the current safety mask.
        private static List<Vector2Int> BuildTraversableList(HexMapRuntime map, PathfindingSandboxController controller)
        {
            List<Vector2Int> cells = new List<Vector2Int>(map.Width * map.Height);

            for(int y = 0; y < map.Height; y++)
            {
                for(int x = 0; x < map.Width; x++)
                {
                    Vector2Int c = new Vector2Int(x, y);
                    if (controller.IsCellTraversable(c))
                    {
                        cells.Add(c);                        
                    }
                }
            }

            return cells;
        }
    }
}
