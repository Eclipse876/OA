// HexMapDefinitionEditor.cs:
// Custom inspector for map definition assets. Keeps the normal field layout,
// but adds a quick seed reroll button where designers are already looking.
#if UNITY_EDITOR
using OA.Simulation.Navigation;
using UnityEditor;
using UnityEngine;

namespace OA.Tools.Editor.Navigation
{
    [CustomEditor(typeof(HexMapDefinition))]
    [CanEditMultipleObjects]
    public sealed class HexMapDefinitionEditor : UnityEditor.Editor
    {
        private SerializedProperty width;
        private SerializedProperty height;
        private SerializedProperty cellSize;
        private SerializedProperty seed;
        private SerializedProperty obstacleChance;
        private SerializedProperty depthClass;
        private SerializedProperty landElevationClass;
        private SerializedProperty roughWaterChance;
        private SerializedProperty smoothingPasses;
        private SerializedProperty blocked;
        private SerializedProperty moveCost;

        private void OnEnable()
        {
            width = serializedObject.FindProperty("width");
            height = serializedObject.FindProperty("height");
            cellSize = serializedObject.FindProperty("cellSize");
            seed = serializedObject.FindProperty("seed");
            obstacleChance = serializedObject.FindProperty("obstacleChance");
            depthClass = serializedObject.FindProperty("depthClass");
            landElevationClass = serializedObject.FindProperty("landElevationClass");
            roughWaterChance = serializedObject.FindProperty("roughWaterChance");
            smoothingPasses = serializedObject.FindProperty("smoothingPasses");
            blocked = serializedObject.FindProperty("blocked");
            moveCost = serializedObject.FindProperty("moveCost");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScriptField();

            EditorGUILayout.LabelField("Hex Map Grid", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(width);
            EditorGUILayout.PropertyField(height);
            EditorGUILayout.PropertyField(cellSize);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Bake Metadata", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(seed);

            if (GUILayout.Button("Roll Random Seed"))
            {
                seed.intValue = UnityEngine.Random.Range(1, int.MaxValue);
            }

            EditorGUILayout.PropertyField(obstacleChance);
            EditorGUILayout.PropertyField(depthClass);
            EditorGUILayout.PropertyField(landElevationClass);
            EditorGUILayout.PropertyField(roughWaterChance);
            EditorGUILayout.PropertyField(smoothingPasses);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Cell Data", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(blocked);
            EditorGUILayout.PropertyField(moveCost);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScriptField()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    "Script",
                    MonoScript.FromScriptableObject((HexMapDefinition)target),
                    typeof(MonoScript),
                    false);
            }
        }
    }
}
#endif
