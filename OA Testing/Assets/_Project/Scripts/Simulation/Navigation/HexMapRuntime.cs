// HexMapRuntime.cs:
// This is the live hex map brain. It knows which cells are blocked, what they
// cost to cross, where their centers landed in world space, and how to speak
// hex math so the rest of the code does not have to.
using UnityEngine;

namespace OA.Simulation.Navigation
{
    // Runtime-only map state used by generation, pathfinding, and presenters.
    // Not an asset, just the working copy everybody pokes at.
    public sealed class HexMapRuntime
    {
        // Cell data is stored flat: index = y * Width + x. Simple, fast, slightly boring.
        private readonly bool[] blocked;
        private readonly float[] moveCost;
        private readonly WaterDepthClass[] depthClass;
        private readonly Vector2[] worldCenters;

        private bool hasWorldCenters;

        // Read-only map shape. Resize by making a new runtime map.
        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public bool HasWorldCenters => hasWorldCenters;

        public bool[] Blocked => blocked;
        public float[] MoveCost => moveCost;
        public WaterDepthClass[] DepthClass => depthClass;

        // Creates a clamped, empty map and defaults every cell to normal movement cost.
        public HexMapRuntime(int width, int height, float cellSize)
        {
            Width = Mathf.Max(4, width);
            Height = Mathf.Max(4, height);
            CellSize = Mathf.Max(0.05f, cellSize);

            int count = Width * Height;
            blocked = new bool[count];
            moveCost = new float[count];
            depthClass = new WaterDepthClass[count];
            worldCenters = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                moveCost[i] = 1f;
                depthClass[i] = WaterDepthClass.Deep;
            }
        }

        // Builds a runtime copy from a saved HexMapDefinition asset.
        public static HexMapRuntime FromDefinition(HexMapDefinition definition)
        {
            if (definition == null)
            {
                Debug.LogError("HexMapRuntime.FromDefinition: definition is null");
                return null;
            }

            HexMapRuntime map = new HexMapRuntime(definition.Width, definition.Height, definition.CellSize);
            bool[] defBlocked = definition.CellBlocked;
            float[] srcMoveCost = definition.MoveCost;
            WaterDepthClass[] srcDepthClass = definition.DepthClass;
            int count = map.Width * map.Height;

            if (defBlocked != null)
            {
                System.Array.Copy(defBlocked, map.blocked, Mathf.Min(defBlocked.Length, count));
            }

            if (srcMoveCost != null)
            {
                int copy = Mathf.Min(srcMoveCost.Length, count);
                for (int i = 0; i < copy; i++)
                {
                    map.moveCost[i] = Mathf.Max(1f, srcMoveCost[i]);
                }
            }

            if (srcDepthClass != null)
            {
                int copy = Mathf.Min(srcDepthClass.Length, count);
               
                for(int i = 0; i < copy; i++)
                {
                    map.depthClass[i] = srcDepthClass[i];
                }
            }

            return map;
        }

        // Converts x/y into the flat array index used everywhere else.
        public int GetIndex(int x, int y)
        {
            return y * Width + x;
        }

        // Checks whether a cell lives inside the map rectangle.
        public bool InBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        // Treats out-of-bounds as blocked so callers fail closed.
        public bool IsBlocked(int x, int y)
        {
            return !InBounds(x, y) || blocked[GetIndex(x, y)];
        }

        // True only when the cell exists and is not blocked.
        public bool IsWalkable(int x, int y)
        {
            return InBounds(x, y) && !blocked[GetIndex(x, y)];
        }

        // Returns movement cost, or infinity if someone asks for nonsense coordinates.
        public float GetMoveCost(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return float.PositiveInfinity;
            }

