// NavigationTraversalMask.cs:
// Immutable per-ship water access data. Routes can compare draft and clearance
// alternatives without rewriting the shared A* graph under every query.
using UnityEngine;

namespace OA.Simulation.Navigation
{
    public sealed class NavigationTraversalMask
    {
        public HexMapRuntime Map { get; }
        public int MapVersion { get; }
        public NavigationProfile Profile { get; }
        public bool[] BlockedCells { get; }

        // Built once per immutable mask. A legal tile in a different ocean/lake
        // is not a usable destination for this ship.
        private int[] regions;

        public bool TryFindClosestReachableCell(
            Vector2Int start, Vector2 requestedWorld, out Vector2Int destination)
        {
            destination = default;
            if (Map == null || !Map.HasWorldCenters || Map.Version != MapVersion ||
                IsBlocked(start) || float.IsNaN(requestedWorld.x) ||
                float.IsNaN(requestedWorld.y) || float.IsInfinity(requestedWorld.x) ||
                float.IsInfinity(requestedWorld.y))
                return false;

            EnsureRegions();
            int region = regions[Map.GetIndex(start.x, start.y)];
            float bestDistance = float.PositiveInfinity;
            int bestIndex = -1;
            for (int index = 0; index < regions.Length; index++)
            {
                if (regions[index] != region) continue;
                int x = index % Map.Width;
                int y = index / Map.Width;
                float distance = (Map.GetWorldCenter(x, y) - requestedWorld).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestIndex = index;
            }

            if (bestIndex < 0) return false;
            destination = new Vector2Int(bestIndex % Map.Width, bestIndex / Map.Width);
            return true;
        }

        private void EnsureRegions()
        {
            if (regions != null) return;
            int count = Map.Width * Map.Height;
            regions = new int[count];
            int[] queue = new int[count];
            Vector2Int[] neighbors = new Vector2Int[6];
            int region = 0;
            for (int seed = 0; seed < count; seed++)
            {
                if (regions[seed] != 0 || IsBlocked(new Vector2Int(seed % Map.Width, seed / Map.Width)))
                    continue;
                regions[seed] = ++region;
                int head = 0;
                int tail = 0;
                queue[tail++] = seed;
                while (head < tail)
                {
                    int current = queue[head++];
                    int neighborCount = Map.GetNeighborCount(current % Map.Width, current / Map.Width, neighbors);
                    for (int i = 0; i < neighborCount; i++)
                    {
                        Vector2Int next = neighbors[i];
                        int index = Map.GetIndex(next.x, next.y);
                        if (regions[index] != 0 || IsBlocked(next)) continue;
                        regions[index] = region;
                        queue[tail++] = index;
                    }
                }
            }
        }

        public NavigationTraversalMask(
            HexMapRuntime map,
            int mapVersion,
            NavigationProfile profile,
            bool[] blockedCells)
        {
            Map = map;
            MapVersion = mapVersion;
            Profile = profile;
            BlockedCells = blockedCells ?? System.Array.Empty<bool>();
        }

        public bool IsBlocked(Vector2Int cell)
        {
            if (Map == null || !Map.InBounds(cell.x, cell.y))
            {
                return true;
            }

            int index = Map.GetIndex(cell.x, cell.y);
            return index < 0 ||
                   index >= BlockedCells.Length ||
                   BlockedCells[index];
        }
    }
}
