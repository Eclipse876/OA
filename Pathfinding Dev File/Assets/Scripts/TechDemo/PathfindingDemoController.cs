using System;
using System.Collections.Generic;
using OA.Simulation.Units;
using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OA.TechDemo
{
    public sealed class PathfindingDemoController : MonoBehaviour
    {
        private const int MapWidth = 42;
        private const int MapHeight = 28;
        private const float CellSize = 1f;
        private const float ShipY = 0.55f;
        private const float DefaultObstacleChance = 0.2f;
        private const float DefaultRoughWaterChance = 0.22f;
        private const int DefaultSmoothingPasses = 4;

        private readonly Plane _groundPlane = new Plane(Vector3.up, Vector3.zero);
        private readonly Random _seedRng = new Random();
        private readonly List<Vector2Int> _cellPath = new List<Vector2Int>(512);
        private readonly List<Vector3> _worldPath = new List<Vector3>(512);

        private readonly AStarPathfinder _pathfinder = new AStarPathfinder();
        private readonly RandomGridMapGenerator _mapGenerator = new RandomGridMapGenerator();

        private Camera _camera;
        private GridMap _map;
        private GridMapVisual _mapVisual;
        private ShipAgentController _shipAgent;
        private LineRenderer _pathLine;
        private PathfindingDemoUi _ui;

        private UnitArchetypeDefinition _archetype;
        private int _currentSeed;
        private float _currentObstacleChance;
        private Vector2Int? _lastDestinationCell;

        public MovementProfileDefinition MovementProfile => _archetype != null ? _archetype.movementProfile : null;
        public int CurrentSeed => _currentSeed;
        public float CurrentObstacleChance => _currentObstacleChance;

        private void Awake()
        {
            EnsureCameraAndLighting();
            BuildArchetypeAndShip();
            BuildMapAndVisuals();
            BuildUi();

            GenerateMap(GetRandomSeed(), DefaultObstacleChance);
            _ui.SyncMapInputs(_currentSeed, _currentObstacleChance);
            _ui.RefreshTraitInputs();
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
            _lastDestinationCell = null;

            Vector2Int guaranteedStart = new Vector2Int(3, 3);
            Vector2Int guaranteedGoal = new Vector2Int(MapWidth - 4, MapHeight - 4);

            _mapGenerator.Generate(
                _map,
                _currentSeed,
                _currentObstacleChance,
                DefaultRoughWaterChance,
                DefaultSmoothingPasses,
                guaranteedStart,
                guaranteedGoal);

            _mapVisual.Refresh(_map);

            Vector2Int spawnCell = FindClosestWalkableCell(guaranteedStart);
            _shipAgent.WarpTo(_map.GridToWorld(spawnCell.x, spawnCell.y, ShipY));
            UpdatePathLine(null);

            SetStatus($"Map generated. Seed: {_currentSeed}. Left-click a tile to route the ship.");
        }

        public void OnMovementTraitsChanged()
        {
            if (MovementProfile == null)
            {
                return;
            }

            MovementProfile.maxSpeed = Mathf.Max(0.1f, MovementProfile.maxSpeed);
            MovementProfile.acceleration = Mathf.Max(0f, MovementProfile.acceleration);
            MovementProfile.deceleration = Mathf.Max(0f, MovementProfile.deceleration);
            MovementProfile.turnRateDegreesPerSecond = Mathf.Max(0f, MovementProfile.turnRateDegreesPerSecond);
            MovementProfile.turningRadius = Mathf.Max(0f, MovementProfile.turningRadius);
            MovementProfile.safetyRadius = Mathf.Max(0f, MovementProfile.safetyRadius);
            MovementProfile.stoppingDistance = Mathf.Max(0.01f, MovementProfile.stoppingDistance);

            if (_lastDestinationCell.HasValue)
            {
                TryPathToCell(_lastDestinationCell.Value, true);
            }
            else
            {
                SetStatus("Traits updated. Click a destination to test the new movement behavior.");
            }
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

            if (wasPressed)
            {
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

                TryPathToCell(targetCell, false);
            }
        }

        private void TryPathToCell(Vector2Int targetCell, bool silent)
        {
            if (!_map.IsWalkable(targetCell.x, targetCell.y))
            {
                if (!silent)
                {
                    SetStatus("That tile is blocked by terrain. Pick an open-water tile.");
                }

                return;
            }

            if (!_map.WorldToGrid(_shipAgent.transform.position, out Vector2Int startCell))
            {
                startCell = FindClosestWalkableCell(targetCell);
            }

            bool foundPath = _pathfinder.TryFindPath(
                _map,
                startCell,
                targetCell,
                MovementProfile.safetyRadius,
                _cellPath);

            if (!foundPath || _cellPath.Count == 0)
            {
                if (!silent)
                {
                    SetStatus("No route found with the current safety radius. Lower safety radius or pick another destination.");
                }

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
            _lastDestinationCell = targetCell;

            if (!silent)
            {
                SetStatus($"Path found: {_cellPath.Count} nodes. Ship routing to ({targetCell.x}, {targetCell.y}).");
            }
        }

        private void BuildArchetypeAndShip()
        {
            var movement = ScriptableObject.CreateInstance<MovementProfileDefinition>();
            movement.maxSpeed = 8f;
            movement.acceleration = 7f;
            movement.deceleration = 10f;
            movement.turnRateDegreesPerSecond = 120f;
            movement.turningRadius = 2.5f;
            movement.safetyRadius = 1f;
            movement.stoppingDistance = 0.2f;

            var presentation = ScriptableObject.CreateInstance<PresentationProfileDefinition>();
            presentation.hullColor = new Color(0.77f, 0.9f, 1f);

            _archetype = ScriptableObject.CreateInstance<UnitArchetypeDefinition>();
            _archetype.displayName = "Pathfinder Test Ship";
            _archetype.movementProfile = movement;
            _archetype.presentationProfile = presentation;

            GameObject ship = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            ship.name = "OA_Ship_Runtime";
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
                shipRenderer.material.color = presentation.hullColor;
            }

            GameObject bow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bow.name = "Bow";
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
                bowRenderer.material.color = new Color(0.2f, 0.6f, 1f);
            }

            _shipAgent = ship.AddComponent<ShipAgentController>();
            _shipAgent.Initialize(_archetype, ship.transform.position);
        }

        private void BuildMapAndVisuals()
        {
            _map = new GridMap(MapWidth, MapHeight, CellSize);

            var mapVisualObject = new GameObject("OA_GridMapVisual");
            _mapVisual = mapVisualObject.AddComponent<GridMapVisual>();
            _mapVisual.Build(_map);

            var pathObject = new GameObject("OA_PathLine");
            _pathLine = pathObject.AddComponent<LineRenderer>();
            _pathLine.material = new Material(Shader.Find("Sprites/Default"));
            _pathLine.useWorldSpace = true;
            _pathLine.widthCurve = AnimationCurve.Constant(0f, 1f, 0.14f);
            _pathLine.numCapVertices = 4;
            _pathLine.numCornerVertices = 4;
            _pathLine.positionCount = 0;
            _pathLine.startColor = new Color(0.87f, 0.98f, 1f, 0.95f);
            _pathLine.endColor = new Color(0.35f, 0.85f, 1f, 0.95f);
        }

        private void BuildUi()
        {
            var uiObject = new GameObject("OA_DemoUi");
            _ui = uiObject.AddComponent<PathfindingDemoUi>();
            _ui.Initialize(this, MovementProfile);
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
            _camera.backgroundColor = new Color(0.03f, 0.08f, 0.12f);
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

        private Vector2Int FindClosestWalkableCell(Vector2Int desired)
        {
            if (_map.IsWalkable(desired.x, desired.y))
            {
                return desired;
            }

            int maxRadius = Mathf.Max(_map.Width, _map.Height);
            for (int radius = 1; radius < maxRadius; radius++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (Mathf.Abs(x) != radius && Mathf.Abs(y) != radius)
                        {
                            continue;
                        }

                        int nx = desired.x + x;
                        int ny = desired.y + y;

                        if (_map.IsWalkable(nx, ny))
                        {
                            return new Vector2Int(nx, ny);
                        }
                    }
                }
            }

            return new Vector2Int(1, 1);
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
