using UnityEngine;
using System;
using System.Threading;
using TGS.PathFinding;

namespace TGS
{

    public partial class TerrainGridSystem : MonoBehaviour {

        // Thread-local pathfinder instances for thread-safe concurrent pathfinding
        ThreadLocal<IPathFinder> finderPool;
        bool needRefreshRouteMatrix = true;

        // Cached cell array for thread-safe access
        Cell[] cachedCellsArray;
        int cachedCellColumnCount;
        int cachedCellRowCount;
        GridTopology cachedGridTopology;

        // Clearance cache (thread-safe: computed once, read by all threads)
        byte[] clearanceCache;
        bool clearanceComputed;
        int clearanceCellGroupMask;
        readonly object clearanceLock = new object();

        IPathFinder GetPathFinder() {
            if (finderPool == null) {
                finderPool = new ThreadLocal<IPathFinder>(() => CreatePathFinder(), trackAllValues: false);
            }
            return finderPool.Value;
        }

        IPathFinder CreatePathFinder() {
            Cell[] cellsArray = cachedCellsArray;
            if (cellsArray == null) return null;

            if (cachedGridTopology == GridTopology.Irregular) {
                return new PathFinderFastIrregular(cellsArray);
            } else {
                if ((cachedCellColumnCount & (cachedCellColumnCount - 1)) == 0) { // is power of two?
                    return new PathFinderFast(cellsArray, cachedCellColumnCount, cachedCellRowCount);
                } else {
                    return new PathFinderFastNonSQR(cellsArray, cachedCellColumnCount, cachedCellRowCount);
                }
            }
        }

        void ComputeRouteMatrix() {

            // prepare matrix
            if (!needRefreshRouteMatrix)
                return;

            needRefreshRouteMatrix = false;

            // Cache grid configuration for thread-safe pathfinder creation
            cachedCellsArray = cells.ToArray();
            cachedCellColumnCount = _cellColumnCount;
            cachedCellRowCount = _cellRowCount;
            cachedGridTopology = _gridTopology;

            // Dispose old ThreadLocal and create new one to ensure all threads get fresh pathfinders
            if (finderPool != null) {
                finderPool.Dispose();
                finderPool = null;
            }

            // Invalidate clearance cache when grid changes
            lock (clearanceLock) {
                clearanceComputed = false;
                clearanceCache = null;
            }
        }


        /// <summary>
        /// Updates clearance data for each cell. Clearance is used with FindPath method (minClearance parameter) and it's used to specify the minimum width of a path.
        /// Thread-safe: computes once and caches in a byte array.
        /// </summary>
        public void ComputeClearance(int cellGroupMask) {

            lock (clearanceLock) {
                if (clearanceComputed && clearanceCellGroupMask == cellGroupMask) return;

                clearanceComputed = true;
                clearanceCellGroupMask = cellGroupMask;

                int cellsCount = cells.Count;

                // Allocate or reuse clearance cache
                if (clearanceCache == null || clearanceCache.Length != cellsCount) {
                    clearanceCache = new byte[cellsCount];
                } else {
                    Array.Clear(clearanceCache, 0, cellsCount);
                }

                // Also update cell.clearance for backward compatibility
                for (int k = 0; k < cellsCount; k++) {
                    cells[k].clearance = 0;
                }

                int maxDim = Mathf.Max(rowCount, columnCount);
                // uses true clearance
                for (int j = rowCount - 1; j >= 0; j--) {
                    for (int k = 0; k < columnCount; k++) {
                        Cell cell = CellGetAtPosition(k, j);
                        if (cell == null) continue;
                        for (int maxClearance = 2; maxClearance < maxDim; maxClearance++) {
                            bool blocked = false;
                            int maxIter = maxClearance * maxClearance;
                            for (int i = 1; i < maxIter; i++) {
                                int nj = j - (i / maxClearance);
                                int nk = k + (i % maxClearance);
                                if (nj < 0 || nk >= columnCount) {
                                    blocked = true;
                                    break;
                                }
                                Cell neighbour = CellGetAtPosition(nk, nj);
                                if (neighbour == null || (neighbour.group & cellGroupMask) == 0 || !neighbour.canCross) {
                                    blocked = true;
                                    break;
                                }
                            }
                            if (blocked) {
                                byte clearanceValue = (byte)(maxClearance - 1);
                                cell.clearance = clearanceValue;
                                clearanceCache[cell.index] = clearanceValue;
                                break;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets the current clearance cache array. Thread-safe.
        /// Returns null if clearance has not been computed.
        /// </summary>
        internal byte[] GetClearanceCache() {
            lock (clearanceLock) {
                return clearanceCache;
            }
        }
    }

}