// NavigationInterfaces.cs:
// Tiny handshake file so map/path logic does not care whether the grid is drawn
// with Tilemaps, TGS, or whatever other idea we try at 1 AM.
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

        // Rebuilds the path graph from map data and the current unit safety radius.
        void RebuildGraph(HexMapRuntime map, float safetyRadiusWorld);
        // Finds a route between two cells and writes it into the provided output list.
        bool TryFindPath(Vector2Int start, Vector2Int goal, List<Vector2Int> outPath);
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
