using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using TGS.Geom;
using System.Runtime.CompilerServices;

namespace TGS {

    public enum CELL_SIDE {
        TopLeft = 0,
        Top = 1,
        TopRight = 2,
        BottomRight = 3,
        Bottom = 4,
        BottomLeft = 5,
        Left = 6,
        Right = 7
    }

    public enum CELL_DIRECTION {
        Exiting = 0,
        Entering = 1,
        Any = 2
    }

    public partial class Cell : AdminEntity {


        /// <summary>
        /// The index of the cell in the cells array
        /// </summary>
        public int index;

        /// <summary>
        /// Physical surface-related data
        /// </summary>
        public Region region;

        /// <summary>
        /// Cells adjacent to this cell
        /// </summary>
        public readonly List<Cell> neighbours = new List<Cell>();

        /// <summary>
        /// The territory to which this cell belongs to. You can change it using CellSetTerritory method.
        /// WARNING: do not change this value directly, use CellSetTerritory instead.
        /// </summary>
        public short territoryIndex = -1;

        /// <summary>
        /// Used for performance optimizations.
        /// </summary>
        public int usedFlag, usedFlag2;

        /// <summary>
        /// Controls visibility of the cell. Bit 1 = visibleSelf, Bit 2 = visibleByRules, Bit 3 = visibleAlways.
        /// </summary>
        byte visibleFlags = 3;

        /// <summary>
        /// Gets or sets whether the cell is visible. If true, the cell will be visible if visibleByRules is also visible. Use "visible" to determine the actual visibility state.
        /// </summary>

        public bool visibleSelf {
            get { return (visibleFlags & 1) != 0; } // Check if bit 1 is set
            set {
                if (value) {
                    visibleFlags |= 1; // Set bit 1 (visibleSelf)
                }
                else {
                    visibleFlags &= 254; // Unset bit 1 (visibleSelf) - 254 is 11111110 in binary
                }
            }
        }

        /// <summary>
        /// Returns the actual visibility state of the cell. The setter will set only visibleSelf and not visibleByRules or visibleAlways.
        /// </summary>
        public override bool visible {
            get { return ((visibleFlags & 3) == 3) || (visibleFlags & 4) != 0; } // visibleSelf (1) AND visibleByRules (2) OR visibleAlways (4)
            set {
                if (value) {
                    visibleFlags |= 1; // Set bit 1 (visibleSelf)
                }
                else {
                    visibleFlags &= 254; // Unset bit 1 (visibleSelf) - 254 is 11111110 in binary
                }
            }
        }

        /// <summary>
        /// Gets or sets the visibility of the cell based on rules.
        /// </summary>
        public bool visibleByRules {
            get { return (visibleFlags & 2) != 0; } // Check if bit 2 is set
            set {
                if (value) {
                    visibleFlags |= 2; // Set bit 2
                }
                else {
                    visibleFlags &= 253; // Unset bit 2 (253 is 11111101 in binary)
                }
            }
        }

        /// <summary>
        /// Gets or sets whether the cell is always visible, regardless of other rules.
        /// </summary>
        public bool visibleAlways {
            get { return (visibleFlags & 4) != 0; } // Check if bit 3 is set
            set {
                if (value) {
                    visibleFlags |= 4; // Set bit 3
                }
                else {
                    visibleFlags &= 251; // Unset bit 3 (251 is 11111011 in binary)
                }
            }
        }

        /// <summary>
        /// Distance to nearest blocking cell
        /// </summary>
        public byte clearance = 1;

        /// <summary>
        /// Optional value that can be set with CellSetTag. You can later get the cell quickly using CellGetWithTag method.
        /// </summary>
        public int tag;

        public ushort row, column;

        public string coordinates { get { return string.Format("row = {0}, column = {1}", row, column); } }

        /// <summary>
        /// If this cell blocks path finding.
        /// </summary>
        public bool canCross = true;

