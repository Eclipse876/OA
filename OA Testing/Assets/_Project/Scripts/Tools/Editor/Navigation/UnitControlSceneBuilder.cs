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
                // A scene transition may unload assets held only in stack locals.
                // Resolve every persistent dependency after the transition.
                UnitArchetypeDefinition garcia = LoadRequired<UnitArchetypeDefinition>(
                    "Assets/_Project/Data/Scenarios/Navigation/ShipArchetype_Main.asset");
                UnitArchetypeDefinition enterprise = LoadRequired<UnitArchetypeDefinition>(
                    DataFolder + "/EnterpriseArchetype.asset");
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
                BakePreview(presenter, tilemap, mapDefinition, map);

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
                ValidateShip(garciaUnit, false);
                ValidateShip(enterpriseUnit, false);
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

        // Batch-only repair of the generated scene. It leaves other open scenes
        // and saved unit placements untouched, and refuses unsaved scene edits.
        public static void RepairPreview()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("RepairPreview must run in batch Edit mode.");
            LoadRequired<SceneAsset>(ScenePath);
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForRepair = !scene.IsValid() || !scene.isLoaded;
            if (!openedForRepair && scene.isDirty)
                throw new InvalidOperationException("Refusing to overwrite unsaved unit-control scene edits.");
            if (openedForRepair)
            {
                bool emptyStartup = string.IsNullOrEmpty(previousScene.path) &&
                    !previousScene.isDirty && previousScene.rootCount == 0;
                scene = EditorSceneManager.OpenScene(ScenePath,
                    emptyStartup ? OpenSceneMode.Single : OpenSceneMode.Additive);
            }
            SceneManager.SetActiveScene(scene);

            try
            {
                HexMapDefinition definition = LoadRequired<HexMapDefinition>(MapPath);
                TilemapHexGridPresenter presenter = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (TilemapHexGridPresenter candidate in root.GetComponentsInChildren<TilemapHexGridPresenter>(true))
                    {
                        if (presenter != null)
                            throw new InvalidOperationException("Unit scene contains more than one grid presenter.");
                        presenter = candidate;
                    }
                }
                if (presenter == null)
                    throw new InvalidOperationException("Unit scene has no grid presenter.");
                Tilemap tilemap = presenter.GetComponentInChildren<Tilemap>(true);
                if (tilemap == null)
                    throw new InvalidOperationException("Unit scene has no terrain Tilemap.");

                BakePreview(presenter, tilemap, definition, definition.CreateRuntimeCopy());
                int unitCount = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (SelectableShip unit in root.GetComponentsInChildren<SelectableShip>(true))
                    {
                        ValidateShip(unit, true);
                        unitCount++;
                    }
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Could not save " + ScenePath);
                Debug.Log($"[UnitControlSceneBuilder] Repaired preview: {definition.Width * definition.Height} tiles; " +
                    $"verified {unitCount} ship bindings. Saved only {ScenePath}.");
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
                if (openedForRepair && previousScene.IsValid() && previousScene.isLoaded &&
                    scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void BakePreview(TilemapHexGridPresenter presenter, Tilemap tilemap,
            HexMapDefinition definition, HexMapRuntime map)
        {
            TileBase deepWater = LoadGuid<TileBase>("18feb46b92ca7854baab2ca54fb4dc3a");
            TileBase roughWater = LoadGuid<TileBase>("f12ce85544d1d6f4183728b1b405e06a");
            TileBase obstacle = LoadGuid<TileBase>("19488b58fe65aa44da270ed068c26566");
            if (!EditorUtility.IsPersistent(deepWater) || !EditorUtility.IsPersistent(roughWater) ||
                !EditorUtility.IsPersistent(obstacle))
                throw new InvalidOperationException("Preview terrain must use persistent tile assets.");

            Configure(presenter, serialized =>
            {
                Property(serialized, "tilemap").objectReferenceValue = tilemap;
                Property(serialized, "previewDefinition").objectReferenceValue = definition;
                Property(serialized, "baseHexCellSize").vector2Value = new Vector2(0.8659766f, 1f);
                Property(serialized, "baseGridLocalScale").vector3Value = Vector3.one;
                Property(serialized, "deepWaterTile").objectReferenceValue = deepWater;
                Property(serialized, "roughWaterTile").objectReferenceValue = roughWater;
                Property(serialized, "obstacleTile").objectReferenceValue = obstacle;
                Property(serialized, "useProceduralTextureOverlay").boolValue = false;
            });
            presenter.BuildOrRefresh(map);
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                    if (!tilemap.HasTile(new Vector3Int(x, y, 0)))
                        throw new InvalidOperationException($"Preview failed to paint terrain cell ({x}, {y}).");
            Configure(presenter, serialized =>
                Property(serialized, "useProceduralTextureOverlay").boolValue = true);
            EditorUtility.SetDirty(tilemap);
            EditorUtility.SetDirty(tilemap.transform);
            EditorUtility.SetDirty(tilemap.layoutGrid.transform);
        }

        private static void ValidateShip(SelectableShip unit, bool repairMissingAgentArchetype)
        {
            if (unit.Archetype == null || unit.Archetype.movementProfile == null ||
                unit.Archetype.presentationProfile == null ||
                unit.Archetype.presentationProfile.worldSprite == null || unit.Agent == null ||
                unit.Hull == null || unit.Planner == null)
                throw new InvalidOperationException("Unit has missing archetype, profile, agent, hull, or planner: " + unit.name);

            var agent = new SerializedObject(unit.Agent);
            UnityEngine.Object assignedArchetype = Property(agent, "archetype").objectReferenceValue;
            if (assignedArchetype == null && repairMissingAgentArchetype)
            {
                unit.Agent.Initialize(unit.Archetype, unit.transform.position);
                EditorUtility.SetDirty(unit.Agent);
                EditorUtility.SetDirty(unit.Hull);
                EditorUtility.SetDirty(unit.Hull.transform);
                agent.Update();
                assignedArchetype = Property(agent, "archetype").objectReferenceValue;
            }
            if (assignedArchetype != unit.Archetype ||
                unit.Hull.sprite != unit.Archetype.presentationProfile.worldSprite)
                throw new InvalidOperationException("Unit's agent archetype or hull sprite does not match: " + unit.name);
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
