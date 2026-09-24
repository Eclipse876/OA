#pragma warning disable UDR0001
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TGS.PathFinding;

namespace TGS {

    public enum CanCrossCheckType {
        Default = 0,
        IgnoreCanCrossCheckOnAllCells = 1,
        IgnoreCanCrossCheckOnStartAndEndCells = 2,
        IgnoreCanCrossCheckOnStartCell = 3,
        IgnoreCanCrossCheckOnEndCell = 4,
        IgnoreCanCrossCheckOnAllCellsExceptStartAndEndCells = 5,
    }

    public class FindPathOptions {
        public float maxSearchCost;
        public int maxSteps;
        public int cellGroupMask = -1;
        public bool cellGroupMaskExactComparison;
        public CanCrossCheckType canCrossCheckType = CanCrossCheckType.Default;
        public bool ignoreCellCosts;
        public bool includeInvisibleCells = true;
        public int minClearance = 1;
        public bool gridBordersAsObstacles;
        public float maxCellCrossCost = float.MaxValue;
        public PathFindingEvent OnPathFindingCrossCell;
        public object OnPathFindingCrossCellData;
    }

    /// <summary>
    /// Result of an async pathfinding operation
    /// </summary>
    public struct FindPathAsyncResult {
        public List<int> path;
        public float totalCost;
        public bool success;
    }


    public partial class TerrainGridSystem : MonoBehaviour {


        [SerializeField]
        HeuristicFormula
            _pathFindingHeuristicFormula = HeuristicFormula.DiagonalShortCut;

        /// <summary>
        /// The path finding heuristic formula to estimate distance from current position to destination
        /// </summary>
        public PathFinding.HeuristicFormula pathFindingHeuristicFormula {
            get { return _pathFindingHeuristicFormula; }
            set {
                if (value != _pathFindingHeuristicFormula) {
                    _pathFindingHeuristicFormula = value;
                    isDirty = true;
                }
            }
        }

        [SerializeField]
        int
            _pathFindingMaxSteps = 2000;

        /// <summary>
        /// The maximum number of steps that a path can return.
        /// </summary>
        public int pathFindingMaxSteps {
            get { return _pathFindingMaxSteps; }
            set {
                if (value != _pathFindingMaxSteps) {
                    _pathFindingMaxSteps = value;
                    isDirty = true;
                }
            }
        }

        [SerializeField]
        float
            _pathFindingMaxCost = 200000;

        /// <summary>
        /// The maximum search cost of the path finding execution.
        /// </summary>
        public float pathFindingMaxCost {
            get { return _pathFindingMaxCost; }
            set {
                if (value != _pathFindingMaxCost) {
                    _pathFindingMaxCost = value;
                    isDirty = true;
                }
            }
        }

        [SerializeField]
        bool
            _pathFindingUseDiagonals = true;

        /// <summary>
        /// If path can include diagonals between cells
        /// </summary>
        public bool pathFindingUseDiagonals {
            get { return _pathFindingUseDiagonals; }
            set {
                if (value != _pathFindingUseDiagonals) {
                    _pathFindingUseDiagonals = value;
                    isDirty = true;
                }
            }
        }

        [SerializeField]
        float
            _pathFindingHeavyDiagonalsCost = 1.4f;

        /// <summary>
        /// The cost for crossing diagonals.
        /// </summary>
        public float pathFindingHeavyDiagonalsCost {
            get { return _pathFindingHeavyDiagonalsCost; }
            set {
                if (value != _pathFindingHeavyDiagonalsCost) {
                    _pathFindingHeavyDiagonalsCost = value;
                    isDirty = true;
                }
            }
        }

        [SerializeField]
        float
            _pathFindingTurnCost;

        /// <summary>
        /// Extra cost added each time the path changes direction (box grids only). 0 = disabled (default, preserves legacy behaviour).
        /// A small value (e.g. 0.001) acts as a tie-breaker that prefers straighter paths among equal-cost routes; larger values avoid turns even at higher movement cost.
        /// </summary>
        public float pathFindingTurnCost {
            get { return _pathFindingTurnCost; }
            set {
                if (value != _pathFindingTurnCost) {
                    _pathFindingTurnCost = value;
                    isDirty = true;
                }
            }
        }



