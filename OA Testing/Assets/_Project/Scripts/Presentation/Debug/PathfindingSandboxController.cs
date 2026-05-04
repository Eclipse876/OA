using System.Collections.Generic;
using OA.Presentation.Units;
using OA.Simulation.Navigation;
using OA.Simulation.Units;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OA.Presentation.Debug
{
    public sealed class PathfindingSandboxController : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private HexMapDefinition mapDefinition;
        [SerializeField] private PathfindingDebugSettings debugSettings;
        [SerializeField] private UnitArchetypeDefinition shipArchetype;
        [SerializeField] private ShipNavigationAgent shipAgent;
        [SerializeField] private MonoBehaviour pathServiceBehaviour;
        [SerializeField] private MonoBehaviour gridPresenterBehaviour;
        [SerializeField] private LineRenderer activeRouteLine;
        [SerializeField] private LineRenderer queuedRouteLine;

        [Header("Defaults")]
        [SerializeField] private Vector2Int guaranteedSpawnCell = new Vector2Int(2, 2);
        [SerializeField] private Vector2Int guaranteedGoalCell = new Vector2Int(31, 21);
        [SerializeField] private float routeSampleFactor = 0.2f;
        [SerializeField] private KeyCode rerollKey = KeyCode.R;
        [SerializeField] private bool logStatus = true;

        private readonly System.Random seedRng = new System.Random();
        private readonly HexMapGenerator generator = new HexMapGenerator();

        private readonly List<Vector2Int> cellPath = new List<Vector2Int>(512);
        private readonly List<Vector2> worldPath = new List<Vector2>(512);
        private readonly List<Vector2> routeCandidates = new List<Vector2>(512);
        private readonly List<Vector2> remainingRoutePoints = new List<Vector2>(512);
        private readonly Queue<Waypoint> destinationQueue = new Queue<Waypoint>();
        private readonly Vector2Int[] neighborBuffer = new Vector2Int[6];

        private INavigationPathService pathService;
        private INavigationGridPresenter gridPresenter;
        private HexMapRuntime map;

        private Waypoint? activeDestination;

        public HexMapRuntime CurrentMap => map;
        public INavigationPathService CurrentPathService => pathService;

        private void Awake()
        {
            if (!ResolveDependencies())
            {
                enabled = false;
                return;
            }

            if (!shipAgent.IsInitialized)
            {
                shipAgent.Initialize(shipArchetype, new Vector2(shipAgent.transform.position.x, shipAgent.transform.position.y));
            }

            BuildMapFromDefinition();
            RebuildNavigation();

            Vector2Int spawn = FindClosestTraversableCell(ClampCellToBounds(guaranteedSpawnCell));
            shipAgent.WarpTo(map.GetWorldCenter(spawn.x, spawn.y));

            ClearLines();
            LogStatus("Pathfinding sandbox initialized.");
        }

        private void Update()
        {
            HandleRerollHotkey();
            HandleClickToMoveInput();
            AdvanceQueuedRoutesIfNeeded();
            UpdateActiveRouteLine();
        }

        public bool IsCellTraversable(Vector2Int cell)
        {
            return IsTraversableForSafety(cell);
        }

        [ContextMenu("Rebuild Navigation From Map Definition")]
        public void RebuildFromMapDefinition()
        {
            if (mapDefinition == null)
            {
                UnityEngine.Debug.LogWarning("[PathfindingSandboxController] Map Definition is not assigned.");
                return;
            }

            BuildMapFromDefinition();
            RebuildNavigation();
            ClearLines();
            LogStatus("Rebuilt navigation from map definition.");
        }

        public void GenerateRuntimeDebugMap()
        {
            if (debugSettings == null || !debugSettings.enableRuntimeReroll)
            {
                LogStatus("Runtime reroll ignored. enableRuntimeReroll is false.");
                return;
            }

            if (map == null)
            {
                BuildMapFromDefinition();
            }

            int seed = debugSettings.fixedSeed > 0
                ? debugSettings.fixedSeed
                : seedRng.Next(1, int.MaxValue);

            Vector2Int spawn = ClampCellToBounds(guaranteedSpawnCell);
            Vector2Int goal = ClampCellToBounds(guaranteedGoalCell);

            generator.Generate(
                map,
                seed,
                debugSettings.obstacleChance,
                debugSettings.roughWaterChance,
                debugSettings.smoothingPasses,
                spawn,
                goal);

            RebuildNavigation();

            Vector2Int finalSpawn = FindClosestTraversableCell(spawn);
            shipAgent.WarpTo(map.GetWorldCenter(finalSpawn.x, finalSpawn.y));

            activeDestination = null;
            destinationQueue.Clear();
            shipAgent.SetPath(System.Array.Empty<Vector2>());
            ClearLines();

            LogStatus($"Runtime debug map generated. Seed={seed}");
        }

        private bool ResolveDependencies()
        {
            pathService = pathServiceBehaviour as INavigationPathService;
            if (pathService == null)
            {
                UnityEngine.Debug.LogError("[PathfindingSandboxController] pathServiceBehaviour must implement INavigationPathService.");
                return false;
            }

            gridPresenter = gridPresenterBehaviour as INavigationGridPresenter;
            if (gridPresenter == null)
            {
                UnityEngine.Debug.LogError("[PathfindingSandboxController] gridPresenterBehaviour must implement INavigationGridPresenter.");
                return false;
            }

            if (sceneCamera == null)
            {
                sceneCamera = Camera.main;
            }

            if (sceneCamera == null)
            {
                UnityEngine.Debug.LogError("[PathfindingSandboxController] Missing Scene Camera reference and no MainCamera found.");
                return false;
            }

            if (mapDefinition == null)
            {
                UnityEngine.Debug.LogError("[PathfindingSandboxController] Missing mapDefinition reference.");
                return false;
            }

            if (shipAgent == null)
            {
                UnityEngine.Debug.LogError("[PathfindingSandboxController] Missing shipAgent reference.");
                return false;
            }

            if (shipArchetype == null)
            {
                UnityEngine.Debug.LogError("[PathfindingSandboxController] Missing shipArchetype reference.");
                return false;
            }

            return true;
        }

        [SerializeField] private bool generateFromDefinitionMetadata = true;

        private void BuildMapFromDefinition()
        {
            if (!generateFromDefinitionMetadata)
            {
                map = mapDefinition.CreateRuntimeCopy();
                return;
            }

            map = new HexMapRuntime(mapDefinition.Width, mapDefinition.Height, mapDefinition.CellSize);

            Vector2Int spawn = ClampCellToBounds(guaranteedSpawnCell);
            Vector2Int goal = ClampCellToBounds(guaranteedGoalCell);

            generator.Generate(
                map,
                mapDefinition.Seed,
                mapDefinition.ObstacleChance,
                mapDefinition.RoughWaterChance,
                mapDefinition.SmoothingPasses,
                spawn,
                goal);
        }


        private void RebuildNavigation()
        {
            MovementProfileDefinition movement = shipAgent.MovementProfile;
            float safetyRadius = movement != null ? movement.safetyRadius : 0f;

            pathService.RebuildGraph(map, safetyRadius);
            gridPresenter.BuildOrRefresh(map, pathService.LastAppliedBlockedMask);
        }

        private void HandleRerollHotkey()
        {
            if (debugSettings == null || !debugSettings.enableRuntimeReroll || !debugSettings.enableRerollHotkey)
            {
                return;
            }

            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                pressed = Keyboard.current[(Key)System.Enum.Parse(typeof(Key), rerollKey.ToString())].wasPressedThisFrame;
            }
#else
            pressed = Input.GetKeyDown(rerollKey);
#endif
            if (pressed)
            {
                GenerateRuntimeDebugMap();
            }
        }

        private void HandleClickToMoveInput()
        {
            bool wasPressed;
            Vector2 mouseScreen;
            bool appendWaypoint;

#if ENABLE_INPUT_SYSTEM
            wasPressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            mouseScreen = wasPressed ? Mouse.current.position.ReadValue() : default;
            appendWaypoint = Keyboard.current != null &&
                             (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
#else
            wasPressed = Input.GetMouseButtonDown(0);
            mouseScreen = wasPressed ? (Vector2)Input.mousePosition : default;
            appendWaypoint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            if (!wasPressed)
            {
                return;
            }

            Vector3 world = sceneCamera.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -sceneCamera.transform.position.z));
            Vector2 world2 = new Vector2(world.x, world.y);

            Vector2Int targetCell;
            bool hasCell = gridPresenter.TryWorldToCell(world2, out targetCell);
            if (!hasCell && !map.TryWorldToCell(world2, out targetCell))
            {
                return;
            }

            HandleDestinationSelection(targetCell, world2, appendWaypoint);
        }

        private void HandleDestinationSelection(Vector2Int targetCell, Vector2 clickedWorld, bool appendWaypoint)
        {
            if (!IsTraversableForSafety(targetCell))
            {
                LogStatus("Selected cell is blocked for current safety radius.");
                return;
            }

            Waypoint destination = CreateWaypoint(targetCell, clickedWorld);

            if (!appendWaypoint)
            {
                destinationQueue.Clear();
                TryRouteToDestination(destination, false);
                UpdateQueuedRouteLine();
                return;
            }

            bool hasActiveLeg = activeDestination.HasValue || shipAgent.HasPath();
            if (hasActiveLeg)
            {
                destinationQueue.Enqueue(destination);
                UpdateQueuedRouteLine();
                LogStatus($"Waypoint queued ({targetCell.x}, {targetCell.y}) Queue={destinationQueue.Count}");
            }
            else
            {
                TryRouteToDestination(destination, false);
                UpdateQueuedRouteLine();
            }
        }

        private bool TryRouteToDestination(Waypoint destination, bool silent)
        {
            Vector2Int startCell;
            if (!gridPresenter.TryWorldToCell(GetShipPosition(), out startCell) &&
                !map.TryWorldToCell(GetShipPosition(), out startCell))
            {
                startCell = FindClosestTraversableCell(ClampCellToBounds(guaranteedSpawnCell));
            }

            startCell = FindClosestTraversableCell(startCell);
            Vector2Int goalCell = FindClosestTraversableCell(destination.Cell);

            bool foundPath = pathService.TryFindPath(startCell, goalCell, cellPath);
            if (!foundPath || cellPath.Count == 0)
            {
                if (!silent)
                {
                    LogStatus("No valid route found.");
                }

                shipAgent.SetPath(System.Array.Empty<Vector2>());
                UpdateActiveRouteLine();
                return false;
            }

            PathRouteSmoother.BuildRoute(
                map,
                pathService.LastAppliedBlockedMask,
                cellPath,
                GetShipPosition(),
                destination.World,
                worldPath,
                routeCandidates,
                routeSampleFactor);

            if (worldPath.Count < 2)
            {
                shipAgent.SetPath(System.Array.Empty<Vector2>());
                return false;
            }

            shipAgent.SetPath(worldPath);
            activeDestination = new Waypoint(goalCell, destination.World);

            if (!silent)
            {
                LogStatus($"Routing to ({goalCell.x}, {goalCell.y}) across {cellPath.Count} cells.");
            }

            return true;
        }

        private void AdvanceQueuedRoutesIfNeeded()
        {
            if (shipAgent.HasPath())
            {
                return;
            }

            if (!activeDestination.HasValue && destinationQueue.Count == 0)
            {
                return;
            }

            activeDestination = null;

            int skipped = 0;
            while (destinationQueue.Count > 0)
            {
                Waypoint next = destinationQueue.Dequeue();
                if (TryRouteToDestination(next, false))
                {
                    if (skipped > 0)
                    {
                        LogStatus($"Skipped {skipped} unreachable queued waypoint(s).");
                    }

                    UpdateQueuedRouteLine();
                    return;
                }

                skipped++;
            }

            if (skipped > 0)
            {
                LogStatus("Queue ended because remaining waypoints were unreachable.");
            }
            else
            {
                LogStatus("Route complete.");
            }

            UpdateQueuedRouteLine();
        }

        private Vector2Int FindClosestTraversableCell(Vector2Int desired)
        {
            if (!map.InBounds(desired.x, desired.y))
            {
                desired = new Vector2Int(map.Width / 2, map.Height / 2);
            }

            if (IsTraversableForSafety(desired))
            {
                return desired;
            }

            Queue<Vector2Int> queue = new Queue<Vector2Int>(64);
            HashSet<int> visited = new HashSet<int>();

            queue.Enqueue(desired);
            visited.Add(map.GetIndex(desired.x, desired.y));

            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                int neighbors = map.GetNeighborCount(current.x, current.y, neighborBuffer);

                for (int i = 0; i < neighbors; i++)
                {
                    Vector2Int next = neighborBuffer[i];
                    int index = map.GetIndex(next.x, next.y);

                    if (!visited.Add(index))
                    {
                        continue;
                    }

                    if (IsTraversableForSafety(next))
                    {
                        return next;
                    }

                    queue.Enqueue(next);
                }
            }

            return new Vector2Int(1, 1);
        }

        private bool IsTraversableForSafety(Vector2Int cell)
        {
            if (!map.InBounds(cell.x, cell.y))
            {
                return false;
            }

            bool[] mask = pathService.LastAppliedBlockedMask;
            if (mask == null || mask.Length == 0)
            {
                return map.IsWalkable(cell.x, cell.y);
            }

            int index = map.GetIndex(cell.x, cell.y);
            return index >= 0 && index < mask.Length && !mask[index];
        }

        private void UpdateActiveRouteLine()
        {
            if (activeRouteLine == null)
            {
                return;
            }

            shipAgent.GetRemainingPath(remainingRoutePoints);
            if (remainingRoutePoints.Count < 2)
            {
                activeRouteLine.positionCount = 0;
                return;
            }

            activeRouteLine.positionCount = remainingRoutePoints.Count;
            for (int i = 0; i < remainingRoutePoints.Count; i++)
            {
                Vector2 p = remainingRoutePoints[i];
                activeRouteLine.SetPosition(i, new Vector3(p.x, p.y, 0f));
            }
        }

        private void UpdateQueuedRouteLine()
        {
            if (queuedRouteLine == null)
            {
                return;
            }

            if (destinationQueue.Count == 0)
            {
                queuedRouteLine.positionCount = 0;
                return;
            }

            worldPath.Clear();

            if (activeDestination.HasValue)
            {
                worldPath.Add(activeDestination.Value.World);
            }
            else
            {
                worldPath.Add(GetShipPosition());
            }

            foreach (Waypoint wp in destinationQueue)
            {
                worldPath.Add(wp.World);
            }

            queuedRouteLine.positionCount = worldPath.Count;
            for (int i = 0; i < worldPath.Count; i++)
            {
                Vector2 p = worldPath[i];
                queuedRouteLine.SetPosition(i, new Vector3(p.x, p.y, 0f));
            }
        }

        private void ClearLines()
        {
            if (activeRouteLine != null)
            {
                activeRouteLine.positionCount = 0;
            }

            if (queuedRouteLine != null)
            {
                queuedRouteLine.positionCount = 0;
            }
        }

        private Vector2 GetShipPosition()
        {
            Vector3 p = shipAgent.transform.position;
            return new Vector2(p.x, p.y);
        }

        private Waypoint CreateWaypoint(Vector2Int cell, Vector2 clickedWorld)
        {
            Vector2 center = map.GetWorldCenter(cell.x, cell.y);
            Vector2 clamped = ClampPointToCell(clickedWorld, center);
            return new Waypoint(cell, clamped);
        }

        private Vector2 ClampPointToCell(Vector2 point, Vector2 center)
        {
            Vector2 delta = point - center;
            float maxOffset = map.CellSize * 0.4f;
            if (delta.sqrMagnitude > maxOffset * maxOffset)
            {
                delta = delta.normalized * maxOffset;
            }

            return center + delta;
        }

        private Vector2Int ClampCellToBounds(Vector2Int cell)
        {
            int x = Mathf.Clamp(cell.x, 0, mapDefinition.Width - 1);
            int y = Mathf.Clamp(cell.y, 0, mapDefinition.Height - 1);
            return new Vector2Int(x, y);
        }

        private void LogStatus(string message)
        {
            if (logStatus)
            {
                UnityEngine.Debug.Log($"[PathfindingSandbox] {message}");
            }
        }

        private struct Waypoint
        {
            public Vector2Int Cell;
            public Vector2 World;

            public Waypoint(Vector2Int cell, Vector2 world)
            {
                Cell = cell;
                World = world;
            }
        }
    }
}
