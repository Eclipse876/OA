using System.Collections.Generic;
using UnityEngine;

namespace OA.Simulation.Navigation
{
    public interface INavigationPathService
    {
        bool IsReady { get; }
        bool[] LastAppliedBlockedMask { get; }

        void RebuildGraph(HexMapRuntime map, float safetyRadiusWorld);
        bool TryFindPath(Vector2Int start, Vector2Int goal, List<Vector2Int> outPath);
    }

    public interface INavigationGridPresenter
    {
        void BuildOrRefresh(HexMapRuntime map, bool[] blockedMask = null);
        bool TryWorldToCell(Vector2 worldPosition, out Vector2Int cell);
    }
}