        float[] _crossCost;
        /// <summary>
        /// Used by pathfinding in Cell mode. Cost for crossing a cell for each side. Defaults to 1.
        /// </summary>
        /// <value>The cross cost.</value>
        public float[] crossCost {
            get { return _crossCost; }
            set { _crossCost = value; }
        }

        bool[] _blocksLOS;
        /// <summary>
        /// Used by specify if LOS is blocked across cell sides.
        /// </summary>
        /// <value>The cross cost.</value>
        public bool[] blocksLOS {
            get { return _blocksLOS; }
            set { _blocksLOS = value; }
        }


        /// <summary>
        /// Group for this cell. A different group can be assigned to use along with FindPath cellGroupMask argument.
        /// </summary>
        public int group = 1;

        /// <summary>
        /// Used internally to optimize certain algorithms
        /// </summary>
        [NonSerialized]
        public int iteration;


        /// <summary>
        /// Editor-only flag indicating this cell has been customized in the Grid Editor.
        /// </summary>
        bool _customized;
        public bool customized {
            get { return _customized; }
            set { _customized = value; }
        }


        /// <summary>
        /// Returns the centroid of the territory. The centroid always lays inside the polygon whereas the center is the geometric center of the enclosing rectangle and could fall outside an irregular polygon.
        /// </summary>
        /// <returns></returns>
        public Vector2 GetCentroid () {
            return region.centroid;
        }

        /// <summary>
        /// Returns the centroid of the territory using a more accurate algorithm. The centroid always lays inside the polygon whereas the center is the geometric center of the enclosing rectangle and could fall outside an irregular polygon.
        /// </summary>
        /// <returns></returns>
        public Vector2 GetBetterCentroid () {
            return region.betterCentroid;
        }

        public Cell (Vector2 center) {
            this.center = center;
            visible = true;
            borderVisible = true;
        }

        public Cell () : this(Vector2.zero) {
        }

        /// <summary>
        /// Returns the highest crossing cost of all side of a cell
        /// </summary>
        public float GetSidesCost () {
            if (_crossCost == null) return 0;
            int crossCostLength = _crossCost.Length;
            float maxCost = 0;
            for (int k = 0; k < crossCostLength; k++) { if (_crossCost[k] > maxCost) maxCost = _crossCost[k]; }
            return maxCost;
        }

        /// <summary>
        /// Gets the side cross cost.
        /// </summary>
        /// <returns>The side cross cost.</returns>
        /// <param name="side">Side.</param>
        public float GetSideCrossCost (CELL_SIDE side) {
            if (_crossCost == null) return 0;
            return _crossCost[(int)side];
        }

        /// <summary>
        /// Assigns a crossing cost for a given hexagonal side
        /// </summary>
        /// <param name="side">Side.</param>
        /// <param name="cost">Cost.</param>
        public void SetSideCrossCost (CELL_SIDE side, float cost) {
            if (_crossCost == null) _crossCost = new float[8];
            _crossCost[(int)side] = cost;
        }

        /// <summary>
        /// Sets the same crossing cost for all sides of the hexagon.
        /// </summary>
        public void SetAllSidesCost (float cost) {
            if (_crossCost == null) _crossCost = new float[8];
            int crossCostLength = _crossCost.Length;
            for (int k = 0; k < crossCostLength; k++) { _crossCost[k] = cost; }
        }

        /// <summary>
        /// Returns true if side is blocking LOS (from outside to inside cell)
        /// </summary>
        public bool GetSideBlocksLOS (CELL_SIDE side) {
            if (_blocksLOS == null) return false;
            return _blocksLOS[(int)side];
        }

        /// <summary>
        /// Specifies if LOS is blocked through this side (from outside to inside cell)
        /// </summary>
        /// <param name="side">Side.</param>
        public void SetSideBlocksLOS (CELL_SIDE side, bool blocks) {
            if (_blocksLOS == null) _blocksLOS = new bool[8];
            _blocksLOS[(int)side] = blocks;
        }

    }
}

