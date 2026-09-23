// HexMapBakerWindow.cs:
// Editor button window for baking a hex map into an asset. Pull values, roll seed,
// bake the thing, ???, profit.
#if UNITY_EDITOR
using OA.Simulation.Navigation;
using UnityEditor;
using UnityEngine;

namespace OA.Tools.Editor.Navigation
{
    // Unity editor window that runs HexMapGenerator and saves the result into HexMapDefinition.
    public sealed class HexMapBakerWindow : EditorWindow
    {
        private HexMapDefinition mapDefinition;

        // Local UI state mirrors the asset until the user decides to bake.
        private int seed = 1;
        private float obstacleChance = 0.2f;
        private float roughWaterChance = 0f;
        private int smoothingPasses = 3;

        // Cells we promise to keep usable while generating a test route across the map.
        private Vector2Int guaranteedStart = new Vector2Int(2, 2);
        private Vector2Int guaranteedGoal = new Vector2Int(31, 21);

        private readonly HexMapGenerator generator = new HexMapGenerator();

        [MenuItem("OA/Navigation/Hex Map Baker")]
        // Opens the custom baker from the Unity menu.
        public static void Open()
        {
            GetWindow<HexMapBakerWindow>("Hex Map Baker");
        }

        // Draws the baker controls and routes button presses into helper methods.
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

        // Copies current asset settings into the window controls.
        private void PullFromAsset()
        {
            seed = Mathf.Max(1, mapDefinition.Seed);
            obstacleChance = mapDefinition.ObstacleChance;
            roughWaterChance = mapDefinition.RoughWaterChance;
            smoothingPasses = mapDefinition.SmoothingPasses;

            guaranteedStart = new Vector2Int(2, 2);
            guaranteedGoal = new Vector2Int(mapDefinition.Width - 3, mapDefinition.Height - 3);
        }

        // Generates a runtime map and writes the baked result back into the asset.
        private void Bake()
        {
            if (mapDefinition == null)
            {
                return;
            }

            // Make sure asset arrays exist before we overwrite them with generated data.
            mapDefinition.EnsureCellArrays();

            HexMapRuntime runtime = new HexMapRuntime(mapDefinition.Width, mapDefinition.Height, mapDefinition.CellSize);

            // Clamp endpoints so bad inspector values do not wander off-map.
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

            // Save generated cells plus the bake knobs back into the asset.
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

        // Keeps a requested cell inside the runtime map bounds.
        private static Vector2Int ClampToMap(Vector2Int cell, HexMapRuntime map)
        {
            int x = Mathf.Clamp(cell.x, 0, map.Width - 1);
            int y = Mathf.Clamp(cell.y, 0, map.Height - 1);
            return new Vector2Int(x, y);
        }
    }
}
#endif
