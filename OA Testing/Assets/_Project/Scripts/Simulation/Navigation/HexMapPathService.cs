// HexMapPathService.cs:
// Native OA hex pathfinding. This searches HexMapRuntime directly instead of
// translating the map into an external A* graph object.
using System.Collections.Generic;
using UnityEngine;

namespace OA.Simulation.Navigation
{
    // This is the permanent-style path service: the map itself is the graph.
    // The ship still moves freely in world space after this returns a corridor;
    // this class only answers "which hex cells make a legal water route?"
    public sealed class HexMapPathService : MonoBehaviour, INavigationPathService
    {
        [Header("Diagnostics")]
        [SerializeField] private bool logRebuild = true;

        // Shared scratch data. Keeping these around avoids allocations every time
        // the player clicks a destination.
        private readonly Vector2Int[] neighborBuffer = new Vector2Int[6];
        private readonly Dictionary<TraversalMaskKey, NavigationTraversalMask> traversalMasks =
            new Dictionary<TraversalMaskKey, NavigationTraversalMask>();
        private readonly List<int> rebuiltPath = new List<int>(512);

        // One binary heap, reused between searches. It stores "cells to visit next"
        // ordered by the cheapest estimated total route cost.
        private NativeOpenHeap openHeap;

        // The active map is the authoritative terrain graph. If this changes,
        // every cached traversal mask and search buffer belongs to the old world.
        private HexMapRuntime activeMap;
        private NavigationTraversalMask lastAppliedTraversalMask;

        // A* bookkeeping arrays, indexed by HexMapRuntime.GetIndex(x, y).
        // Search versions let us reuse the arrays without clearing them each query.
        private int[] cameFrom;
        private float[] gScore;
        private int[] searchVersionByCell;
        private int[] closedVersionByCell;
        private int searchVersion;

        public bool IsReady => activeMap != null;

        public bool[] LastAppliedBlockedMask { get; private set; }
        public int TraversalMaskCacheHits { get; private set; }
        public int TraversalMaskCacheMisses { get; private set; }

        // Compatibility overload for older callers that only know a radius.
        public void RebuildGraph(HexMapRuntime map, float safetyRadiusWorld)
        {
            RebuildGraph(
                map,
                new NavigationProfile(safetyRadiusWorld, ShipDraftClass.Shallow));
        }

        // "Rebuild graph" now means "prepare to search this HexMapRuntime."
        // There is no secondary graph to construct, but the interface name stays
        // so existing controller code can swap services without caring.
        public void RebuildGraph(HexMapRuntime map, NavigationProfile profile)
        {
            if (map == null)
            {
                Debug.LogError("[HexMapPathService] Cannot rebuild navigation service: map is null.");
                return;
            }

            if (!map.HasWorldCenters)
            {
                Debug.LogError("[HexMapPathService] Cannot rebuild navigation service before visible cell centers are cached.");
                return;
            }

            activeMap = map;
            EnsureSearchBuffers(map.Width * map.Height);

            traversalMasks.Clear();
            TraversalMaskCacheHits = 0;
            TraversalMaskCacheMisses = 0;

            lastAppliedTraversalMask = GetTraversalMask(map, profile);
            LastAppliedBlockedMask = lastAppliedTraversalMask.BlockedCells;

            if (logRebuild)
            {
                Debug.Log(
                    $"[HexMapPathService] Native hex path service ready. " +
                    $"Cells={map.Width * map.Height}, " +
                    $"Map={map.Width}x{map.Height}, " +
                    $"Draft={profile.DraftClass}, " +
                    $"SafetyRadius={profile.SafetyRadiusWorld:F2}");
            }
        }

        // Selects the mask shown by debug tools. Queries can still pass their own
        // mask for temporary wider clearance tests without changing visible state.
        public void ApplyTraversalProfile(
            HexMapRuntime map,
            NavigationProfile profile)
        {
            if (map == null)
            {
                Debug.LogError("[HexMapPathService] Cannot apply traversal profile: map is null.");
                return;
            }

            if (!IsReady || map != activeMap)
            {
                RebuildGraph(map, profile);
                return;
            }

            lastAppliedTraversalMask = GetTraversalMask(map, profile);
            LastAppliedBlockedMask = lastAppliedTraversalMask.BlockedCells;
        }

        // Builds or reuses the ship-specific water mask. This contains terrain,
        // draft, and safety-radius expansion in one immutable array.
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

        // Finds a path using the currently displayed traversal mask.
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

        // Native A*: search cell indexes directly, asking HexMapRuntime for
        // neighbors, world-center distances, and water move costs.
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

