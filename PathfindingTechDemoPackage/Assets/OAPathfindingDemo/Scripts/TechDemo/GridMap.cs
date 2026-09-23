using UnityEngine;

namespace OA.TechDemo
{
    public sealed class GridMap
    {
        private readonly bool[] _blocked;
        private readonly float[] _moveCost;

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }

        public GridMap(int width, int height, float cellSize)
        {
            Width = Mathf.Max(4, width);
            Height = Mathf.Max(4, height);
            CellSize = Mathf.Max(0.1f, cellSize);

            int count = Width * Height;
            _blocked = new bool[count];
            _moveCost = new float[count];

            for (int i = 0; i < count; i++)
            {
                _moveCost[i] = 1f;
            }
        }

        public int GetIndex(int x, int y)
        {
            return y * Width + x;
        }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        public bool IsBlocked(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return true;
            }

            return _blocked[GetIndex(x, y)];
        }

        public bool IsWalkable(int x, int y)
        {
            return InBounds(x, y) && !_blocked[GetIndex(x, y)];
        }

        public float GetMoveCost(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return float.PositiveInfinity;
            }

            return Mathf.Max(1f, _moveCost[GetIndex(x, y)]);
        }

        public void SetBlocked(int x, int y, bool blocked)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            _blocked[GetIndex(x, y)] = blocked;
        }

        public void SetMoveCost(int x, int y, float moveCost)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            _moveCost[GetIndex(x, y)] = Mathf.Max(1f, moveCost);
        }

        public Vector3 GridToWorld(int x, int y, float worldY = 0f)
        {
            float worldX = (x - (Width - 1) * 0.5f) * CellSize;
            float worldZ = (y - (Height - 1) * 0.5f) * CellSize;
            return new Vector3(worldX, worldY, worldZ);
        }

        public bool WorldToGrid(Vector3 worldPosition, out Vector2Int cell)
        {
            float gridX = (worldPosition.x / CellSize) + (Width - 1) * 0.5f;
            float gridY = (worldPosition.z / CellSize) + (Height - 1) * 0.5f;

            int x = Mathf.RoundToInt(gridX);
            int y = Mathf.RoundToInt(gridY);

            if (!InBounds(x, y))
            {
                cell = default;
                return false;
            }

            cell = new Vector2Int(x, y);
            return true;
        }
    }
}
