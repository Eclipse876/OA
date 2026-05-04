using System.Collections.Generic;
using OA.Simulation.Navigation;
using Pathfinding;
using UnityEngine;

namespace OA.Integrations.AStar
{
    public sealed class AStarHexPathService : MonoBehaviour, INavigationPathService
    {
        [Header("Graph Settings")]
            [SerializeField] private string graphName = "OA_HexGraph";
            [SerializeField] private bool scanGraphOnRebuild = true;

        //Preferred: align graph center to Unity Grid layout.
            [SerializeField] private bool alignGraphToGridLayout = true;
            [SerializeField] private GridLayout alignmentGridLayout;

        //Fallback is no grid layout is assigned.
            [SerializeField] private Vector2 manualGraphCenterWorld = Vector2.zero;
            [SerializeField, Min(0.05f)] private float manualHexWidth = 1.1f;


        [Header("Cost Tuning")]
            [SerializeField] private float roughPenaltyScale = 12000f;

        private readonly Vector2Int[] neighborBuffer = new Vector2Int[6];

        private GridGraph graph;
        private GraphMask graphMask;

        public bool IsReady => graph != null && AstarPath.active != null;
        public bool[] LastAppliedBlockedMask { get; private set; }


        public void RebuildGraph(HexMapRuntime map, float safetyRadiusWorld)
        {
            if (map == null)
            {
                Debug.LogError("[AStarHexPathService] RebuildGraph called with null map.");
                return;
            }

            if (AstarPath.active == null)
            {
                Debug.LogError("[AStarHexPathService] Missing AstarPath component in scene.");
                return;
            }

            EnsureGraph(map);
            if (graph == null)
            {
                return;
            }

            bool[] blockedWithSafety = BuildSafetyMask(map, safetyRadiusWorld);
            LastAppliedBlockedMask = blockedWithSafety;

            AstarPath.active.AddWorkItem(ctx =>
            {
                for (int y = 0; y < map.Height; y++)
                {
                    for (int x = 0; x < map.Width; x++)
                    {
                        GridNodeBase node = graph.GetNode(x, y);
                        if (node == null)
                        {
                            continue;
                        }

                        int index = map.GetIndex(x, y);
                        bool walkable = !blockedWithSafety[index];
                        node.Walkable = walkable;

                        float cellCost = map.GetMoveCost(x, y);
                        uint penalty = cellCost <= 1.001f
                            ? 0u
                            : (uint)Mathf.RoundToInt((cellCost - 1f) * roughPenaltyScale);

                        node.Penalty = walkable ? penalty : 0u;
                    }
                }

                graph.RecalculateAllConnections();
                ctx.SetGraphDirty(graph);
            });

            AstarPath.active.FlushWorkItems();
        }

        public bool TryFindPath(Vector2Int start, Vector2Int goal, List<Vector2Int> outPath)
        {
            outPath.Clear();

            if (graph == null || AstarPath.active == null)
            {
                return false;
            }

            GridNodeBase startNode = graph.GetNode(start.x, start.y);
            GridNodeBase goalNode = graph.GetNode(goal.x, goal.y);

            if (startNode == null || goalNode == null || !startNode.Walkable || !goalNode.Walkable)
            {
                return false;
            }

            ABPath request = ABPath.Construct((Vector3)startNode.position, (Vector3)goalNode.position, null);
            request.Claim(this);
            request.traversalConstraint.graphMask = graphMask;
            request.calculatePartial = false;

            try
            {
                AstarPath.StartPath(request);
                request.BlockUntilCalculated();

                bool success = !request.error && request.path != null && request.path.Count > 0;
                if (!success)
                {
                    return false;
                }

                for (int i = 0; i < request.path.Count; i++)
                {
                    GridNodeBase n = request.path[i] as GridNodeBase;
                    if (n != null)
                    {
                        outPath.Add(n.CoordinatesInGrid);
                    }
                }

                return outPath.Count > 0;
            }
            finally
            {
                request.Release(this);
            }
        }