            if (!CanSearch(start, goal, mask))
            {
                return false;
            }

            int startIndex = activeMap.GetIndex(start.x, start.y);
            int goalIndex = activeMap.GetIndex(goal.x, goal.y);

            if (startIndex == goalIndex)
            {
                outPath.Add(start);
                return true;
            }

            BeginSearch();

            gScore[startIndex] = 0f;
            cameFrom[startIndex] = -1;
            searchVersionByCell[startIndex] = searchVersion;

            openHeap.Clear();
            openHeap.Push(startIndex, CalculateHeuristic(start, goal));

            while (openHeap.Count > 0)
            {
                int currentIndex = openHeap.Pop();

                if (closedVersionByCell[currentIndex] == searchVersion)
                {
                    continue;
                }

                closedVersionByCell[currentIndex] = searchVersion;

                if (currentIndex == goalIndex)
                {
                    RebuildPath(startIndex, goalIndex, outPath);
                    return outPath.Count > 0;
                }

                VisitNeighbors(
                    currentIndex,
                    goal,
                    mask);
            }

            return false;
        }

        // Fails closed when a request does not match the active map or asks for
        // a blocked start/end. That keeps route failures predictable.
        private bool CanSearch(
            Vector2Int start,
            Vector2Int goal,
            NavigationTraversalMask mask)
        {
            return IsReady &&
                   mask != null &&
                   mask.Map == activeMap &&
                   mask.MapVersion == activeMap.Version &&
                   activeMap.InBounds(start.x, start.y) &&
                   activeMap.InBounds(goal.x, goal.y) &&
                   !mask.IsBlocked(start) &&
                   !mask.IsBlocked(goal);
        }

        // Expands one cell. Each neighbor gets a score from:
        // distance already traveled + cost to enter that neighbor + estimate to goal.
        private void VisitNeighbors(
            int currentIndex,
            Vector2Int goal,
            NavigationTraversalMask mask)
        {
            Vector2Int current = IndexToCell(currentIndex);
            int neighborCount = activeMap.GetNeighborCount(
                current.x,
                current.y,
                neighborBuffer);

            for (int i = 0; i < neighborCount; i++)
            {
                Vector2Int neighbor = neighborBuffer[i];

                if (mask.IsBlocked(neighbor))
                {
                    continue;
                }

                int neighborIndex = activeMap.GetIndex(neighbor.x, neighbor.y);

                if (closedVersionByCell[neighborIndex] == searchVersion)
                {
                    continue;
                }

                float tentativeScore =
                    gScore[currentIndex] +
                    CalculateTraversalCost(current, neighbor);

                if (searchVersionByCell[neighborIndex] == searchVersion &&
                    tentativeScore >= gScore[neighborIndex])
                {
                    continue;
                }

                searchVersionByCell[neighborIndex] = searchVersion;
                cameFrom[neighborIndex] = currentIndex;
                gScore[neighborIndex] = tentativeScore;

                float priority =
                    tentativeScore +
                    CalculateHeuristic(neighbor, goal);

                openHeap.Push(neighborIndex, priority);
            }
        }

        // Allocates search memory once per map size. The arrays are reused until
        // the map dimensions change.
        private void EnsureSearchBuffers(int cellCount)
        {
            if (cameFrom != null &&
                cameFrom.Length == cellCount &&
                openHeap != null)
            {
                return;
            }

            cameFrom = new int[cellCount];
            gScore = new float[cellCount];
            searchVersionByCell = new int[cellCount];
            closedVersionByCell = new int[cellCount];
            openHeap = new NativeOpenHeap(cellCount);
            searchVersion = 0;
        }

        // Starts a new query without clearing every cell. If the counter ever
        // wraps, clear the markers and continue from one.
        private void BeginSearch()
        {
            searchVersion++;

            if (searchVersion == int.MaxValue)
            {
                System.Array.Clear(searchVersionByCell, 0, searchVersionByCell.Length);
                System.Array.Clear(closedVersionByCell, 0, closedVersionByCell.Length);
                searchVersion = 1;
            }
        }

        // Cost to enter a neighboring cell. Rough water raises move cost, so A*
        // naturally prefers calmer water when the detour is worth it.
        private float CalculateTraversalCost(Vector2Int from, Vector2Int to)
        {
            Vector2 fromWorld = activeMap.GetWorldCenter(from.x, from.y);
            Vector2 toWorld = activeMap.GetWorldCenter(to.x, to.y);

            float distance = Vector2.Distance(fromWorld, toWorld);
            float moveCost = activeMap.GetMoveCost(to.x, to.y);

            return distance * Mathf.Max(1f, moveCost);
        }