        [SerializeField]
        bool
            _pathFindingIncludeInvisibleCells = true;

        /// <summary>
        /// If true, the path will include invisible cells as well.
        /// </summary>
        public bool pathFindingIncludeInvisibleCells {
            get { return _pathFindingIncludeInvisibleCells; }
            set {
                if (value != _pathFindingIncludeInvisibleCells) {
                    _pathFindingIncludeInvisibleCells = value;
                    isDirty = true;
                }
            }
        }


        #region Public Path Finding functions

        readonly FindPathOptions defaultOptions = new();

        /// <summary>
        /// Returns an optimal path from startPosition to endPosition with options.
        /// </summary>
        /// <returns>The route consisting of a list of cell indexes.</returns>
        /// <param name="maxSearchCost">Maximum search cost for the path finding algorithm. A value of 0 will use the global default defined by pathFindingMaxCost</param>
        /// <param name="maxSteps">Maximum steps for the path. A value of 0 will use the global default defined by pathFindingMaxSteps</param>
        /// <param name="maxCellCrossCost">The maximum allowed crossing cost of any cell</param>
        public List<int> FindPath(int cellIndexStart, int cellIndexEnd, float maxSearchCost = 0, int maxSteps = 0, int cellGroupMask = -1, CanCrossCheckType canCrossCheckType = CanCrossCheckType.Default, bool ignoreCellCosts = false, bool includeInvisibleCells = true, int minClearance = 1, float maxCellCrossCost = float.MaxValue, bool cellGroupMaskExactComparison = false, bool gridBordersAsObstacles = false) {
            return FindPath(cellIndexStart, cellIndexEnd, out _, maxSearchCost, maxSteps, cellGroupMask, canCrossCheckType, ignoreCellCosts, includeInvisibleCells, minClearance, maxCellCrossCost, cellGroupMaskExactComparison, gridBordersAsObstacles);
        }

        /// <summary>
		/// Returns an optimal path from startPosition to endPosition with options.
		/// </summary>
		/// <returns>The route consisting of a list of cell indexes.</returns>
		/// <param name="totalCost">The total accumulated cost for the path</param>
		/// <param name="maxSearchCost">Maximum search cost for the path finding algorithm. A value of 0 will use the global default defined by pathFindingMaxCost</param>
		/// <param name="maxSteps">Maximum steps for the path. A value of 0 will use the global default defined by pathFindingMaxSteps</param>
        /// <param name="maxCellCrossCost">The maximum allowed crossing cost of any cell</param>
		public List<int> FindPath(int cellIndexStart, int cellIndexEnd, out float totalCost, float maxSearchCost = 0, int maxSteps = 0, int cellGroupMask = -1, CanCrossCheckType canCrossCheckType = CanCrossCheckType.Default, bool ignoreCellCosts = false, bool includeInvisibleCells = true, int minClearance = 1, float maxCellCrossCost = float.MaxValue, bool cellGroupMaskExactComparison = false, bool gridBordersAsObstacles = false) {
            List<int> results = new();
            FindPath(cellIndexStart, cellIndexEnd, results, out totalCost, maxSearchCost, maxSteps, cellGroupMask, canCrossCheckType, ignoreCellCosts, includeInvisibleCells, minClearance, maxCellCrossCost, cellGroupMaskExactComparison, gridBordersAsObstacles);
            return results;
        }


