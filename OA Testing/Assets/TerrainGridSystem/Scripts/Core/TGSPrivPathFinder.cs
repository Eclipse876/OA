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
        bool clearanceBordersAsObstacles;
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
            ComputeClearance(cellGroupMask, false);
        }

        public void ComputeClearance(int cellGroupMask, bool gridBordersAsObstacles) {

            lock (clearanceLock) {
                if (clearanceComputed && clearanceCellGroupMask == cellGroupMask && clearanceBordersAsObstacles == gridBordersAsObstacles) return;

                clearanceComputed = true;
                clearanceCellGroupMask = cellGroupMask;
                clearanceBordersAsObstacles = gridBordersAsObstacles;

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

                if (_gridTopology == GridTopology.Hexagonal) {
                    ComputeClearanceHexDisk(cellGroupMask, gridBordersAsObstacles);
                } else {
                    ComputeClearanceBox(cellGroupMask, gridBordersAsObstacles);
                }
            }
        }

        void ComputeClearanceBox(int cellGroupMask, bool gridBordersAsObstacles) {
            int maxDim = Mathf.Max(rowCount, columnCount);
            // uses true clearance (axis-aligned square in row/column space; valid for box grids only)
            for (int j = rowCount - 1; j >= 0; j--) {
                for (int k = 0; k < columnCount; k++) {
                    Cell cell = CellGetAtPosition(k, j);
                    if (cell == null) continue;
                    byte clearanceValue = (byte)Mathf.Min(maxDim - 1, 255);
                    for (int maxClearance = 2; maxClearance < maxDim; maxClearance++) {
                        bool blocked = false;
                        int maxIter = maxClearance * maxClearance;
                        for (int i = 1; i < maxIter; i++) {
                            int nj = j - (i / maxClearance);
                            int nk = k + (i % maxClearance);
                            if (nj < 0 || nk >= columnCount) {
                                if (gridBordersAsObstacles) {
                                    blocked = true;
                                    break;
                                }
                                continue;
                            }
                            Cell neighbour = CellGetAtPosition(nk, nj);
                            if (neighbour == null || (neighbour.group & cellGroupMask) == 0 || !neighbour.canCross) {
                                blocked = true;
                                break;
                            }
                        }
                        if (blocked) {
                            clearanceValue = (byte)(maxClearance - 1);
                            break;
                        }
                    }
                    cell.clearance = clearanceValue;
                    clearanceCache[cell.index] = clearanceValue;
                }
            }
        }

        void ComputeClearanceHexDisk(int cellGroupMask, bool gridBordersAsObstacles) {
            int maxDim = Mathf.Max(rowCount, columnCount);

            for (int row = 0; row < rowCount; row++) {
                for (int col = 0; col < columnCount; col++) {
                    Cell cell = CellGetAtPosition(col, row);
                    if (cell == null) continue;

                    if (!IsClearanceTraversable(cell, cellGroupMask)) {
                        continue;
                    }

                    ClearanceRowColToAxial(row, col, out int ax, out int ay);

                    // clearance 1 = disk of hex-radius 0 (the cell itself); increment while full rings fit
                    byte clearance = 1;
                    for (int ring = 1; ring < maxDim; ring++) {
                        if (!IsHexRingClear(ax, ay, ring, cellGroupMask, gridBordersAsObstacles)) {
                            break;
                        }
                        clearance = (byte)(ring + 1);
                    }

                    cell.clearance = clearance;
                    clearanceCache[cell.index] = clearance;
                }
            }
        }

        static bool IsClearanceTraversable(Cell cell, int cellGroupMask) {
            return cell != null && cell.canCross && (cell.group & cellGroupMask) != 0;
        }

        void ClearanceRowColToAxial(int row, int col, out int ax, out int ay) {
            int offset = _evenLayout ? 0 : 1;
            if (_pointyTopHexagons) {
                ay = row;
                ax = col - Mathf.FloorToInt((row + offset) / 2f);
            } else {
                ax = col;
                ay = row - Mathf.FloorToInt((col + offset) / 2f);
            }
        }

        bool ClearanceAxialToRowCol(int ax, int ay, out int row, out int col) {
            int offset = _evenLayout ? 0 : 1;
            if (_pointyTopHexagons) {
                row = ay;
                col = ax + Mathf.FloorToInt((ay + offset) / 2f);
            } else {
                col = ax;
                row = ay + Mathf.FloorToInt((ax + offset) / 2f);
            }
            return row >= 0 && row < rowCount && col >= 0 && col < columnCount;
        }

        bool IsHexRingClear(int x0, int y0, int ring, int cellGroupMask, bool gridBordersAsObstacles) {
            for (int dq = -ring; dq <= ring; dq++) {
                int drStart = Math.Max(-ring, -dq - ring);
                int drEnd = Math.Min(ring, -dq + ring);
                for (int dr = drStart; dr <= drEnd; dr++) {
                    int dist = Math.Max(Math.Abs(dq), Math.Max(Math.Abs(dr), Math.Abs(dq + dr)));
                    if (dist != ring) {
                        continue;
                    }
                    int ax = x0 + dq;
                    int ay = y0 + dr;
                    if (!ClearanceAxialToRowCol(ax, ay, out int row, out int col)) {
                        if (gridBordersAsObstacles) {
                            return false;
                        }
                        continue;
                    }
                    Cell neighbour = CellGetAtPosition(col, row);
                    if (!IsClearanceTraversable(neighbour, cellGroupMask)) {
                        return false;
                    }
                }
            }
            return true;
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
