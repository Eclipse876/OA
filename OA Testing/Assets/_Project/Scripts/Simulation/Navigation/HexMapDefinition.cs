using System;
using UnityEngine;

namespace OA.Simulation.Navigation
{
    [CreateAssetMenu(
        fileName = "HexMapDefinition",
        menuName = "OA/Navigation/Hex Map Definition")]
    public sealed class HexMapDefinition : ScriptableObject
    {
        [Header("Hex Map Grid")]
        [SerializeField, Min(4)] private int width = 80;
        [SerializeField, Min(4)] private int height = 120;
        [SerializeField, Min(0f)] private float cellSize = 1.1f;

        [Header("Bake Metadata")]
        [SerializeField, Min(1)] private int seed = 1;
        [SerializeField, Range(0.05f, 0.45f)] private float obstacleChance = 0.2f;
        [SerializeField, Range(0f, 0.85f)] private float roughWaterChance = 0f;
        [SerializeField, Range(0, 8)] private int smoothingPasses = 3;

        [Header("Cell Data")]
        [SerializeField] private bool[] blocked;
        [SerializeField] private float[] moveCost;

        public int Width => width;
        public int Height => height;
        public float CellSize => cellSize;

        public int Seed => seed;
        public float ObstacleChance => obstacleChance;
        public float RoughWaterChance => roughWaterChance;
        public int SmoothingPasses => smoothingPasses;

        public bool[] CellBlocked => blocked;
        public float[] MoveCost => moveCost;

        private void OnValidate()
        {
            width = Mathf.Max(4, width);
            height = Mathf.Max(4, height);
            cellSize = Mathf.Max(0.05f, cellSize);

            seed = Mathf.Max(1, seed);
            obstacleChance = Mathf.Clamp(obstacleChance, 0.05f, 0.45f);
            roughWaterChance = Mathf.Clamp(roughWaterChance, 0f, 0.85f);
            smoothingPasses = Mathf.Clamp(smoothingPasses, 0, 8);

            EnsureCellArrays();
        }

        public void EnsureCellArrays()
        {
            int count = width * height;
            blocked = ResizeBoolArray(blocked, count, false);
            moveCost = ResizeFloatArray(moveCost, count, 1f);

            for (int i = 0; i < moveCost.Length; i++)
            {
                moveCost[i] = Mathf.Max(1f, moveCost[i]);
            }
        }

        public void ApplyFromRuntime(
            HexMapRuntime runtime,
            int bakedSeed,
            float bakedObstacleChance,
            float bakedRoughWaterChance,
            int bakedSmoothingPasses)
        {
            if (runtime == null)
            {
                return;
            }

            width = runtime.Width;
            height = runtime.Height;
            cellSize = runtime.CellSize;

            seed = Mathf.Max(1, bakedSeed);
            obstacleChance = Mathf.Clamp(bakedObstacleChance, 0.05f, 0.45f);
            roughWaterChance = Mathf.Clamp(bakedRoughWaterChance, 0f, 0.85f);
            smoothingPasses = Mathf.Clamp(bakedSmoothingPasses, 0, 8);

            EnsureCellArrays();

            Array.Copy(runtime.Blocked, blocked, Mathf.Min(runtime.Blocked.Length, blocked.Length));
            Array.Copy(runtime.MoveCost, moveCost, Mathf.Min(runtime.MoveCost.Length, moveCost.Length));
        }

        public HexMapRuntime CreateRuntimeCopy()
        {
            EnsureCellArrays();
            return HexMapRuntime.FromDefinition(this);
        }

        private static bool[] ResizeBoolArray(bool[] source, int count, bool defaultValue)
        {
            bool[] result = new bool[count];

            if (source != null)
            {
                Array.Copy(source, result, Mathf.Min(source.Length, count));
            }

            if (source == null || source.Length < count)
            {
                int start = source == null ? 0 : source.Length;
                for (int i = start; i < count; i++)
                {
                    result[i] = defaultValue;
                }
            }

            return result;
        }

        private static float[] ResizeFloatArray(float[] source, int count, float defaultValue)
        {
            float[] result = new float[count];

            if (source != null)
            {
                Array.Copy(source, result, Mathf.Min(source.Length, count));
            }

            if (source == null || source.Length < count)
            {
                int start = source == null ? 0 : source.Length;
                for (int i = start; i < count; i++)
                {
                    result[i] = defaultValue;
                }
            }

            return result;
        }
    }
}
