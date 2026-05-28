// AStarHexPathService.cs:
// Converts the authoritative runtime hex map into an A* PointGraph.
// Each point node is placed directly on the corresponding visible tile center,
// while the ship remains free to move continuously through world space.
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
        [SerializeField] private bool logGraphRebuild = true;

        private readonly Vector2Int[] neighborBuffer = new Vector2Int[6];
        private readonly Dictionary<GraphNode, Vector2Int> cellByNode =
            new Dictionary<GraphNode, Vector2Int>();
        private readonly Dictionary<TraversalMaskKey, NavigationTraversalMask> traversalMasks =
            new Dictionary<TraversalMaskKey, NavigationTraversalMask>();
        private readonly Dictionary<NavigationTraversalMask, MaskTraversalProvider> traversalProviders =
            new Dictionary<NavigationTraversalMask, MaskTraversalProvider>();

        private PointGraph graph;
        private PointNode[] nodesByCell;
        private HexMapRuntime activeMap;
        private GraphMask graphMask;
        private NavigationTraversalMask lastAppliedTraversalMask;

        public bool IsReady =>
            graph != null &&
            nodesByCell != null &&
            activeMap != null &&
            AstarPath.active != null;

        public bool[] LastAppliedBlockedMask { get; private set; }
        public int TraversalMaskCacheHits { get; private set; }
        public int TraversalMaskCacheMisses { get; private set; }

        // Compatibility overload for callers that only provide a safety radius.
        public void RebuildGraph(HexMapRuntime map, float safetyRadiusWorld)
        {
            RebuildGraph(
                map,
                new NavigationProfile(safetyRadiusWorld, ShipDraftClass.Shallow));
        }

        // Builds an exact-position PointGraph for the current ship navigation profile.
        public void RebuildGraph(HexMapRuntime map, NavigationProfile profile)
        {
            if (map == null)
            {
                Debug.LogError(
                    "[AStarHexPathService] Cannot rebuild graph: map is null.");
                return;
            }

            if (!map.HasWorldCenters)
            {
                Debug.LogError(
                    "[AStarHexPathService] Cannot build PointGraph before visible cell centers are cached.");
                return;
            }

            if (AstarPath.active == null)
            {
                Debug.LogError(
                    "[AStarHexPathService] Cannot rebuild graph: AstarPath component is missing in scene.");
                return;
            }

            if (!EnsureGraph())
            {
                return;
            }

            activeMap = map;
            traversalMasks.Clear();
            traversalProviders.Clear();
            TraversalMaskCacheHits = 0;
            TraversalMaskCacheMisses = 0;

            lastAppliedTraversalMask = GetTraversalMask(map, profile);
            LastAppliedBlockedMask = lastAppliedTraversalMask.BlockedCells;

            AstarPath.active.AddWorkItem(ctx =>
            {
                graph.Clear();

                nodesByCell = new PointNode[map.Width * map.Height];
                cellByNode.Clear();

                BuildNodes(map);
                BuildConnections(map);

                graph.RebuildNodeLookup();
                ctx.SetGraphDirty(graph);
            });

            AstarPath.active.FlushWorkItems();
            graphMask = GraphMask.FromGraph(graph);

            if (logGraphRebuild)
            {
                Debug.Log(
                    $"[AStarHexPathService] PointGraph rebuilt: " +
                    $"Nodes={graph.nodeCount}, " +
                    $"Map={map.Width}x{map.Height}, " +
                    $"Draft={profile.DraftClass}, " +
                    $"SafetyRadius={profile.SafetyRadiusWorld:F2}");
            }
        }

        // Selects a different visible profile without changing graph topology or node state.
        // Individual path requests carry their immutable mask through an ITraversalProvider.
        public void ApplyTraversalProfile(
            HexMapRuntime map,
            NavigationProfile profile)
        {
            if (map == null)
            {
                Debug.LogError(
                    "[AStarHexPathService] Cannot apply traversal profile: map is null.");
                return;
            }

            if (!IsReady || map != activeMap)
            {
                // A new map needs complete topology construction before masks can be swapped.
                RebuildGraph(map, profile);
                return;
            }

            lastAppliedTraversalMask = GetTraversalMask(map, profile);
            LastAppliedBlockedMask = lastAppliedTraversalMask.BlockedCells;
        }

        // Caches expanded draft/clearance masks. Map edits change Version and miss safely.
        public NavigationTraversalMask GetTraversalMask(
            HexMapRuntime map,
            NavigationProfile profile)
        {
            if (map == null)
            {
                return null;
            }

            int safetySteps = Mathf.CeilToInt(
                Mathf.Max(0f, profile.SafetyRadiusWorld) /
                Mathf.Max(0.001f, map.CellSize));

            TraversalMaskKey key = new TraversalMaskKey(
                map.Version,
                profile.DraftClass,
                safetySteps);

            if (map == activeMap &&
                traversalMasks.TryGetValue(key, out NavigationTraversalMask cached))
            {
                TraversalMaskCacheHits++;
                return cached;
            }

            TraversalMaskCacheMisses++;
            NavigationTraversalMask created = new NavigationTraversalMask(
                map,
                map.Version,
                profile,
                BuildTraversalMask(map, profile));

            if (map == activeMap)
            {
                traversalMasks[key] = created;
            }

            return created;
        }

        // Requests a route between two simulation cells and returns simulation cells.
        public bool TryFindPath(
            Vector2Int start,
            Vector2Int goal,
            List<Vector2Int> outPath)
        {
            return TryFindPath(
                start,
                goal,
                lastAppliedTraversalMask,
                outPath);
        }

        // Requests a path using immutable per-ship restrictions instead of graph mutations.
        public bool TryFindPath(
            Vector2Int start,
            Vector2Int goal,
            NavigationTraversalMask mask,
            List<Vector2Int> outPath)
        {
            if (outPath == null)
            {
                return false;
            }

            outPath.Clear();

            if (!IsReady)
            {
                return false;
            }

            if (!activeMap.InBounds(start.x, start.y) ||
                !activeMap.InBounds(goal.x, goal.y) ||
                mask == null ||
                mask.Map != activeMap ||
                mask.MapVersion != activeMap.Version ||
                mask.IsBlocked(start) ||
                mask.IsBlocked(goal))
            {
                return false;
            }

            PointNode startNode = GetNode(start);
            PointNode goalNode = GetNode(goal);

            if (startNode == null ||
                goalNode == null ||
                !startNode.Walkable ||
                !goalNode.Walkable)
            {
                return false;
            }

            ABPath request = ABPath.Construct(
                (Vector3)startNode.position,
                (Vector3)goalNode.position,
                null);

            request.Claim(this);
            request.traversalConstraint.graphMask = graphMask;
            MaskTraversalProvider provider = GetTraversalProvider(mask);
            request.traversalConstraint.traversalProvider = provider;
            request.traversalCosts.traversalProvider = provider;
            request.calculatePartial = false;

            try
            {
                AstarPath.StartPath(request);
                request.BlockUntilCalculated();

                if (request.error ||
                    request.path == null ||
                    request.path.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < request.path.Count; i++)
                {
                    GraphNode returnedNode = request.path[i];

                    if (!cellByNode.TryGetValue(returnedNode, out Vector2Int cell))
                    {
                        continue;
                    }

                    if (outPath.Count == 0 ||
                        outPath[outPath.Count - 1] != cell)
                    {
                        outPath.Add(cell);
                    }
                }

                return outPath.Count > 0;
            }
            finally
            {
                request.Release(this);
            }
        }

        // Finds or creates the runtime PointGraph owned by this service.
        private bool EnsureGraph()
        {
            AstarData data = AstarPath.active.data;

            // Earlier sandbox builds stored a GridGraph under the same service-owned
            // name. Remove only that legacy graph so its diagonal gizmo cannot sit
            // behind the exact-position PointGraph and confuse alignment testing.
            NavGraph legacyGraph = data.FindGraph(candidate =>
                !(candidate is PointGraph) &&
                candidate.name == graphName);

            if (legacyGraph != null)
            {
                data.RemoveGraph(legacyGraph);

                if (logGraphRebuild)
                {
                    Debug.Log(
                        $"[AStarHexPathService] Removed legacy graph '{graphName}' " +
                        "before building the PointGraph.");
                }
            }

            graph = data.FindGraph(candidate =>
                candidate is PointGraph &&
                candidate.name == graphName) as PointGraph;

            if (graph == null)
            {
                graph = data.AddGraph(typeof(PointGraph)) as PointGraph;
            }

            if (graph == null)
            {
                Debug.LogError(
                    "[AStarHexPathService] Failed to create or locate PointGraph.");
                return false;
            }

            graph.name = graphName;

            // Nodes and links are constructed directly from HexMapRuntime.
            graph.root = null;
            graph.searchTag = null;
            graph.maxDistance = -1f;
            graph.raycast = false;
            graph.optimizeForSparseGraph = true;
            graph.nearestNodeDistanceMode = PointGraph.NodeDistanceMode.Node;

            return true;
        }

        // Adds one graph node for every visible simulation cell.
        // This method must only run inside an A* work item.
        private void BuildNodes(HexMapRuntime map)
        {
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int index = map.GetIndex(x, y);
                    Vector2 center = map.GetWorldCenter(x, y);

                    PointNode node = graph.AddNode(
                        (Int3)new Vector3(center.x, center.y, 0f));

                    node.Walkable = !map.IsBlocked(x, y);
                    node.Penalty = 0u;

                    nodesByCell[index] = node;
                    cellByNode[node] = new Vector2Int(x, y);
                }
            }
        }

        // Copies the authoritative odd-row neighbor topology into A* connections.
        // This method must only run inside an A* work item.
        private void BuildConnections(HexMapRuntime map)
        {
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int fromIndex = map.GetIndex(x, y);
                    PointNode fromNode = nodesByCell[fromIndex];

                    int neighborCount = map.GetNeighborCount(
                        x,
                        y,
                        neighborBuffer);

                    for (int i = 0; i < neighborCount; i++)
                    {
                        Vector2Int neighbor = neighborBuffer[i];
                        int toIndex = map.GetIndex(neighbor.x, neighbor.y);

                        // GraphNode.Connect creates a two-way connection.
                        // Only process each pair once.
                        if (toIndex <= fromIndex)
                        {
                            continue;
                        }

                        PointNode toNode = nodesByCell[toIndex];

                        uint cost = (uint)(toNode.position - fromNode.position)
                            .costMagnitude;

                        GraphNode.Connect(fromNode, toNode, cost);
                    }
                }
            }
        }

        private PointNode GetNode(Vector2Int cell)
        {
            if (activeMap == null ||
                nodesByCell == null ||
                !activeMap.InBounds(cell.x, cell.y))
            {
                return null;
            }

            return nodesByCell[activeMap.GetIndex(cell.x, cell.y)];
        }

        // Creates a ship-specific blocked mask from terrain, draft and clearance.
        private bool[] BuildTraversalMask(
            HexMapRuntime map,
            NavigationProfile profile)
        {
            int count = map.Width * map.Height;
            bool[] blocked = new bool[count];

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    blocked[map.GetIndex(x, y)] = IsForbiddenForShip(
                        map,
                        x,
                        y,
                        profile.DraftClass);
                }
            }

            int safetySteps = Mathf.CeilToInt(
                Mathf.Max(0f, profile.SafetyRadiusWorld) /
                Mathf.Max(0.001f, map.CellSize));

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
                    int index = map.GetIndex(x, y);

                    if (!blocked[index])
                    {
                        continue;
                    }

                    visited[index] = true;
                    queue.Enqueue(
                        new CellDepth(new Vector2Int(x, y), 0));
                }
            }

            while (queue.Count > 0)
            {
                CellDepth current = queue.Dequeue();
                int currentIndex = map.GetIndex(
                    current.Cell.x,
                    current.Cell.y);

                blocked[currentIndex] = true;

                if (current.Depth >= safetySteps)
                {
                    continue;
                }

                int neighborCount = map.GetNeighborCount(
                    current.Cell.x,
                    current.Cell.y,
                    neighborBuffer);

                for (int i = 0; i < neighborCount; i++)
                {
                    Vector2Int next = neighborBuffer[i];
                    int nextIndex = map.GetIndex(next.x, next.y);

                    if (visited[nextIndex])
                    {
                        continue;
                    }

                    visited[nextIndex] = true;
                    queue.Enqueue(
                        new CellDepth(next, current.Depth + 1));
                }
            }

            return blocked;
        }

        private static bool IsForbiddenForShip(
            HexMapRuntime map,
            int x,
            int y,
            ShipDraftClass draftClass)
        {
            if (map.IsBlocked(x, y))
            {
                return true;
            }

            return draftClass == ShipDraftClass.Deep &&
                   map.GetDepthClass(x, y) == WaterDepthClass.Shallow;
        }

        private MaskTraversalProvider GetTraversalProvider(
            NavigationTraversalMask mask)
        {
            if (traversalProviders.TryGetValue(mask, out MaskTraversalProvider provider))
            {
                return provider;
            }

            provider = new MaskTraversalProvider(
                mask,
                cellByNode);

            traversalProviders.Add(mask, provider);
            return provider;
        }

        private readonly struct TraversalMaskKey
        {
            private readonly int mapVersion;
            private readonly ShipDraftClass draftClass;
            private readonly int safetySteps;

            public TraversalMaskKey(
                int mapVersion,
                ShipDraftClass draftClass,
                int safetySteps)
            {
                this.mapVersion = mapVersion;
                this.draftClass = draftClass;
                this.safetySteps = safetySteps;
            }

            public override bool Equals(object obj)
            {
                return obj is TraversalMaskKey other &&
                       mapVersion == other.mapVersion &&
                       draftClass == other.draftClass &&
                       safetySteps == other.safetySteps;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = mapVersion;
                    hash = hash * 397 ^ (int)draftClass;
                    hash = hash * 397 ^ safetySteps;
                    return hash;
                }
            }
        }

        // Query-specific restrictions and water cost; immutable so A* worker threads can read it.
        private sealed class MaskTraversalProvider : ITraversalProvider
        {
            private readonly NavigationTraversalMask mask;
            private readonly Dictionary<GraphNode, Vector2Int> cellByNode;

            public MaskTraversalProvider(
                NavigationTraversalMask mask,
                Dictionary<GraphNode, Vector2Int> cellByNode)
            {
                this.mask = mask;
                this.cellByNode = cellByNode;
            }

            public bool CanTraverse(
                ref TraversalConstraint traversalConstraint,
                GraphNode node)
            {
                return cellByNode.TryGetValue(node, out Vector2Int cell) &&
                       !mask.IsBlocked(cell);
            }

            public float GetTraversalCostMultiplier(
                ref TraversalCosts traversalCosts,
                GraphNode node)
            {
                if (!cellByNode.TryGetValue(node, out Vector2Int cell))
                {
                    return 1f;
                }

                return Mathf.Max(
                    1f,
                    mask.Map.GetMoveCost(cell.x, cell.y));
            }

            public uint GetConnectionCost(
                ref TraversalCosts traversalCosts,
                GraphNode from,
                GraphNode to)
            {
                // The multiplier already represents rough-water travel time.
                // A second added cost would exaggerate the live slowdown.
                return 0u;
            }
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