        /// <summary>
        /// Returns an optimal path from startPosition to endPosition with options.
        /// </summary>
        /// <returns>The route consisting of a list of cell indexes.</returns>
        /// <param name="cellIndices">User provided list to fill with path indices</param>
        /// <param name="totalCost">The total accumulated cost for the path</param>
        /// <param name="maxSearchCost">Maximum search cost for the path finding algorithm. A value of 0 will use the global default defined by pathFindingMaxCost</param>
        /// <param name="maxSteps">Maximum steps for the path. A value of 0 will use the global default defined by pathFindingMaxSteps</param>
        /// <param name="maxCellCrossCost">The maximum allowed crossing cost of any cell</param>
        public int FindPath(int cellIndexStart, int cellIndexEnd, List<int> cellIndices, out float totalCost, float maxSearchCost = 0, int maxSteps = 0, int cellGroupMask = -1, CanCrossCheckType canCrossCheckType = CanCrossCheckType.Default, bool ignoreCellCosts = false, bool includeInvisibleCells = true, int minClearance = 1, float maxCellCrossCost = float.MaxValue, bool cellGroupMaskExactComparison = false, bool gridBordersAsObstacles = false) {
            defaultOptions.maxSearchCost = maxSearchCost;
            defaultOptions.maxSteps = maxSteps;
            defaultOptions.cellGroupMask = cellGroupMask;
            defaultOptions.cellGroupMaskExactComparison = cellGroupMaskExactComparison;
            defaultOptions.canCrossCheckType = canCrossCheckType;
            defaultOptions.ignoreCellCosts = ignoreCellCosts;
            defaultOptions.includeInvisibleCells = includeInvisibleCells;
            defaultOptions.minClearance = minClearance;
            defaultOptions.gridBordersAsObstacles = gridBordersAsObstacles;
            defaultOptions.maxCellCrossCost = maxCellCrossCost;
            defaultOptions.OnPathFindingCrossCell = null;
            defaultOptions.OnPathFindingCrossCellData = null;
            return FindPath(cellIndexStart, cellIndexEnd, cellIndices, out totalCost, defaultOptions);
        }

        public FindPathOptions GetDefaultOptions() {
            defaultOptions.maxSearchCost = 0;
            defaultOptions.maxSteps = 0;
            defaultOptions.cellGroupMask = -1;
            defaultOptions.cellGroupMaskExactComparison = false;
            defaultOptions.canCrossCheckType = CanCrossCheckType.Default;
            defaultOptions.ignoreCellCosts = false;
            defaultOptions.includeInvisibleCells = true;
            defaultOptions.minClearance = 1;
            defaultOptions.gridBordersAsObstacles = false;
            defaultOptions.maxCellCrossCost = float.MaxValue;
            defaultOptions.OnPathFindingCrossCell = null;
            defaultOptions.OnPathFindingCrossCellData = null;
            return defaultOptions;
        }


        /// <summary>
        /// Returns an optimal path from startPosition to endPosition with options.
        /// </summary>
        /// <returns>The route consisting of a list of cell indexes.</returns>
        /// <param name="cellIndices">User provided list to fill with path indices</param>
        /// <param name="totalCost">The total accumulated cost for the path</param>
        /// <param name="options">A FindPathOptions object that contains options for the path finding execution</param>
        public int FindPath(int cellIndexStart, int cellIndexEnd, List<int> cellIndices, out float totalCost, FindPathOptions options) {
            totalCost = 0;
            if (cellIndices == null) return 0;
            if (options == null) options = GetDefaultOptions();
            cellIndices.Clear();
            if (cellIndexStart == cellIndexEnd || cellIndexStart < 0 || cellIndexEnd < 0 || cellIndexStart >= cells.Count || cellIndexEnd >= cells.Count) return 0;
            Cell startCell = cells[cellIndexStart];
            Cell endCell = cells[cellIndexEnd];
            if (startCell == null || endCell == null) return 0;

            // Thread-safe: check canCross without mutation, let pathfinder handle exceptions
            if (options.canCrossCheckType != CanCrossCheckType.IgnoreCanCrossCheckOnAllCells) {
                switch (options.canCrossCheckType) {
                    case CanCrossCheckType.IgnoreCanCrossCheckOnStartAndEndCells:
                        // Pathfinder will handle the exception for both cells
                        break;
                    case CanCrossCheckType.IgnoreCanCrossCheckOnStartCell:
                        if (!endCell.canCross) return 0;
                        break;
                    case CanCrossCheckType.IgnoreCanCrossCheckOnEndCell:
                        if (!startCell.canCross) return 0;
                        break;
                    default:
                        if (!startCell.canCross || !endCell.canCross)
                            return 0;
                        break;
                }
            }

            if (options.minClearance > 1 && (needRefreshRouteMatrix || !clearanceComputed || clearanceBordersAsObstacles != options.gridBordersAsObstacles)) {
                ComputeClearance(options.cellGroupMask, options.gridBordersAsObstacles);
            }
            ComputeRouteMatrix();

            IPathFinder finder = GetPathFinder();
            if (finder == null) return 0;

            ConfigurePathFinder(finder, cellIndexStart, cellIndexEnd, options);

            List<PathFinderNode> route = finder.FindPath(this, startCell, endCell, out totalCost, _evenLayout);
            if (route != null) {
                int routeCount = route.Count;
                if (_gridTopology == GridTopology.Irregular) {
                    for (int r = routeCount - 2; r >= 0; r--) {
                        cellIndices.Add(route[r].PX);
                    }
                } else {
                    for (int r = routeCount - 2; r >= 0; r--) {
                        int cellIndex = route[r].PY * _cellColumnCount + route[r].PX;
                        cellIndices.Add(cellIndex);
                    }
                }
                cellIndices.Add(cellIndexEnd);
            } else {
                return 0;    // no route available
            }
            return cellIndices.Count;
        }

