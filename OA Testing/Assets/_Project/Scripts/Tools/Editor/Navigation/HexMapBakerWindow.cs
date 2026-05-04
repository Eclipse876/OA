#if UNITY_EDITOR
using OA.Simulation.Navigation;
using UnityEditor;
using UnityEngine;

namespace OA.Tools.Editor.Navigation
{
    public sealed class HexMapBakerWindow : EditorWindow
    {
        private HexMapDefinition mapDefinition;

        private int seed = 1;
        private float obstacleChance = 0.2f;
        private float roughWaterChance = 0f;
        private int smoothingPasses = 3;

        private Vector2Int guaranteedStart = new Vector2Int(2, 2);
        private Vector2Int guaranteedGoal = new Vector2Int(31, 21);

        private readonly HexMapGenerator generator = new HexMapGenerator();

        [MenuItem("OA/Navigation/Hex Map Baker")]
        public static void Open()
        {
            GetWindow<HexMapBakerWindow>("Hex Map Baker");
        }

        private void OnGUI()
        {
            GUILayout.Label("Hex Map Baker", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            mapDefinition = (HexMapDefinition)EditorGUILayout.ObjectField(
                "Map Definition", mapDefinition, typeof(HexMapDefinition), false);

            if (mapDefinition == null)
            {
                EditorGUILayout.HelpBox("Assign a HexMapDefinition asset.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);

            seed = EditorGUILayout.IntField("Seed", Mathf.Max(1, seed));
            obstacleChance = EditorGUILayout.Slider("Obstacle Chance", obstacleChance, 0.05f, 0.45f);
            roughWaterChance = EditorGUILayout.Slider("Rough Water Chance", roughWaterChance, 0f, 0.85f);
            smoothingPasses = EditorGUILayout.IntSlider("Smoothing Passes", smoothingPasses, 0, 8);

            guaranteedStart = EditorGUILayout.Vector2IntField("Guaranteed Start", guaranteedStart);
            guaranteedGoal = EditorGUILayout.Vector2IntField("Guaranteed Goal", guaranteedGoal);

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Pull Values From Asset"))
            {
                PullFromAsset();
            }

            if (GUILayout.Button("Roll Random Seed"))
            {
                seed = Random.Range(1, int.MaxValue);
            }

            if (GUILayout.Button("Bake Into Asset"))
            {
                Bake();
            }
        }

        private void PullFromAsset()
        {
            seed = Mathf.Max(1, mapDefinition.Seed);
            obstacleChance = mapDefinition.ObstacleChance;
            roughWaterChance = mapDefinition.RoughWaterChance;
            smoothingPasses = mapDefinition.SmoothingPasses;

            guaranteedStart = new Vector2Int(2, 2);
            guaranteedGoal = new Vector2Int(mapDefinition.Width - 3, mapDefinition.Height - 3);
        }

        private void Bake()
        {
            if (mapDefinition == null)
            {
                return;
            }

            mapDefinition.EnsureCellArrays();

            HexMapRuntime runtime = new HexMapRuntime(mapDefinition.Width, mapDefinition.Height, mapDefinition.CellSize);

            Vector2Int start = ClampToMap(guaranteedStart, runtime);
            Vector2Int goal = ClampToMap(guaranteedGoal, runtime);

            generator.Generate(
                runtime,
                Mathf.Max(1, seed),
                obstacleChance,
                roughWaterChance,
                smoothingPasses,
                start,
                goal);

            mapDefinition.ApplyFromRuntime(
                runtime,
                Mathf.Max(1, seed),
                obstacleChance,
                roughWaterChance,
                smoothingPasses);

            EditorUtility.SetDirty(mapDefinition);
            AssetDatabase.SaveAssets();

            Debug.Log($"[HexMapBaker] Baked map '{mapDefinition.name}' with Seed={seed}");
        }

        private static Vector2Int ClampToMap(Vector2Int cell, HexMapRuntime map)
        {
            int x = Mathf.Clamp(cell.x, 0, map.Width - 1);
            int y = Mathf.Clamp(cell.y, 0, map.Height - 1);
            return new Vector2Int(x, y);
        }
    }
}
#endif
