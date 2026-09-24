using UnityEngine;
using System.Collections.Generic;
using TGS;
using TGS.PathFinding;

namespace TGSDemos {

    // Simplified box-grid path finding demo focused on the per-turn cost.
    // Click a start cell, click an end cell, then drag the Turn Cost slider to see
    // equal-cost zig-zag paths straighten into fewer-turn routes.
    public class Demo35 : MonoBehaviour {

        int numObstacles = 250;
        readonly List<int> obstacles = new List<int>();

        TerrainGridSystem tgs;
        GUIStyle labelStyle;
        bool isSelectingStart = true;
        int cellStartIndex = -1;
        int cellEndIndex = -1;
        List<int> path;

        readonly Color startColor = Color.yellow;
        readonly Color endColor = Color.green;
        readonly Color pathColor = new Color(0f, 0.6f, 1f, 0.7f);
        readonly Color obstacleColor = new Color(0.35f, 0.35f, 0.35f);

        readonly HeuristicFormula[] heuristics = {
            HeuristicFormula.Manhattan, HeuristicFormula.MaxDXDY, HeuristicFormula.DiagonalShortCut,
            HeuristicFormula.Euclidean, HeuristicFormula.EuclideanSQR, HeuristicFormula.Custom1
        };
        readonly string[] heuristicNames = { "Manhattan", "MaxDXDY", "Diagonal", "Euclidean", "EuclideanSQR", "Custom1" };

        void Start() {
            tgs = TerrainGridSystem.instance;

            // GUI resizer - only for the demo
            GUIResizer.Init(800, 500);
            labelStyle = new GUIStyle();
            labelStyle.alignment = TextAnchor.MiddleLeft;
            labelStyle.normal.textColor = Color.white;

            GenerateObstacles();

            tgs.OnCellClick += (grid, cellIndex, buttonIndex) => OnCellClicked(cellIndex);
        }

        void GenerateObstacles() {
            // clear previous obstacles
            ClearPathColor();
            path = null;
            for (int k = 0; k < obstacles.Count; k++) {
                tgs.CellSetCanCross(obstacles[k], true);
                tgs.CellToggleRegionSurface(obstacles[k], false, Color.white);
            }
            obstacles.Clear();

            // scatter obstacles so paths have to bend
            Random.InitState(2);
            for (int i = 0; i < numObstacles; i++) {
                int row = Random.Range(1, tgs.rowCount - 1);
                int col = Random.Range(1, tgs.columnCount - 1);
                int cellIndex = tgs.CellGetIndex(row, col);
                if (cellIndex == cellStartIndex || cellIndex == cellEndIndex || !tgs.cells[cellIndex].canCross) continue;
                tgs.CellSetCanCross(cellIndex, false);
                tgs.CellSetColor(cellIndex, obstacleColor);
                obstacles.Add(cellIndex);
            }

            if (cellStartIndex >= 0 && cellEndIndex >= 0) ComputePath();
        }

        void OnCellClicked(int cellIndex) {
            if (!tgs.cells[cellIndex].canCross) return;

            if (isSelectingStart) {
                ClearPath();
                if (cellStartIndex >= 0) tgs.CellToggleRegionSurface(cellStartIndex, false, Color.white);
                cellStartIndex = cellIndex;
                cellEndIndex = -1;
                tgs.CellToggleRegionSurface(cellStartIndex, true, startColor);
            } else {
                cellEndIndex = cellIndex;
                ComputePath();
            }
            isSelectingStart = !isSelectingStart;
        }

        void ComputePath() {
            ClearPathColor();
            if (cellStartIndex < 0 || cellEndIndex < 0) return;

            path = tgs.FindPath(cellStartIndex, cellEndIndex);
            if (path != null) {
                for (int k = 0; k < path.Count; k++) {
                    tgs.CellToggleRegionSurface(path[k], true, pathColor);
                }
            }
            tgs.CellToggleRegionSurface(cellStartIndex, true, startColor);
            tgs.CellToggleRegionSurface(cellEndIndex, true, endColor);
        }

        void ClearPathColor() {
            if (path == null) return;
            for (int k = 0; k < path.Count; k++) {
                tgs.CellToggleRegionSurface(path[k], false, Color.white);
            }
        }

        void ClearPath() {
            ClearPathColor();
            path = null;
            cellEndIndex = -1;
        }

        void OnGUI() {
            GUIResizer.AutoResize();

            GUI.Label(new Rect(10, 10, 420, 24), isSelectingStart ? "Click a cell to set the START" : "Click a cell to set the END", labelStyle);

            GUI.Label(new Rect(10, 40, 420, 24), "Turn Cost: " + tgs.pathFindingTurnCost.ToString("0.00"), labelStyle);
            float newTurnCost = GUI.HorizontalSlider(new Rect(110, 46, 200, 24), tgs.pathFindingTurnCost, 0f, 5f);
            if (!Mathf.Approximately(newTurnCost, tgs.pathFindingTurnCost)) {
                tgs.pathFindingTurnCost = newTurnCost;
                ComputePath();
            }

            bool useDiagonals = GUI.Toggle(new Rect(10, 70, 200, 24), tgs.pathFindingUseDiagonals, " Use Diagonals");
            if (useDiagonals != tgs.pathFindingUseDiagonals) {
                tgs.pathFindingUseDiagonals = useDiagonals;
                ComputePath();
            }

            GUI.Label(new Rect(10, 100, 200, 24), "Obstacles: " + numObstacles, labelStyle);
            int newObstacles = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(130, 106, 200, 24), numObstacles, 0, 500));
            if (newObstacles != numObstacles) {
                numObstacles = newObstacles;
                GenerateObstacles();
            }

            GUI.Label(new Rect(10, 130, 460, 24), "Raise Turn Cost to straighten zig-zag paths of equal length", labelStyle);

            // Heuristic selector
            GUI.Label(new Rect(10, 160, 460, 24), "Heuristic:", labelStyle);
            int currentHeuristic = System.Array.IndexOf(heuristics, tgs.pathFindingHeuristicFormula);
            if (currentHeuristic < 0) currentHeuristic = 0;
            int newHeuristic = GUI.SelectionGrid(new Rect(90, 160, 160, 144), currentHeuristic, heuristicNames, 1);
            if (newHeuristic != currentHeuristic) {
                tgs.pathFindingHeuristicFormula = heuristics[newHeuristic];
                ComputePath();
            }
        }
    }
}