            return Mathf.Max(1f, moveCost[GetIndex(x, y)]);
        }

        // Safely updates blocked state and ignores out-of-bounds writes.
        public void SetBlocked(int x, int y, bool value)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            blocked[GetIndex(x, y)] = value;
        }

        // Safely updates movement cost while clamping it to at least normal water.
        public void SetMoveCost(int x, int y, float value)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            moveCost[GetIndex(x, y)] = Mathf.Max(1f, value);
        }

        //Out-of-bounts returns Shallow so Deep draft ships fail closed.
        public WaterDepthClass GetDepthClass(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return WaterDepthClass.Shallow;
            }
            
            return depthClass[GetIndex(x, y)];
        }

        public void SetDepthClass(int x, int y, WaterDepthClass value)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            depthClass[GetIndex(x, y)] = value;
        }

        // Stores the world-space center for a rendered cell.
        public void SetWorldCenter(int x, int y, Vector2 worldCenter)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            worldCenters[GetIndex(x, y)] = worldCenter;
        }

        // Marks world centers usable after the presenter finishes filling them in.
        public void MarkWorldCentersReady()
        {
            hasWorldCenters = true;
        }

        // Returns the cached world center for a cell, or zero if the cell is invalid.
        public Vector2 GetWorldCenter(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return Vector2.zero;
            }

            return worldCenters[GetIndex(x, y)];
        }

        // Finds the nearest cached cell center to a world position for clicks and routing.
        public bool TryWorldToCell(Vector2 worldPosition, out Vector2Int cell)
        {
            if (!hasWorldCenters || worldCenters.Length == 0)
            {
                cell = default;
                return false;
            }

            // Brute-force nearest-center lookup is fine for debug maps; swap later if maps get huge.
            float bestDistanceSqr = float.PositiveInfinity;
            int bestIndex = -1;

            for (int i = 0; i < worldCenters.Length; i++)
            {
                Vector2 center = worldCenters[i];
                float dx = center.x - worldPosition.x;
                float dy = center.y - worldPosition.y;
                float distanceSqr = dx * dx + dy * dy;

                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                cell = default;
                return false;
            }

            cell = new Vector2Int(bestIndex % Width, bestIndex / Width);
            return true;
        }

        // Fills a caller-owned buffer with valid neighboring hex cells.
        public int GetNeighborCount(int x, int y, Vector2Int[] results)
        {
            if (!InBounds(x, y) || results == null || results.Length == 0)
            {
                return 0;
            }

            int count = 0;
            Vector2Int[] offsets = (y & 1) == 0 ? EvenRowNeighborOffsets : OddRowNeighborOffsets;

            for (int i = 0; i < offsets.Length && count < results.Length; i++)
            {
                int nx = x + offsets[i].x;
                int ny = y + offsets[i].y;
                if (!InBounds(nx, ny))
                {
                    continue;
                }

                results[count++] = new Vector2Int(nx, ny);
            }

            return count;
        }

        // Old/alternate name for neighbor lookup, kept as a tiny wrapper.
        public int GetNeighborIndex(int x, int y, Vector2Int[] results)
        {
            return GetNeighborCount(x, y, results);
        }

        // Measures true hex distance by converting offset cells to cube coordinates.
        public static int HexDistance(Vector2Int a, Vector2Int b)
        {
            ToCube(a, out int ax, out int ay, out int az);
            ToCube(b, out int bx, out int by, out int bz);
            return (Mathf.Abs(ax - bx) + Mathf.Abs(ay - by) + Mathf.Abs(az - bz)) / 2;
        }

        // Converts odd-row offset coordinates into cube coordinates for cleaner hex math.
        public static void ToCube(Vector2Int offset, out int cubeX, out int cubeY, out int cubeZ)
        {
            cubeX = offset.x - (offset.y - (offset.y & 1)) / 2;
            cubeZ = offset.y;
            cubeY = -cubeX - cubeZ;
        }

        // Converts cube-ish coordinates back into odd-row offset cells.
        public static Vector2Int CubeToOffset(float cubeX, float cubeZ)
        {
            int row = Mathf.RoundToInt(cubeZ);
            int col = Mathf.RoundToInt(cubeX + (row - (row & 1)) / 2f);
            return new Vector2Int(col, row);
        }

        // Odd-r offset neighbor tables. Rows alternate which diagonals they own.
        private static readonly Vector2Int[] EvenRowNeighborOffsets =
        {
            new Vector2Int(-1, 0),
            new Vector2Int( 1, 0),
            new Vector2Int(-1,-1),
            new Vector2Int( 0,-1),
            new Vector2Int(-1, 1),
            new Vector2Int( 0, 1)
        };

        private static readonly Vector2Int[] OddRowNeighborOffsets =
        {
            new Vector2Int(-1, 0),
            new Vector2Int( 1, 0),
            new Vector2Int( 0,-1),
            new Vector2Int( 1,-1),
            new Vector2Int( 0, 1),
            new Vector2Int( 1, 1),
        };
    }
}