        void ConfigurePathFinder(IPathFinder finder, int cellIndexStart, int cellIndexEnd, FindPathOptions options) {
            finder.Formula = _pathFindingHeuristicFormula;
            finder.MaxSteps = options.maxSteps > 0 ? options.maxSteps : _pathFindingMaxSteps;
            finder.Diagonals = _pathFindingUseDiagonals;
            finder.HeavyDiagonalsCost = _pathFindingHeavyDiagonalsCost;
            finder.TurnCost = _pathFindingTurnCost;
            switch (_gridTopology) {
                case GridTopology.Irregular: finder.CellShape = CellType.Irregular; break;
                case GridTopology.Hexagonal: finder.CellShape = _pointyTopHexagons ? CellType.PointyTopHexagon : CellType.FlatTopHexagon; break;
                default: finder.CellShape = CellType.Box; break;
            }
            finder.MaxSearchCost = options.maxSearchCost > 0 ? options.maxSearchCost : _pathFindingMaxCost;
            finder.CellGroupMask = options.cellGroupMask;
            finder.CellGroupMaskExactComparison = options.cellGroupMaskExactComparison;
            finder.IgnoreCanCrossCheck = options.canCrossCheckType == CanCrossCheckType.IgnoreCanCrossCheckOnAllCells || options.canCrossCheckType == CanCrossCheckType.IgnoreCanCrossCheckOnAllCellsExceptStartAndEndCells;
            finder.IgnoreCellCost = options.ignoreCellCosts;
            finder.IncludeInvisibleCells = options.includeInvisibleCells;
            finder.MinClearance = options.minClearance;
            finder.MaxCellCrossCost = options.maxCellCrossCost;
            finder.Data = options.OnPathFindingCrossCellData;

            // Thread-safe: pass start/end cell indices to pathfinder for exception handling
            finder.StartCellIndex = cellIndexStart;
            finder.EndCellIndex = cellIndexEnd;
            finder.CanCrossCheckType = options.canCrossCheckType;
            finder.ClearanceData = options.minClearance > 1 ? GetClearanceCache() : null;

            if (options.OnPathFindingCrossCell != null) {
                finder.OnCellCross = options.OnPathFindingCrossCell;
            } else if (OnPathFindingCrossCell != null) { 
                finder.OnCellCross = OnPathFindingCrossCell;
            } else {
                finder.OnCellCross = null;
            }
        }

        #endregion

        #region Async Path Finding

        // Main thread synchronization context for callbacks
        static SynchronizationContext mainThreadContext;