        // Straight-line estimate to the goal. This is optimistic because it
        // ignores obstacles, which is exactly what A* wants from a heuristic.
        private float CalculateHeuristic(Vector2Int from, Vector2Int goal)
        {
            Vector2 fromWorld = activeMap.GetWorldCenter(from.x, from.y);
            Vector2 goalWorld = activeMap.GetWorldCenter(goal.x, goal.y);

            return Vector2.Distance(fromWorld, goalWorld);
        }

        // Once A* reaches the goal, walk backward through cameFrom and flip the
        // result into start-to-goal order.
        private void RebuildPath(
            int startIndex,
            int goalIndex,
            List<Vector2Int> outPath)
        {
            rebuiltPath.Clear();

            int current = goalIndex;
            rebuiltPath.Add(current);

            while (current != startIndex)
            {
                current = cameFrom[current];

                if (current < 0)
                {
                    outPath.Clear();
                    return;
                }

                rebuiltPath.Add(current);
            }

            for (int i = rebuiltPath.Count - 1; i >= 0; i--)
            {
                outPath.Add(IndexToCell(rebuiltPath[i]));
            }
        }

        private Vector2Int IndexToCell(int index)
        {
            return new Vector2Int(
                index % activeMap.Width,
                index / activeMap.Width);
        }

        // Creates the blocked array for one ship profile. First mark forbidden
        // cells, then expand outward by safety radius so ships do not plan routes
        // brushing directly against land.
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

            ExpandBlockedCellsForSafetyRadius(
                map,
                blocked,
                safetySteps);

            return blocked;
        }

        // Breadth-first expansion from every forbidden cell. Think of this as
        // painting a no-sail buffer around terrain.
        private void ExpandBlockedCellsForSafetyRadius(
            HexMapRuntime map,
            bool[] blocked,
            int safetySteps)
        {
            int count = map.Width * map.Height;
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
                    queue.Enqueue(new CellDepth(new Vector2Int(x, y), 0));
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
                    queue.Enqueue(new CellDepth(next, current.Depth + 1));
                }
            }
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

        // Cache key for one immutable mask. Safety radius is reduced to cell
        // steps because two nearby world radii may expand to the same cells.
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

        // Small queue payload used while expanding blocked safety buffers.
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

        // Minimal binary min-heap for A*. It intentionally allows duplicate cell
        // entries; closedVersionByCell discards stale entries when popped. The
        // array grows instead of dropping pushes, because a dropped lower-cost
        // entry can turn a valid route into a false failure.
        private sealed class NativeOpenHeap
        {
            private HeapNode[] nodes;
            private int count;

            public int Count => count;

            public NativeOpenHeap(int capacity)
            {
                nodes = new HeapNode[Mathf.Max(1, capacity)];
            }

            public void Clear()
            {
                count = 0;
            }

            public void Push(int cellIndex, float priority)
            {
                EnsureCapacity(count + 1);

                int index = count++;
                nodes[index] = new HeapNode(cellIndex, priority);

                while (index > 0)
                {
                    int parent = (index - 1) / 2;

                    if (nodes[parent].Priority <= nodes[index].Priority)
                    {
                        break;
                    }

                    Swap(parent, index);
                    index = parent;
                }
            }

            public int Pop()
            {
                int result = nodes[0].CellIndex;
                count--;

                if (count > 0)
                {
                    nodes[0] = nodes[count];
                    HeapifyDown(0);
                }

                return result;
            }

            private void HeapifyDown(int index)
            {
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    int smallest = index;

                    if (left < count &&
                        nodes[left].Priority < nodes[smallest].Priority)
                    {
                        smallest = left;
                    }

                    if (right < count &&
                        nodes[right].Priority < nodes[smallest].Priority)
                    {
                        smallest = right;
                    }

                    if (smallest == index)
                    {
                        return;
                    }

                    Swap(index, smallest);
                    index = smallest;
                }
            }

            private void EnsureCapacity(int requested)
            {
                if (requested <= nodes.Length)
                {
                    return;
                }

                int newCapacity = Mathf.Max(
                    requested,
                    nodes.Length * 2);

                System.Array.Resize(ref nodes, newCapacity);
            }

            private void Swap(int a, int b)
            {
                HeapNode temp = nodes[a];
                nodes[a] = nodes[b];
                nodes[b] = temp;
            }

            private struct HeapNode
            {
                public int CellIndex;
                public float Priority;

                public HeapNode(int cellIndex, float priority)
                {
                    CellIndex = cellIndex;
                    Priority = priority;
                }
            }
        }
    }
}
