************************************
*   DEMO 34: ASYNC PATHFINDING     *
************************************

This demo demonstrates the thread-safe async pathfinding API.

FEATURES:
- FindPathAsync() runs pathfinding on a background thread
- Multiple concurrent requests can run simultaneously without race conditions
- Results are returned via callback on the main thread
- No cell mutation during pathfinding ensures thread safety

USAGE:
1. Press SPACE to launch 6 concurrent pathfinding requests
2. Watch as all paths are computed in parallel and displayed
3. Press C to clear and try again

API EXAMPLE:
    // Launch async pathfinding
    tgs.FindPathAsync(startCellIndex, endCellIndex, result => {
        if (result.success) {
            // Use result.path (List<int>) and result.totalCost
        }
    });

    // Or use async/await pattern
    var result = await tgs.FindPathAsync(startCellIndex, endCellIndex);

IMPORTANT:
- Do not modify grid structure while async pathfinding is in progress
- OnPathFindingCrossCell callback is not supported in async mode
- The synchronous FindPath() methods still work as before (backward compatible)
