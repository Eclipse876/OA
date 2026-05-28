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
