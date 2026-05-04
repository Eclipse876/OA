using UnityEngine;

namespace OA.Simulation.Navigation
{
    public sealed class HexMapRuntime
    {
        private readonly bool[] blocked;
        private readonly float[] moveCost;
        private readonly Vector2[] worldCenters;

        private bool hasWorldCenters;

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }

        public bool[] Blocked => blocked;
        public float[] MoveCost => moveCost;

        public HexMapRuntime(int width, int height, float cellSize)
        {
            Width = Mathf.Max(4, width);
            Height = Mathf.Max(4, height);
            CellSize = Mathf.Max(0.05f, cellSize);

            int count = Width * Height;
            blocked = new bool[count];
            moveCost = new float[count];
            worldCenters = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                moveCost[i] = 1f;
            }
        }

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

            return map;
        }

        public int GetIndex(int x, int y)
        {
            return y * Width + x;
        }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public bool IsBlocked(int x, int y)
        {
            return !InBounds(x, y) || blocked[GetIndex(x, y)];
        }

        public bool IsWalkable(int x, int y)
        {
            return InBounds(x, y) && !blocked[GetIndex(x, y)];
        }

        public float GetMoveCost(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return float.PositiveInfinity;
            }

            return Mathf.Max(1f, moveCost[GetIndex(x, y)]);
        }

        public void SetBlocked(int x, int y, bool value)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            blocked[GetIndex(x, y)] = value;
        }

        public void SetMoveCost(int x, int y, float value)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            moveCost[GetIndex(x, y)] = Mathf.Max(1f, value);
        }

        public void SetWorldCenter(int x, int y, Vector2 worldCenter)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            worldCenters[GetIndex(x, y)] = worldCenter;
        }

        public void MarkWorldCentersReady()
        {
            hasWorldCenters = true;
        }

        public Vector2 GetWorldCenter(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return Vector2.zero;
            }

            return worldCenters[GetIndex(x, y)];
        }

        public bool TryWorldToCell(Vector2 worldPosition, out Vector2Int cell)
        {
            if (!hasWorldCenters || worldCenters.Length == 0)
            {
                cell = default;
                return false;
            }

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

        public int GetNeighborIndex(int x, int y, Vector2Int[] results)
        {
            return GetNeighborCount(x, y, results);
        }

        public static int HexDistance(Vector2Int a, Vector2Int b)
        {
            ToCube(a, out int ax, out int ay, out int az);
            ToCube(b, out int bx, out int by, out int bz);
            return (Mathf.Abs(ax - bx) + Mathf.Abs(ay - by) + Mathf.Abs(az - bz)) / 2;
        }

        public static void ToCube(Vector2Int offset, out int cubeX, out int cubeY, out int cubeZ)
        {
            cubeX = offset.x - (offset.y - (offset.y & 1)) / 2;
            cubeZ = offset.y;
            cubeY = -cubeX - cubeZ;
        }

        public static Vector2Int CubeToOffset(float cubeX, float cubeZ)
        {
            int row = Mathf.RoundToInt(cubeZ);
            int col = Mathf.RoundToInt(cubeX + (row - (row & 1)) / 2f);
            return new Vector2Int(col, row);
        }

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
