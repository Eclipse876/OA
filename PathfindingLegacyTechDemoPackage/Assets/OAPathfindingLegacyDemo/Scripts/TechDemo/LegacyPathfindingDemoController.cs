using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OA.LegacyTechDemo
{
    public sealed class LegacyPathfindingDemoController : MonoBehaviour
    {
        private const int MapWidth = 42;
        private const int MapHeight = 28;
        private const float CellSize = 1f;
        private const float ShipY = 0.55f;
        private const float DefaultObstacleChance = 0.2f;
        private const float DefaultRoughWaterChance = 0.22f;
        private const int CenterSpawnGateRadius = 3;

        private readonly Plane _groundPlane = new Plane(Vector3.up, Vector3.zero);
        private readonly System.Random _seedRng = new System.Random();
        private readonly List<Vector2Int> _cellPath = new List<Vector2Int>(512);
        private readonly List<Vector3> _worldPath = new List<Vector3>(512);

        private readonly LegacyGridPathfinder _pathfinder = new LegacyGridPathfinder();
        private readonly LegacyRandomGridMapGenerator _mapGenerator = new LegacyRandomGridMapGenerator();

        private Camera _camera;
        private LegacyGridMap _map;
        private LegacyGridMapVisual _mapVisual;
        private LegacyShipAgent _shipAgent;
        private LineRenderer _pathLine;
        private LegacyPathfindingDemoUi _ui;

        private int _currentSeed;
        private float _currentObstacleChance;

        public int CurrentSeed => _currentSeed;
        public float CurrentObstacleChance => _currentObstacleChance;
        private static Vector2Int CenterSpawnCell => new Vector2Int(MapWidth / 2, MapHeight / 2);

        private void Awake()
        {
            EnsureCameraAndLighting();
            BuildShip();
            BuildMapAndVisuals();
            BuildUi();

            GenerateMap(GetRandomSeed(), DefaultObstacleChance);
            _ui.SyncMapInputs(_currentSeed, _currentObstacleChance);
        }

        private void Update()
        {
            HandleClickToMoveInput();
        }

        public int GetRandomSeed()
        {
            return _seedRng.Next(1, int.MaxValue);
        }

        public void GenerateMap(int seed, float obstacleChance)
        {
            if (_map == null || _mapVisual == null)
            {
                return;
            }

            _currentSeed = seed;
            _currentObstacleChance = Mathf.Clamp(obstacleChance, 0.05f, 0.45f);

            Vector2Int spawnCell = CenterSpawnCell;
            Vector2Int guaranteedGoal = new Vector2Int(MapWidth - 4, MapHeight - 4);

            _mapGenerator.Generate(
                _map,
                _currentSeed,
                _currentObstacleChance,
                DefaultRoughWaterChance,
                spawnCell,
                guaranteedGoal,
                CenterSpawnGateRadius);

            _mapVisual.Refresh(_map);
            _shipAgent.WarpTo(_map.GridToWorld(spawnCell.x, spawnCell.y, ShipY));
            UpdatePathLine(null);

            SetStatus($"Legacy map rerolled. Seed: {_currentSeed}. This build uses basic cell-by-cell A*.");
        }

        public void SetStatus(string message)
        {
            if (_ui != null)
            {
                _ui.SetStatus(message);
            }
        }

        private void HandleClickToMoveInput()
        {
            bool wasPressed;
            Vector2 mouseScreenPosition;

#if ENABLE_INPUT_SYSTEM
            wasPressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            mouseScreenPosition = wasPressed ? Mouse.current.position.ReadValue() : default;
#else
            wasPressed = Input.GetMouseButtonDown(0);
            mouseScreenPosition = wasPressed ? Input.mousePosition : default;
#endif

            if (!wasPressed)
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            Ray ray = _camera.ScreenPointToRay(mouseScreenPosition);
            if (!_groundPlane.Raycast(ray, out float enter))
            {
                return;
            }

            Vector3 hit = ray.GetPoint(enter);
            if (!_map.WorldToGrid(hit, out Vector2Int targetCell))
            {
                return;
            }

            TryPathToCell(targetCell);
        }

        private void TryPathToCell(Vector2Int targetCell)
        {
            if (!_map.IsWalkable(targetCell.x, targetCell.y))
            {
                SetStatus("That tile is blocked by terrain. Pick an open-water tile.");
                return;
            }

            if (!_map.WorldToGrid(_shipAgent.transform.position, out Vector2Int startCell))
            {
                startCell = CenterSpawnCell;
            }

            bool foundPath = _pathfinder.TryFindPath(_map, startCell, targetCell, _cellPath);

            if (!foundPath || _cellPath.Count == 0)
            {
                SetStatus("No route found in the legacy grid pathfinder.");
                UpdatePathLine(null);
                return;
            }

            _worldPath.Clear();
            for (int i = 0; i < _cellPath.Count; i++)
            {
                Vector2Int cell = _cellPath[i];
                _worldPath.Add(_map.GridToWorld(cell.x, cell.y, ShipY));
            }

            if (_worldPath.Count > 0)
            {
                _worldPath[0] = _shipAgent.transform.position;
            }

            _shipAgent.SetPath(_worldPath);
            UpdatePathLine(_worldPath);
            SetStatus($"Legacy path found: {_cellPath.Count} cell steps.");
        }

        private void BuildShip()
        {
            GameObject ship = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            ship.name = "OA_LegacyShip_Runtime";
            ship.transform.position = new Vector3(0f, ShipY, 0f);
            ship.transform.localScale = new Vector3(0.7f, 0.4f, 1.3f);

            var shipCollider = ship.GetComponent<Collider>();
            if (shipCollider != null)
            {
                Destroy(shipCollider);
            }

            var shipRenderer = ship.GetComponent<Renderer>();
            if (shipRenderer != null)
            {
                shipRenderer.material.color = new Color(1f, 0.82f, 0.47f);
            }

            GameObject bow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bow.name = "LegacyBow";
            bow.transform.SetParent(ship.transform, false);
            bow.transform.localPosition = new Vector3(0f, 0.9f, 0.8f);
            bow.transform.localScale = new Vector3(0.22f, 0.22f, 0.45f);

            var bowCollider = bow.GetComponent<Collider>();
            if (bowCollider != null)
            {
                Destroy(bowCollider);
            }

            var bowRenderer = bow.GetComponent<Renderer>();
            if (bowRenderer != null)
            {
                bowRenderer.material.color = new Color(0.95f, 0.44f, 0.18f);
            }

            _shipAgent = ship.AddComponent<LegacyShipAgent>();
        }

        private void BuildMapAndVisuals()
        {
            _map = new LegacyGridMap(MapWidth, MapHeight, CellSize);

            var mapVisualObject = new GameObject("OA_LegacyGridMapVisual");
            _mapVisual = mapVisualObject.AddComponent<LegacyGridMapVisual>();
            _mapVisual.Build(_map);

            var pathObject = new GameObject("OA_LegacyPathLine");
            _pathLine = pathObject.AddComponent<LineRenderer>();
            _pathLine.material = new Material(Shader.Find("Sprites/Default"));
            _pathLine.useWorldSpace = true;
            _pathLine.widthCurve = AnimationCurve.Constant(0f, 1f, 0.12f);
            _pathLine.numCapVertices = 2;
            _pathLine.numCornerVertices = 2;
            _pathLine.positionCount = 0;
            _pathLine.startColor = new Color(1f, 0.9f, 0.48f, 0.95f);
            _pathLine.endColor = new Color(1f, 0.48f, 0.22f, 0.95f);
        }

        private void BuildUi()
        {
            var uiObject = new GameObject("OA_LegacyDemoUi");
            _ui = uiObject.AddComponent<LegacyPathfindingDemoUi>();
            _ui.Initialize(this);
        }

        private void EnsureCameraAndLighting()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                _camera = cameraObject.AddComponent<Camera>();
                _camera.tag = "MainCamera";
            }

            _camera.orthographic = true;
            _camera.orthographicSize = 16f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 200f;
            _camera.transform.position = new Vector3(0f, 32f, 0f);
            _camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _camera.backgroundColor = new Color(0.04f, 0.07f, 0.09f);
            _camera.clearFlags = CameraClearFlags.SolidColor;

            if (FindObjectOfType<Light>() == null)
            {
                var lightObject = new GameObject("Directional Light");
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            }
        }

        private void UpdatePathLine(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count == 0)
            {
                _pathLine.positionCount = 0;
                return;
            }

            _pathLine.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                p.y = 0.2f;
                _pathLine.SetPosition(i, p);
            }
        }
    }
}