        private void EnsureGraph(HexMapRuntime map)
        {
            AstarData data = AstarPath.active.data;
            graph = data.gridGraph;
            if (graph == null)
            {
                graph = data.AddGraph(typeof(GridGraph)) as GridGraph;
            }

            if (graph == null)
            {
                Debug.LogError("[AStarHexPathService] Failed to create/read GridGraph.");
                return;
            }

            graph.name = graphName;
            graph.SetGridShape(InspectorGridMode.Hexagonal);
            graph.is2D = true;
            graph.collision.use2D = true;
            graph.collision.collisionCheck = false;
            graph.collision.heightCheck = false;
            graph.uniformEdgeCosts = true;
            graph.neighbours = NumNeighbours.Six;

            ApplyGraphTransform(map);

            if (scanGraphOnRebuild)
            {
                AstarPath.active.Scan();
            }

            graphMask = GraphMask.FromGraph(graph);
        }

        private void ApplyGraphTransform(HexMapRuntime map)
        {
            if (alignGraphToGridLayout && alignmentGridLayout != null)
            {
                float bootstrapNodeSize = GridGraph.ConvertHexagonSizeToNodeSize(
                    InspectorGridHexagonNodeSize.Width,
                    Mathf.Max(0.05f, manualHexWidth));

                graph.SetDimensions(map.Width, map.Height, bootstrapNodeSize);
                graph.AlignToTilemap(alignmentGridLayout);
                graph.SetDimensions(map.Width, map.Height, graph.nodeSize);
                return;
            }

            if (alignGraphToGridLayout && alignmentGridLayout == null)
            {
                Debug.LogWarning("[AStarHexPathService] alignGraphCenterToGrid enabled, but alignGraphToGridLayout enabled, but alignmentGridLayout is null. Using manual center/size fallback."); 
            }

            float nodeSize = GridGraph.ConvertHexagonSizeToNodeSize(
                InspectorGridHexagonNodeSize.Width,
                Mathf.Max(0.05f, manualHexWidth));

            graph.SetDimensions(map.Width, map.Height, nodeSize);
            graph.center = new Vector3(manualGraphCenterWorld.x, manualGraphCenterWorld.y, 0f);

        }

        private bool[] BuildSafetyMask(HexMapRuntime map, float safetyRadiusWorld)
        {
            int count = map.Width * map.Height;
            bool[] blocked = new bool[count];

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    blocked[map.GetIndex(x, y)] = map.IsBlocked(x, y);
                }
            }

            int safetySteps = Mathf.CeilToInt(
                Mathf.Max(0f, safetyRadiusWorld) / Mathf.Max(0.001f, map.CellSize));

            if (safetySteps <= 0)
            {
                return blocked;
            }

            bool[] visited = new bool[count];
            Queue<CellDepth> queue = new Queue<CellDepth>(count);

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    if (!map.IsBlocked(x, y))
                    {
                        continue;
                    }

                    int idx = map.GetIndex(x, y);
                    visited[idx] = true;
                    queue.Enqueue(new CellDepth(new Vector2Int(x, y), 0));
                }
            }

            while (queue.Count > 0)
            {
                CellDepth current = queue.Dequeue();
                blocked[map.GetIndex(current.Cell.x, current.Cell.y)] = true;

                if (current.Depth >= safetySteps)
                {
                    continue;
                }

                int neighbors = map.GetNeighborCount(current.Cell.x, current.Cell.y, neighborBuffer);
                for (int i = 0; i < neighbors; i++)
                {
                    Vector2Int next = neighborBuffer[i];
                    int nextIndex = map.GetIndex(next.x, next.y);
                    if (visited[nextIndex])
                    {
                        continue;
                    }

                    visited[nextIndex] = true;
                    queue.Enqueue(new CellDepth(next, current.Depth + 1));
                }
            }

            return blocked;
        }

        private struct CellDepth
        {
            public Vector2Int Cell;
            public int Depth;

            public CellDepth(Vector2Int cell, int depth)
            {
                Cell = cell;
                Depth = depth;
            }
        }
    }
}
