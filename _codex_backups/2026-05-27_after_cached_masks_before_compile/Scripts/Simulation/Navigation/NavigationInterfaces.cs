// NavigationInterfaces.cs:
// Tiny handshake file so map/path logic does not care whether the grid is drawn
// with Tilemaps, TGS, or whatever else.
using System.Collections.Generic;
using UnityEngine;

namespace OA.Simulation.Navigation
{
    // Path service contract: build a graph from a hex map and answer cell-to-cell route questions.
    public interface INavigationPathService
    {
        // True once the backing pathfinder has a graph ready to query.
        bool IsReady { get; }

        // Last mask applied after unit safety-radius expansion, mostly for matching debug visuals.
        bool[] LastAppliedBlockedMask { get; }
        int TraversalMaskCacheHits { get; }
        int TraversalMaskCacheMisses { get; }

        // Rebuilds the path graph from map data and the current unit safety radius or the units profile, depending on what is in use.
        void RebuildGraph(HexMapRuntime map, float safetyRadiusWorld);
        void RebuildGraph(HexMapRuntime map, NavigationProfile profile);

        // Applies a new ship draft/safety mask without reconstructing graph nodes or connections.
        void ApplyTraversalProfile(HexMapRuntime map, NavigationProfile profile);

        // Gets an immutable ship-specific water mask without changing graph walkability.
        NavigationTraversalMask GetTraversalMask(
            HexMapRuntime map,
            NavigationProfile profile);

        // Finds a route between two cells and writes it into the provided output list.
        bool TryFindPath(Vector2Int start, Vector2Int goal, List<Vector2Int> outPath);
        bool TryFindPath(
            Vector2Int start,
            Vector2Int goal,
            NavigationTraversalMask mask,
            List<Vector2Int> outPath);
    }

    // Grid presenter contract: draw the map and translate world clicks back into hex cells.
    public interface INavigationGridPresenter
    {
        // Builds or repaints the visible grid from the runtime map.
        void BuildOrRefresh(HexMapRuntime map, bool[] blockedMask = null);

        // Converts a world-space position into the matching grid cell if possible.
        bool TryWorldToCell(Vector2 worldPosition, out Vector2Int cell);
    }
}
