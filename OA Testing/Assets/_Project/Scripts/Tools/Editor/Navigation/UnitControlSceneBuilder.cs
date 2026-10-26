using System;
using OA.Presentation.Debug;
using OA.Presentation.Units;
using OA.Simulation.Navigation;
using OA.Simulation.Units;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace OA.Tools.Editor
{
    public static class UnitControlSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/UnitControlTesting.unity";
        private const string DataFolder = "Assets/_Project/Data/Scenarios/UnitControl";
        private const string MapPath = DataFolder + "/HexMapDefinition_UnitControl.asset";
        private const float CellSize = 32.22222f;
        private const int Seed = 20261026;

        // Also callable with -executeMethod OA.Tools.Editor.UnitControlSceneBuilder.Build.
        [MenuItem("OA/Scenes/Create Unit Control Testing")]
        public static void Build()
        {
            if (AssetDatabase.LoadMainAssetAtPath(ScenePath) != null ||
                System.IO.File.Exists(ScenePath))
                throw new InvalidOperationException("Refusing to overwrite existing scene: " + ScenePath);
            if (AssetDatabase.LoadMainAssetAtPath(MapPath) != null ||
                System.IO.File.Exists(MapPath))
                throw new InvalidOperationException("Refusing to overwrite existing map: " + MapPath);

            UnitArchetypeDefinition garcia = LoadRequired<UnitArchetypeDefinition>(
                "Assets/_Project/Data/Scenarios/Navigation/ShipArchetype_Main.asset");
            UnitArchetypeDefinition enterprise = LoadRequired<UnitArchetypeDefinition>(
                DataFolder + "/EnterpriseArchetype.asset");
            TileBase deepWater = LoadGuid<TileBase>("18feb46b92ca7854baab2ca54fb4dc3a");
            TileBase roughWater = LoadGuid<TileBase>("f12ce85544d1d6f4183728b1b405e06a");
            TileBase obstacle = LoadGuid<TileBase>("19488b58fe65aa44da270ed068c26566");

            EnsureFolder("Assets/Scenes");
            EnsureFolder(DataFolder);

            var startingCells = new[] { new Vector2Int(8, 12), new Vector2Int(9, 12) };
            var map = new HexMapRuntime(64, 48, CellSize);
            new HexMapGenerator().Generate(map, Seed, 0.016f, 0f, 4,
                startingCells[0], startingCells[1]);
            Scene previousScene = SceneManager.GetActiveScene();
            bool emptyStartup = string.IsNullOrEmpty(previousScene.path) && !previousScene.isDirty;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                emptyStartup ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            try
            {
                var mapDefinition = ScriptableObject.CreateInstance<HexMapDefinition>();
                mapDefinition.name = "HexMapDefinition_UnitControl";
                mapDefinition.ApplyFromRuntime(map, Seed, 0.016f, 0f, 4);
                var mapRoot = new GameObject("Navigation Map", typeof(Grid));
                Grid grid = mapRoot.GetComponent<Grid>();
                grid.cellLayout = GridLayout.CellLayout.Hexagon;
                grid.cellSwizzle = GridLayout.CellSwizzle.YXZ;
                grid.cellSize = new Vector3(0.8659766f, 1f, 1f);

                var tilemapObject = new GameObject("Terrain", typeof(Tilemap), typeof(TilemapRenderer));
                tilemapObject.transform.SetParent(mapRoot.transform, false);
                Tilemap tilemap = tilemapObject.GetComponent<Tilemap>();
                TilemapRenderer tileRenderer = tilemapObject.GetComponent<TilemapRenderer>();
                tileRenderer.sortingLayerName = "Navigation";
                tileRenderer.sortingOrder = 0;

                TilemapHexGridPresenter presenter = mapRoot.AddComponent<TilemapHexGridPresenter>();
                Configure(presenter, serialized =>
                {
                    Property(serialized, "tilemap").objectReferenceValue = tilemap;
                    Property(serialized, "previewDefinition").objectReferenceValue = mapDefinition;
                    Property(serialized, "baseHexCellSize").vector2Value = new Vector2(0.8659766f, 1f);
                    Property(serialized, "baseGridLocalScale").vector3Value = Vector3.one;
                    Property(serialized, "deepWaterTile").objectReferenceValue = deepWater;
                    Property(serialized, "roughWaterTile").objectReferenceValue = roughWater;
                    Property(serialized, "obstacleTile").objectReferenceValue = obstacle;
                    // Bake a lightweight base-map preview using persistent tiles.
                    // Procedural overlay textures are generated only in Play mode.
                    Property(serialized, "useProceduralTextureOverlay").boolValue = false;
                });
                presenter.BuildOrRefresh(map);
                Configure(presenter, serialized =>
                    Property(serialized, "useProceduralTextureOverlay").boolValue = true);

                var navigationObject = new GameObject("Navigation Service");
                HexMapPathService pathService = navigationObject.AddComponent<HexMapPathService>();
                var unitsRoot = new GameObject("Units");
                SelectableShip garciaUnit = SelectableShip.Create(garcia, unitsRoot.transform);
                SelectableShip enterpriseUnit = SelectableShip.Create(enterprise, unitsRoot.transform);
                garciaUnit.gameObject.name = "Garcia";
                enterpriseUnit.gameObject.name = "Enterprise";
                Vector2 garciaPosition = map.GetWorldCenter(startingCells[0].x, startingCells[0].y);
                Vector2 enterprisePosition = map.GetWorldCenter(startingCells[1].x, startingCells[1].y);
                garciaUnit.transform.position = new Vector3(garciaPosition.x, garciaPosition.y, 0f);
                enterpriseUnit.transform.position = new Vector3(enterprisePosition.x, enterprisePosition.y, 0f);

                var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 100f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 1000f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(20f / 255f, 42f / 255f, 58f / 255f, 1f);
                Vector2 focus = (garciaPosition + enterprisePosition) * 0.5f;
                cameraObject.transform.position = new Vector3(focus.x, focus.y, -10f);
                CameraController cameraController = cameraObject.AddComponent<CameraController>();
                Configure(cameraController, serialized =>
                {
                    Property(serialized, "autoFramePositionWithZoom").boolValue = false;
                    Property(serialized, "fitMapFromCurrentCameraPosition").boolValue = false;
                    Property(serialized, "mapBoundsRoot").objectReferenceValue = mapRoot.transform;
                    Property(serialized, "shipFocusRenderer").objectReferenceValue =
                        garciaUnit.GetComponentInChildren<SpriteRenderer>(true);
                    Property(serialized, "maximumOrthographicSize").floatValue = 2000f;
                });

                UnitControlDemo demo = new GameObject("Unit Control Demo").AddComponent<UnitControlDemo>();
                Configure(demo, serialized =>
                {
                    Property(serialized, "mapDefinition").objectReferenceValue = mapDefinition;
                    Property(serialized, "worldCamera").objectReferenceValue = camera;
                    Property(serialized, "gridPresenter").objectReferenceValue = presenter;
                    Property(serialized, "pathService").objectReferenceValue = pathService;
                    SerializedProperty units = Property(serialized, "startingUnits");
                    units.arraySize = 2;
                    units.GetArrayElementAtIndex(0).objectReferenceValue = garciaUnit;
                    units.GetArrayElementAtIndex(1).objectReferenceValue = enterpriseUnit;
                    SerializedProperty cells = Property(serialized, "startingCells");
                    cells.arraySize = 2;
                    cells.GetArrayElementAtIndex(0).vector2IntValue = startingCells[0];
                    cells.GetArrayElementAtIndex(1).vector2IntValue = startingCells[1];
                    SerializedProperty catalog = Property(serialized, "availableUnits");
                    catalog.arraySize = 2;
                    catalog.GetArrayElementAtIndex(0).objectReferenceValue = garcia;
                    catalog.GetArrayElementAtIndex(1).objectReferenceValue = enterprise;
                });

                AssetDatabase.CreateAsset(mapDefinition, MapPath);
                AssetDatabase.SaveAssets();
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Could not save " + ScenePath);
                Debug.Log("[UnitControlSceneBuilder] Created " + ScenePath +
                    " with a 64x48 map, Garcia, Enterprise, and the two-unit spawn catalog.");
            }
            finally
            {
                // Additive creation leaves pre-existing scenes and unsaved edits intact.
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
            }
        }

        private static void Configure(UnityEngine.Object target, Action<SerializedObject> edit)
        {
            var serialized = new SerializedObject(target);
            serialized.Update();
            edit(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static SerializedProperty Property(SerializedObject serialized, string name)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
                throw new InvalidOperationException(serialized.targetObject.GetType().Name +
                    " has no serialized field named " + name);
            return property;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("Missing required asset: " + path);
            return asset;
        }

        private static T LoadGuid<T>(string guid) where T : UnityEngine.Object
        {
            return LoadRequired<T>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }
    }
}