        /// <summary>
        /// Finds a path asynchronously on a background thread. Thread-safe.
        /// Results are returned via callback on the main thread.
        /// IMPORTANT: Do not modify grid structure while async pathfinding is in progress.
        /// </summary>
        /// <param name="cellIndexStart">Start cell index</param>
        /// <param name="cellIndexEnd">End cell index</param>
        /// <param name="onComplete">Callback invoked on main thread with path results</param>
        /// <param name="options">Pathfinding options (optional)</param>
        public void FindPathAsync(int cellIndexStart, int cellIndexEnd, Action<FindPathAsyncResult> onComplete, FindPathOptions options = null) {
            if (onComplete == null) return;
            if (options == null) options = GetDefaultOptions();

            // Capture main thread context if not already done
            if (mainThreadContext == null) {
                mainThreadContext = SynchronizationContext.Current;
            }

            // Validate parameters on main thread
            if (cellIndexStart == cellIndexEnd || cellIndexStart < 0 || cellIndexEnd < 0 || 
                cellIndexStart >= cells.Count || cellIndexEnd >= cells.Count) {
                onComplete(new FindPathAsyncResult { path = null, totalCost = 0, success = false });
                return;
            }

            Cell startCell = cells[cellIndexStart];
            Cell endCell = cells[cellIndexEnd];
            if (startCell == null || endCell == null) {
                onComplete(new FindPathAsyncResult { path = null, totalCost = 0, success = false });
                return;
            }

            // Check canCross without mutation
            if (options.canCrossCheckType != CanCrossCheckType.IgnoreCanCrossCheckOnAllCells) {
                switch (options.canCrossCheckType) {
                    case CanCrossCheckType.IgnoreCanCrossCheckOnStartCell:
                        if (!endCell.canCross) {
                            onComplete(new FindPathAsyncResult { path = null, totalCost = 0, success = false });
                            return;
                        }
                        break;
                    case CanCrossCheckType.IgnoreCanCrossCheckOnEndCell:
                        if (!startCell.canCross) {
                            onComplete(new FindPathAsyncResult { path = null, totalCost = 0, success = false });
                            return;
                        }
                        break;
                    case CanCrossCheckType.Default:
                        if (!startCell.canCross || !endCell.canCross) {
                            onComplete(new FindPathAsyncResult { path = null, totalCost = 0, success = false });
                            return;
                        }
                        break;
                }
            }

            // Ensure route matrix and clearance are computed on main thread
            if (options.minClearance > 1 && (needRefreshRouteMatrix || !clearanceComputed || clearanceBordersAsObstacles != options.gridBordersAsObstacles)) {
                ComputeClearance(options.cellGroupMask, options.gridBordersAsObstacles);
            }
            ComputeRouteMatrix();

            // Capture snapshot data for thread-safe access
            Cell[] cellsSnapshot = cachedCellsArray;
            byte[] clearanceSnapshot = options.minClearance > 1 ? GetClearanceCache() : null;
            int columnCount = _cellColumnCount;
            int rowCount = _cellRowCount;
            GridTopology topology = _gridTopology;
            bool evenLayout = _evenLayout;
            bool pointyTopHex = _pointyTopHexagons;
            HeuristicFormula formula = _pathFindingHeuristicFormula;
            int maxSteps = options.maxSteps > 0 ? options.maxSteps : _pathFindingMaxSteps;
            float maxSearchCost = options.maxSearchCost > 0 ? options.maxSearchCost : _pathFindingMaxCost;
            bool useDiagonals = _pathFindingUseDiagonals;
            float heavyDiagonalsCost = _pathFindingHeavyDiagonalsCost;
            float turnCost = _pathFindingTurnCost;

            // Copy options to avoid mutation
            int cellGroupMask = options.cellGroupMask;
            bool cellGroupMaskExact = options.cellGroupMaskExactComparison;
            CanCrossCheckType canCrossCheckType = options.canCrossCheckType;
            bool ignoreCellCosts = options.ignoreCellCosts;
            bool includeInvisible = options.includeInvisibleCells;
            int minClearance = options.minClearance;
            float maxCellCrossCost = options.maxCellCrossCost;

            // Run pathfinding on background thread
            Task.Run(() => {
                FindPathAsyncResult result = new FindPathAsyncResult();
                try {
                    // Create thread-local pathfinder
                    IPathFinder threadFinder;
                    if (topology == GridTopology.Irregular) {
                        threadFinder = new PathFinderFastIrregular(cellsSnapshot);
                    } else if ((columnCount & (columnCount - 1)) == 0) {
                        threadFinder = new PathFinderFast(cellsSnapshot, columnCount, rowCount);
                    } else {
                        threadFinder = new PathFinderFastNonSQR(cellsSnapshot, columnCount, rowCount);
                    }

                    // Configure pathfinder
                    threadFinder.Formula = formula;
                    threadFinder.MaxSteps = maxSteps;
                    threadFinder.Diagonals = useDiagonals;
                    threadFinder.HeavyDiagonalsCost = heavyDiagonalsCost;
                    threadFinder.TurnCost = turnCost;
                    switch (topology) {
                        case GridTopology.Irregular: threadFinder.CellShape = CellType.Irregular; break;
                        case GridTopology.Hexagonal: threadFinder.CellShape = pointyTopHex ? CellType.PointyTopHexagon : CellType.FlatTopHexagon; break;
                        default: threadFinder.CellShape = CellType.Box; break;
                    }
                    threadFinder.MaxSearchCost = maxSearchCost;
                    threadFinder.CellGroupMask = cellGroupMask;
                    threadFinder.CellGroupMaskExactComparison = cellGroupMaskExact;
                    threadFinder.IgnoreCanCrossCheck = canCrossCheckType == CanCrossCheckType.IgnoreCanCrossCheckOnAllCells || 
                                                       canCrossCheckType == CanCrossCheckType.IgnoreCanCrossCheckOnAllCellsExceptStartAndEndCells;
                    threadFinder.IgnoreCellCost = ignoreCellCosts;
                    threadFinder.IncludeInvisibleCells = includeInvisible;
                    threadFinder.MinClearance = minClearance;
                    threadFinder.MaxCellCrossCost = maxCellCrossCost;
                    threadFinder.StartCellIndex = cellIndexStart;
                    threadFinder.EndCellIndex = cellIndexEnd;
                    threadFinder.CanCrossCheckType = canCrossCheckType;
                    threadFinder.ClearanceData = clearanceSnapshot;
                    threadFinder.OnCellCross = null; // Callbacks not supported in async mode
                    threadFinder.Data = null;

                    // Execute pathfinding
                    List<PathFinderNode> route = threadFinder.FindPath(null, startCell, endCell, out float totalCost, evenLayout);

                    if (route != null) {
                        result.path = new List<int>();
                        int routeCount = route.Count;
                        if (topology == GridTopology.Irregular) {
                            for (int r = routeCount - 2; r >= 0; r--) {
                                result.path.Add(route[r].PX);
                            }
                        } else {
                            for (int r = routeCount - 2; r >= 0; r--) {
                                int cellIndex = route[r].PY * columnCount + route[r].PX;
                                result.path.Add(cellIndex);
                            }
                        }
                        result.path.Add(cellIndexEnd);
                        result.totalCost = totalCost;
                        result.success = true;
                    } else {
                        result.path = null;
                        result.totalCost = 0;
                        result.success = false;
                    }
                } catch (Exception) {
                    result.path = null;
                    result.totalCost = 0;
                    result.success = false;
                }

                // Invoke callback on main thread
                if (mainThreadContext != null) {
                    mainThreadContext.Post(_ => onComplete(result), null);
                } else {
                    onComplete(result);
                }
            });
        }

        /// <summary>
        /// Finds a path asynchronously and returns a Task. Thread-safe.
        /// IMPORTANT: Do not modify grid structure while async pathfinding is in progress.
        /// </summary>
        public Task<FindPathAsyncResult> FindPathAsync(int cellIndexStart, int cellIndexEnd, FindPathOptions options = null) {
            var tcs = new TaskCompletionSource<FindPathAsyncResult>();
            FindPathAsync(cellIndexStart, cellIndexEnd, result => tcs.SetResult(result), options);
            return tcs.Task;
        }

        #endregion

    }
}

