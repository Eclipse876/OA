// PathfindingSandboxController.cs:
// This is the pathfinding playground glue. It builds the map, hooks up the
// presenter, listens for clicks/rerolls, tells the ship where to go, and keeps
// the test scene from exploding.
using System.Collections.Generic;
using OA.Presentation.Units;
using OA.Simulation.Movement;
using OA.Simulation.Navigation;
using OA.Simulation.Units;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OA.Presentation.Debug
{
    // Scene controller for the navigation sandbox. Big coordinator energy, but for boats.
    public sealed class PathfindingSandboxController : MonoBehaviour
    {
        // Scene references wired in from the prefab/scene. Most bugs here are just missing links.
        [Header("Scene References")]
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private HexMapDefinition mapDefinition;
        [SerializeField] private PathfindingDebugSettings debugSettings;
        [SerializeField] private UnitArchetypeDefinition shipArchetype;
        [SerializeField] private ShipNavigationAgent shipAgent;
        [SerializeField] private MonoBehaviour pathServiceBehaviour;
        [SerializeField] private MonoBehaviour gridPresenterBehaviour;
        [SerializeField] private RouteLineRenderer activeRouteLine;
        [SerializeField] private LineRenderer queuedRouteLine;

        [Header("Waypoint Display")]
        [SerializeField] private Sprite waypointMarkerSprite;
        [SerializeField] private Sprite nextWaypointMarkerSprite;
        [SerializeField] private Color lastWaypointMarkerColor = Color.black;
        [SerializeField] private Transform waypointMarkerRoot;
        [SerializeField] private int waypointMarkerOrderInLayer = 11;
        [SerializeField, Min(0.01f)] private float waypointMarkerScale = 0.35f;

        // Default cells and tuning for click routing, rerolls, and debug line drawing.
        [Header("Defaults")]
        [SerializeField] private Vector2Int guaranteedSpawnCell = new Vector2Int(2, 2);
        [SerializeField] private Vector2Int guaranteedGoalCell = new Vector2Int(31, 21);
        [SerializeField] private float routeSampleFactor = 0.2f;
        [SerializeField] private KeyCode rerollKey = KeyCode.R;
        [SerializeField] private MovementSpeedMode defaultSpeedMode = MovementSpeedMode.Cruise;
        [SerializeField] private KeyCode fastMoveToggleKey = KeyCode.F;
        [SerializeField] private bool logStatus = true;

        [Header("Kinematic Route Validation")]
        [SerializeField, Min(0)] private int kinematicClearanceAttempts = 3;
        [SerializeField, Min(0f)] private float kinematicClearanceStepWorld = 0.5f;

        [Header("Confined Turn Recovery")]
        [SerializeField, Min(0)] private int constrainedTurnAttempts = 3;
        [SerializeField, Range(0.01f, 0.95f)] private float constrainedTurnSpeedScaleStep = 0.3f;
        [SerializeField, Range(0.02f, 1f)] private float minimumConstrainedTurnSpeedScale = 0.1f;

        [Header("Planning Budget")]
        [SerializeField, Min(0.1f)] private float planningBudgetMillisecondsPerFrame = 2f;
        [SerializeField, Range(0.05f, 1f)] private float severeTurnSpeedFraction = 0.35f;

        // Reused generator/path buffers so click-to-move does not allocate more than it needs to.
        private readonly System.Random seedRng = new System.Random();
        private readonly HexMapGenerator generator = new HexMapGenerator();

        private readonly List<Vector2Int> cellPath = new List<Vector2Int>(512);
        private readonly List<Vector2> worldPath = new List<Vector2>(512);
        private readonly List<Vector2> routeCandidates = new List<Vector2>(512);
        private readonly List<Vector2> remainingRoutePoints = new List<Vector2>(512);
        private readonly List<Waypoint> routeWaypoints = new List<Waypoint>(32);
        private readonly List<Vector2> fullWorldPath = new List<Vector2>(512);
        private readonly List<float> fullWorldWaypointDistances = new List<float>(32);
        private readonly List<float> acceptedWaypointDistances = new List<float>(32);
        private readonly List<float> bestCandidateWaypointDistances = new List<float>(32);
        private readonly ShipRoute activeRoute = new ShipRoute();
        private readonly ShipRoute candidateRoute = new ShipRoute();
        private readonly ShipRoute bestCandidateRoute = new ShipRoute();
        private readonly List<Waypoint> previousRouteWaypoints = new List<Waypoint>(32);
        private readonly List<Waypoint> committedRouteWaypoints = new List<Waypoint>(32);
        private readonly List<RouteGeometryCandidate> pendingCandidates = new List<RouteGeometryCandidate>(16);
        private readonly List<RouteGeometryCandidate> pendingNormalCandidates = new List<RouteGeometryCandidate>(8);
        private readonly ShipRoutePrediction pendingPrediction = new ShipRoutePrediction();
        private readonly List<SpriteRenderer> waypointMarkerPool = new List<SpriteRenderer>(32);
        private readonly Vector2Int[] neighborBuffer = new Vector2Int[6];
        private SpriteRenderer nextWaypointMarkerRenderer;
        private SpriteRenderer lastWaypointMarkerRenderer;
        private int activeRouteVisibleSampleIndex;

        // Runtime services resolved from the assigned MonoBehaviours in Awake.
        private INavigationPathService pathService;
        private INavigationGridPresenter gridPresenter;
        private HexMapRuntime map;

        private Waypoint? activeDestination;
        private NavigationProfile displayedNavigationProfile;
        private NavigationTraversalMask displayedTraversalMask;
        private NavigationTraversalMask pendingRequiredMask;
        private NavigationProfile pendingRequiredProfile;
        private System.Diagnostics.Stopwatch pendingPlanningStopwatch;
        private bool hasPendingPlanning;
        private bool pendingPlanSilent;
        private bool pendingDirectTested;
        private bool pendingRecoveryQueued;
        private bool pendingGeometryUnavailable;
        private int pendingCandidateIndex;
        private int pendingClearanceAttempt;
        private int pendingPhysicalCandidates;
        private int pendingPhysicalSteps;
        private int pendingAStarLegs;
        private int pendingPlanFrames;
        private int pendingMaskHitsAtStart;
        private int pendingMaskMissesAtStart;
        private float pendingBestExtraClearance;
        private float pendingBestTurnSpeedScale;
        private ShipRouteFailureReason pendingLastFailureReason;
        private Vector2 pendingLastFailurePosition;
        private float pendingLastFailureTimeSeconds;

        public HexMapRuntime CurrentMap => map;
        public INavigationPathService CurrentPathService => pathService;

        // Resolves dependencies, builds the first map, rebuilds navigation, and places the ship.
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

            shipAgent.SetSpeedMode(defaultSpeedMode);


            BuildMapFromDefinition();
            RebuildNavigation();

            Vector2Int spawn = FindClosestTraversableCell(ClampCellToBounds(guaranteedSpawnCell));
            shipAgent.WarpTo(map.GetWorldCenter(spawn.x, spawn.y));

            ClearLines();
            LogStatus("Pathfinding sandbox initialized.");
        }

        // Per-frame sandbox loop: input, queued routes, and debug route visuals.
        private void Update()
        {
            HandleRerollHotkey();
            HandleFastMoveToggle();
            HandleClickToMoveInput();
            AdvancePendingRoutePlanning();
            RetirePassedWaypoints();
            HandleRouteCompletion();
            UpdateActiveRouteLine();
        }


        public void HandleFastMoveToggle()
        {
            bool pressed = false;

            #if ENABLE_INPUT_SYSTEM
                if (Keyboard.current != null)
                {
                    Key configuredKey = (Key)System.Enum.Parse(
                        typeof(Key),
                        fastMoveToggleKey.ToString());

                    pressed = Keyboard.current[configuredKey].wasPressedThisFrame;
                }
            #else
                pressed = Input.GetKeyDown(fastMoveToggleKey);
            #endif

            if (!pressed)
            {
                return;
            }

            MovementSpeedMode previousMode = shipAgent.SpeedMode;
            shipAgent.ToggleSpeedMode();

            if (routeWaypoints.Count > 0)
            {
                if (!TryRouteThroughWaypoints(true))
                {
                    shipAgent.SetSpeedMode(previousMode);
                    TryRouteThroughWaypoints(true);
                    LogStatus("Requested speed mode cannot execute the active route.");
                    return;
                }
            }

            LogStatus($"Speed Mode: {shipAgent.SpeedMode}");
        }

        // Public helper for benchmark/debug callers to ask about the current safety mask.
        public bool IsCellTraversable(Vector2Int cell)
        {
            return IsTraversableForSafety(cell);
        }

        [ContextMenu("Rebuild Navigation From Map Definition")]
        // Context-menu rebuild using the assigned map definition asset.
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

        // Rerolls the runtime map when debug settings allow it, then resets the ship and routes.
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

            CancelPendingRoutePlanning();

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
            routeWaypoints.Clear();
            committedRouteWaypoints.Clear();
            acceptedWaypointDistances.Clear();
            activeRoute.Clear();
            activeRouteVisibleSampleIndex = 0;
            candidateRoute.Clear();
            shipAgent.SetPath(System.Array.Empty<Vector2>());
            ClearLines();
            RefreshWaypointMarkers();

            LogStatus($"Runtime debug map generated. Seed={seed}");
        }

        // Pulls required scene references into interfaces and fails loudly if anything is missing.
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

        // Toggle between "use baked arrays" and "reroll from asset metadata at startup."
        [SerializeField] private bool generateFromDefinitionMetadata = true;

        // Builds the runtime map either from saved cell arrays or freshly generated metadata.
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


        // Converts the selected ship's public profile into navigation clearance rules.
        // Extra clearance is used only while looking for a route its turning arc can execute.
        private NavigationProfile CreateNavigationProfile(float addedClearance = 0f)
        {
            MovementProfileDefinition movement = shipAgent.MovementProfile;
            float safetyRadius = movement != null
                ? movement.safetyRadius + Mathf.Max(0f, addedClearance)
                : Mathf.Max(0f, addedClearance);

            ShipDraftClass draftClass = movement != null
                ? movement.draftClass
                : ShipDraftClass.Shallow;

            return new NavigationProfile(safetyRadius, draftClass);
        }

        // Rebuilds pathfinding and redraws the grid with the normal ship safety mask.
        private void RebuildNavigation()
        {
            RebuildNavigation(CreateNavigationProfile());
        }

        // Rebuilds graph topology only when the underlying map has changed.
        private void RebuildNavigation(NavigationProfile profile)
        {
            CancelPendingRoutePlanning();
            shipAgent.SetNavigationMap(map);

            // First paint establishes the exact world-space center of every visible cell.
            gridPresenter.BuildOrRefresh(map, null);

            // The A* point graph is built directly on those visible centers.
            pathService.RebuildGraph(map, profile);

            DisplayAppliedNavigationProfile(profile);
        }

        // Updates debug shading only when a profile becomes the accepted visible profile.
        private void DisplayAppliedNavigationProfile(NavigationProfile profile)
        {
            displayedTraversalMask = pathService.GetTraversalMask(map, profile);
            gridPresenter.BuildOrRefresh(
                map,
                displayedTraversalMask != null
                    ? displayedTraversalMask.BlockedCells
                    : null);
            displayedNavigationProfile = profile;
        }

        // Two safety radii that expand to the same number of cells produce the same route mask.
        private bool ProfilesUseSameTraversalMask(
            NavigationProfile a,
            NavigationProfile b)
        {
            int aSteps = Mathf.CeilToInt(
                Mathf.Max(0f, a.SafetyRadiusWorld) /
                Mathf.Max(0.001f, map.CellSize));

            int bSteps = Mathf.CeilToInt(
                Mathf.Max(0f, b.SafetyRadiusWorld) /
                Mathf.Max(0.001f, map.CellSize));

            return a.DraftClass == b.DraftClass &&
                   aSteps == bSteps;
        }

        // Makes sure temporary clearance used to search for a wider course never
        // becomes the ship's persistent legal-water or click-selection mask.
        private NavigationProfile EnsureRequiredNavigationProfileApplied()
        {
            NavigationProfile requiredProfile = CreateNavigationProfile();

            if (!ProfilesUseSameTraversalMask(
                displayedNavigationProfile,
                requiredProfile) ||
                displayedTraversalMask == null ||
                displayedTraversalMask.MapVersion != map.Version)
            {
                DisplayAppliedNavigationProfile(requiredProfile);
            }

            return requiredProfile;
        }

        // Watches the reroll hotkey, supporting both the new Input System and old input path.
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

        // Handles click-to-move and optional shift-click waypoint queueing.
        private void HandleClickToMoveInput()
        {
            bool wasPressed;
            Vector2 mouseScreen;
            bool appendWaypoint;
            bool insertWaypointAtFront;

#if ENABLE_INPUT_SYSTEM
            wasPressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            mouseScreen = wasPressed ? Mouse.current.position.ReadValue() : default;
            appendWaypoint = Keyboard.current != null &&
                             (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
            insertWaypointAtFront = appendWaypoint &&
                                    Keyboard.current != null &&
                                    (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
#else
            wasPressed = Input.GetMouseButtonDown(0);
            mouseScreen = wasPressed ? (Vector2)Input.mousePosition : default;
            appendWaypoint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            insertWaypointAtFront = appendWaypoint &&
                                    (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
#endif
            if (!wasPressed)
            {
                return;
            }

            // Convert mouse screen position to the 2D world plane the grid lives on.
            Vector3 world = sceneCamera.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -sceneCamera.transform.position.z));
            Vector2 world2 = new Vector2(world.x, world.y);

            Vector2Int targetCell;
            bool hasCell = gridPresenter.TryWorldToCell(world2, out targetCell);
            if (!hasCell && !map.TryWorldToCell(world2, out targetCell))
            {
                return;
            }

            HandleDestinationSelection(
                targetCell,
                world2,
                appendWaypoint,
                insertWaypointAtFront);
        }

        // Turns a clicked cell into either an immediate destination or a queued waypoint.
        private void HandleDestinationSelection(
            Vector2Int targetCell,
            Vector2 clickedWorld,
            bool appendWaypoint,
            bool insertWaypointAtFront)
        {
            EnsureRequiredNavigationProfileApplied();

            if (!IsTraversableForSafety(targetCell))
            {
                LogStatus("Selected cell is blocked for the current ship.");
                return;
            }

            // Editing an order that is still being evaluated starts from the
            // route the ship has actually accepted, not from speculative markers.
            if (hasPendingPlanning)
            {
                CancelPendingRoutePlanning();
                routeWaypoints.Clear();
                routeWaypoints.AddRange(committedRouteWaypoints);
            }

            previousRouteWaypoints.Clear();
            previousRouteWaypoints.AddRange(routeWaypoints);

            if (!appendWaypoint)
            {
                routeWaypoints.Clear();
            }

            Waypoint waypoint = CreateWaypoint(targetCell, clickedWorld);

            if (insertWaypointAtFront && routeWaypoints.Count > 0)
            {
                routeWaypoints.Insert(0, waypoint);
            }
            else
            {
                routeWaypoints.Add(waypoint);
            }

            if (TryRouteThroughWaypoints(false))
            {
                RefreshWaypointMarkers();
                UpdateQueuedRouteLine();
                return;
            }

            // A failed append/replacement should leave an already valid course in service.
            routeWaypoints.Clear();
            routeWaypoints.AddRange(previousRouteWaypoints);

            if (routeWaypoints.Count > 0)
            {
                TryRouteThroughWaypoints(true);
            }

            UpdateQueuedRouteLine();
        }

        // Begins one continuous route through every requested waypoint.
        // Intermediate waypoints are pass-through locations; only the final destination stops the ship.
        // Exact motion evaluation is advanced in Update instead of blocking this click.
        private bool TryRouteThroughWaypoints(bool silent)
        {
            if (routeWaypoints.Count == 0)
            {
                CancelPendingRoutePlanning();
                return false;
            }

            CancelPendingRoutePlanning();
            pendingRequiredProfile = EnsureRequiredNavigationProfileApplied();
            pendingRequiredMask = pathService.GetTraversalMask(
                map,
                pendingRequiredProfile);

            if (pendingRequiredMask == null)
            {
                return false;
            }

            hasPendingPlanning = true;
            pendingPlanSilent = silent;
            pendingDirectTested = true;
            pendingRecoveryQueued = false;
            pendingGeometryUnavailable = false;
            pendingCandidateIndex = 0;
            pendingClearanceAttempt = 0;
            pendingPhysicalCandidates = 0;
            pendingPhysicalSteps = 0;
            pendingAStarLegs = 0;
            pendingPlanFrames = 0;
            pendingLastFailureReason = ShipRouteFailureReason.None;
            pendingLastFailurePosition = default;
            pendingLastFailureTimeSeconds = 0f;
            pendingBestExtraClearance = 0f;
            pendingBestTurnSpeedScale = 1f;
            pendingMaskHitsAtStart = pathService.TraversalMaskCacheHits;
            pendingMaskMissesAtStart = pathService.TraversalMaskCacheMisses;
            pendingPlanningStopwatch = System.Diagnostics.Stopwatch.StartNew();
            pendingCandidates.Clear();
            pendingNormalCandidates.Clear();
            bestCandidateRoute.Clear();
            bestCandidateWaypointDistances.Clear();

            if (TryBuildDirectGeometryRoute(pendingRequiredMask))
            {
                QueueCurrentGeometryCandidate(
                    pendingRequiredMask,
                    0f,
                    1f,
                    true,
                    false,
                    true);
            }

            return true;
        }

        // Spends a bounded amount of this frame predicting route alternatives.
        // A valid normal route commits immediately; recovery alternatives compare ETA.
        private void AdvancePendingRoutePlanning()
        {
            if (!hasPendingPlanning)
            {
                return;
            }

            pendingPlanFrames++;
            System.Diagnostics.Stopwatch frameBudget =
                System.Diagnostics.Stopwatch.StartNew();

            while (hasPendingPlanning &&
                   frameBudget.Elapsed.TotalMilliseconds <
                   Mathf.Max(0.1f, planningBudgetMillisecondsPerFrame))
            {
                if (pendingPrediction.IsRunning)
                {
                    int startSteps = pendingPrediction.StepsExecuted;
                    pendingPrediction.Advance(96);
                    pendingPhysicalSteps +=
                        pendingPrediction.StepsExecuted - startSteps;

                    if (pendingPrediction.IsRunning)
                    {
                        continue;
                    }

                    RouteGeometryCandidate completed =
                        pendingCandidates[pendingCandidateIndex - 1];

                    if (candidateRoute.IsValid)
                    {
                        bool severeTurn =
                            HasSevereTurnConstraint(candidateRoute);

                        if (!completed.IsRecovery && !severeTurn)
                        {
                            CommitPendingRoute(candidateRoute, completed);
                            return;
                        }

                        RememberBestPendingRoute(candidateRoute, completed);
                    }
                    else if (candidateRoute.FailureReason !=
                             ShipRouteFailureReason.SlowerThanBestArrival)
                    {
                        pendingLastFailureReason = candidateRoute.FailureReason;
                        pendingLastFailurePosition = candidateRoute.FailurePosition;
                        pendingLastFailureTimeSeconds =
                            candidateRoute.FailureTimeSeconds;
                    }
                }

                if (pendingCandidateIndex < pendingCandidates.Count)
                {
                    BeginNextPendingPrediction();
                    continue;
                }

                if (TryQueueNextNormalGeometryCandidate())
                {
                    continue;
                }

                if (!pendingRecoveryQueued)
                {
                    pendingRecoveryQueued = true;
                    QueueRecoveryCandidates();

                    if (pendingCandidateIndex < pendingCandidates.Count)
                    {
                        continue;
                    }
                }

                if (bestCandidateRoute.IsValid)
                {
                    RouteGeometryCandidate best = new RouteGeometryCandidate();
                    best.ExtraClearance = pendingBestExtraClearance;
                    best.TurnSpeedScale = pendingBestTurnSpeedScale;
                    best.WaypointDistances.AddRange(
                        bestCandidateWaypointDistances);
                    CommitPendingRoute(bestCandidateRoute, best);
                    return;
                }

                CompletePendingRouteFailure();
            }
        }

        // Starts the next already-generated geometry candidate with sparse guidance.
        private void BeginNextPendingPrediction()
        {
            RouteGeometryCandidate candidate =
                pendingCandidates[pendingCandidateIndex++];

            if (!KinematicRoutePlanner.BuildGuidanceCourse(
                candidate.Geometry,
                shipAgent.MovementProfile,
                shipAgent.SpeedMode,
                candidate.TurnSpeedScale,
                candidateRoute))
            {
                pendingLastFailureReason = candidateRoute.FailureReason;
                pendingLastFailurePosition = candidateRoute.FailurePosition;
                pendingLastFailureTimeSeconds =
                    candidateRoute.FailureTimeSeconds;
                return;
            }

            pendingPhysicalCandidates++;
            pendingPrediction.Begin(
                candidateRoute,
                shipAgent.CurrentMovementState,
                shipAgent.MovementProfile,
                shipAgent.SpeedMode,
                map,
                pendingRequiredMask,
                Time.fixedDeltaTime,
                shipAgent.LookAheadDistance,
                shipAgent.WaypointReachDistance,
                bestCandidateRoute.IsValid
                    ? bestCandidateRoute.EstimatedTimeSeconds
                    : float.PositiveInfinity);
        }

        // Adds A* geometry lazily only after a calm direct solution is unavailable
        // or physically unacceptable. Wider masks ask for gentler open-water bends.
        private bool TryQueueNextNormalGeometryCandidate()
        {
            int clearanceAttempts =
                Mathf.Max(0, kinematicClearanceAttempts) + 1;

            while (pendingClearanceAttempt < clearanceAttempts)
            {
                int attempt = pendingClearanceAttempt++;
                float extraClearance =
                    attempt * Mathf.Max(0f, kinematicClearanceStepWorld);

                NavigationProfile candidateProfile =
                    CreateNavigationProfile(extraClearance);

                bool duplicateMask = false;
                for (int i = 0; i < pendingNormalCandidates.Count; i++)
                {
                    if (!pendingNormalCandidates[i].IsDirect &&
                        ProfilesUseSameTraversalMask(
                            pendingNormalCandidates[i].PlanningMask.Profile,
                            candidateProfile))
                    {
                        duplicateMask = true;
                        break;
                    }
                }

                if (duplicateMask)
                {
                    continue;
                }

                NavigationTraversalMask planningMask =
                    pathService.GetTraversalMask(map, candidateProfile);

                if (!TryBuildFullGeometryRoute(planningMask, true))
                {
                    pendingGeometryUnavailable = true;

                    // Increasing clearance cannot open a path that the required
                    // legal-water mask already cannot reach.
                    if (attempt == 0)
                    {
                        pendingClearanceAttempt = clearanceAttempts;
                    }

                    continue;
                }

                QueueCurrentGeometryCandidate(
                    planningMask,
                    extraClearance,
                    1f,
                    false,
                    false,
                    true);
                return true;
            }

            return false;
        }

        // Recovery evaluates progressively tighter low-speed turns only after
        // ordinary courses failed or required extremely severe corner braking.
        private void QueueRecoveryCandidates()
        {
            int attempts = Mathf.Max(0, constrainedTurnAttempts) + 1;

            for (int candidateIndex = 0;
                 candidateIndex < pendingNormalCandidates.Count;
                 candidateIndex++)
            {
                RouteGeometryCandidate ordinary =
                    pendingNormalCandidates[candidateIndex];
                float previousScale = 1f;

                for (int attempt = 1; attempt < attempts; attempt++)
                {
                    float scale = Mathf.Max(
                        minimumConstrainedTurnSpeedScale,
                        1f - attempt *
                        Mathf.Max(0.01f, constrainedTurnSpeedScaleStep));

                    if (Mathf.Abs(scale - previousScale) <= 0.0001f)
                    {
                        continue;
                    }

                    previousScale = scale;
                    RouteGeometryCandidate recovery =
                        new RouteGeometryCandidate();
                    recovery.CopyGeometryFrom(
                        ordinary,
                        scale,
                        true);
                    pendingCandidates.Add(recovery);
                }
            }
        }

        private bool TryBuildDirectGeometryRoute(
            NavigationTraversalMask requiredMask)
        {
            fullWorldPath.Clear();
            fullWorldWaypointDistances.Clear();

            Vector2 start = GetShipPosition();
            fullWorldPath.Add(start);

            for (int i = 0; i < routeWaypoints.Count; i++)
            {
                Vector2 end = routeWaypoints[i].World;

                if (!RouteSegmentUtility.TryEvaluateSegment(
                    map,
                    requiredMask,
                    start,
                    end,
                    out float highestMoveCost) ||
                    highestMoveCost > 1.0001f)
                {
                    fullWorldPath.Clear();
                    fullWorldWaypointDistances.Clear();
                    return false;
                }

                if (Vector2.Distance(
                    fullWorldPath[fullWorldPath.Count - 1],
                    end) > 0.0005f)
                {
                    fullWorldPath.Add(end);
                }

                fullWorldWaypointDistances.Add(
                    CalculatePolylineDistance(fullWorldPath));
                start = end;
            }

            return fullWorldPath.Count >= 2;
        }

        private void QueueCurrentGeometryCandidate(
            NavigationTraversalMask planningMask,
            float extraClearance,
            float turnSpeedScale,
            bool isDirect,
            bool isRecovery,
            bool rememberNormal)
        {
            RouteGeometryCandidate candidate = new RouteGeometryCandidate();
            candidate.Geometry.AddRange(fullWorldPath);
            candidate.WaypointDistances.AddRange(fullWorldWaypointDistances);
            candidate.PlanningMask = planningMask;
            candidate.ExtraClearance = extraClearance;
            candidate.TurnSpeedScale = turnSpeedScale;
            candidate.IsDirect = isDirect;
            candidate.IsRecovery = isRecovery;
            pendingCandidates.Add(candidate);

            if (rememberNormal)
            {
                pendingNormalCandidates.Add(candidate);
            }
        }

        private bool HasSevereTurnConstraint(ShipRoute route)
        {
            if (route == null ||
                shipAgent.MovementProfile == null ||
                route.ControlPoints.Count < 3)
            {
                return false;
            }

            float threshold =
                shipAgent.MovementProfile.cruiseSpeedKnots *
                Mathf.Clamp01(severeTurnSpeedFraction);

            for (int i = 1; i < route.ControlPoints.Count - 1; i++)
            {
                float limit = route.ControlPoints[i].SpeedLimitKnots;
                if (limit > 0f && limit < threshold)
                {
                    return true;
                }
            }

            return false;
        }

        private void RememberBestPendingRoute(
            ShipRoute route,
            RouteGeometryCandidate candidate)
        {
            bool arrivesSooner =
                !bestCandidateRoute.IsValid ||
                route.EstimatedTimeSeconds <
                bestCandidateRoute.EstimatedTimeSeconds - 0.0001f;

            bool equallyFastButShorter =
                bestCandidateRoute.IsValid &&
                Mathf.Abs(
                    route.EstimatedTimeSeconds -
                    bestCandidateRoute.EstimatedTimeSeconds) <= 0.0001f &&
                route.TotalDistanceWorld <
                bestCandidateRoute.TotalDistanceWorld - 0.0001f;

            if (!arrivesSooner && !equallyFastButShorter)
            {
                return;
            }

            bestCandidateRoute.CopyFrom(route);
            bestCandidateWaypointDistances.Clear();
            bestCandidateWaypointDistances.AddRange(
                candidate.WaypointDistances);
            pendingBestExtraClearance = candidate.ExtraClearance;
            pendingBestTurnSpeedScale = candidate.TurnSpeedScale;
        }

        private void CommitPendingRoute(
            ShipRoute route,
            RouteGeometryCandidate candidate)
        {
            pendingPlanningStopwatch?.Stop();
            activeRoute.CopyFrom(route);
            activeRouteVisibleSampleIndex = 0;
            shipAgent.SetRoute(activeRoute);
            activeRouteLine?.Draw(
                activeRoute.PredictedSamples,
                shipAgent.MovementProfile.cruiseSpeedKnots,
                shipAgent.MovementProfile.flankSpeedKnots);

            activeDestination = routeWaypoints[routeWaypoints.Count - 1];
            acceptedWaypointDistances.Clear();
            acceptedWaypointDistances.AddRange(candidate.WaypointDistances);
            committedRouteWaypoints.Clear();
            committedRouteWaypoints.AddRange(routeWaypoints);
            RefreshWaypointMarkers();

            if (!pendingPlanSilent)
            {
                LogStatus(
                    $"Routing through {routeWaypoints.Count} waypoint(s). " +
                    $"SafetyRadius={pendingRequiredProfile.SafetyRadiusWorld:F2}. " +
                    $"PlanningClearance={candidate.ExtraClearance:F2}. " +
                    $"TightTurnSpeedScale={candidate.TurnSpeedScale:F2}. " +
                    $"ETA={activeRoute.EstimatedTimeSeconds:F2}s. " +
                    $"Distance={activeRoute.TotalDistanceWorld:F2}. " +
                    $"PlanWallTime={pendingPlanningStopwatch.Elapsed.TotalMilliseconds:F2}ms. " +
                    $"PlanFrames={pendingPlanFrames}. " +
                    $"DirectTested={pendingDirectTested}. " +
                    $"AStarLegs={pendingAStarLegs}. " +
                    $"PhysicalCandidates={pendingPhysicalCandidates}. " +
                    $"PhysicalSteps={pendingPhysicalSteps}. " +
                    $"MaskCacheHits={pathService.TraversalMaskCacheHits - pendingMaskHitsAtStart}. " +
                    $"MaskCacheMisses={pathService.TraversalMaskCacheMisses - pendingMaskMissesAtStart}. " +
                    $"RenderBuildTime={activeRouteLine?.LastBuildMilliseconds ?? 0d:F2}ms.");
            }

            hasPendingPlanning = false;
            pendingCandidates.Clear();
            pendingNormalCandidates.Clear();
        }

        private void CompletePendingRouteFailure()
        {
            pendingPlanningStopwatch?.Stop();

            if (!pendingPlanSilent)
            {
                string reason =
                    pendingLastFailureReason == ShipRouteFailureReason.None &&
                    pendingGeometryUnavailable
                        ? "No geometric path exists for the selected destination using the current ship clearance."
                        : DescribeRouteFailure(
                            pendingLastFailureReason,
                            pendingLastFailurePosition,
                            pendingLastFailureTimeSeconds);

                LogStatus(
                    reason +
                    $" PlanWallTime={pendingPlanningStopwatch.Elapsed.TotalMilliseconds:F2}ms. " +
                    $"PlanFrames={pendingPlanFrames}. " +
                    $"DirectTested={pendingDirectTested}. " +
                    $"AStarLegs={pendingAStarLegs}. " +
                    $"PhysicalCandidates={pendingPhysicalCandidates}. " +
                    $"PhysicalSteps={pendingPhysicalSteps}.");
            }

            routeWaypoints.Clear();
            routeWaypoints.AddRange(committedRouteWaypoints);
            RefreshWaypointMarkers();
            hasPendingPlanning = false;
            pendingCandidates.Clear();
            pendingNormalCandidates.Clear();
        }

        private void CancelPendingRoutePlanning()
        {
            if (hasPendingPlanning)
            {
                pendingPlanningStopwatch?.Stop();
            }

            hasPendingPlanning = false;
            pendingCandidates.Clear();
            pendingNormalCandidates.Clear();
        }

        // Turns physical route rejection into useful sandbox feedback.
        private string DescribeRouteFailure(
            ShipRouteFailureReason reason,
            Vector2 position,
            float timeSeconds)
        {
            switch (reason)
            {
                case ShipRouteFailureReason.PredictedTrackBlocked:
                    return
                        $"Predicted inertial track enters restricted water near " +
                        $"({position.x:F2}, {position.y:F2}) after " +
                        $"{timeSeconds:F2} simulated seconds.";

                case ShipRouteFailureReason.PredictionBudgetExceeded:
                    return
                        "The predicted ship did not reach a stopped arrival " +
                        "within the route simulation budget.";

                case ShipRouteFailureReason.NoGuidanceCourse:
                    return
                        "Pathfinding found cells, but no usable smoothed " +
                        "guidance course was produced.";

                case ShipRouteFailureReason.InvalidRequest:
                    return
                        "Route prediction received incomplete path or movement data.";

                default:
                    return
                        "No executable route found for this ship's handling characteristics.";
            }
        }

        // Stitches A* legs into one geometric candidate before the movement model evaluates it.
        private bool TryBuildFullGeometryRoute(
            NavigationTraversalMask planningMask,
            bool silent)
        {
            Vector2 shipPosition = GetShipPosition();
            Vector2Int startCell;

            if (!gridPresenter.TryWorldToCell(shipPosition, out startCell) &&
                !map.TryWorldToCell(shipPosition, out startCell))
            {
                startCell = FindClosestTraversableCell(
                    ClampCellToBounds(guaranteedSpawnCell),
                    planningMask);
            }

            startCell = FindClosestTraversableCell(startCell, planningMask);
            fullWorldPath.Clear();
            fullWorldWaypointDistances.Clear();

            Vector2Int legStartCell = startCell;
            Vector2 legStartWorld = shipPosition;

            for (int i = 0; i < routeWaypoints.Count; i++)
            {
                Waypoint waypoint = routeWaypoints[i];
                Vector2Int legGoalCell = FindClosestTraversableCell(
                    waypoint.Cell,
                    planningMask);

                pendingAStarLegs++;
                bool foundPath = pathService.TryFindPath(
                    legStartCell,
                    legGoalCell,
                    planningMask,
                    cellPath);
                if (!foundPath || cellPath.Count == 0)
                {
                    if (!silent)
                    {
                        LogStatus($"No path found to waypoint {i + 1}.");
                    }

                    return false;
                }

                PathRouteSmoother.BuildRoute(
                    map,
                    planningMask,
                    cellPath,
                    legStartWorld,
                    waypoint.World,
                    worldPath,
                    routeCandidates,
                    routeSampleFactor);

                if (worldPath.Count < 2)
                {
                    return false;
                }

                AppendWorldLeg(fullWorldPath, worldPath);
                fullWorldWaypointDistances.Add(
                    CalculatePolylineDistance(fullWorldPath));

                legStartCell = legGoalCell;
                legStartWorld = fullWorldPath[fullWorldPath.Count - 1];
            }

            return fullWorldPath.Count >= 2;
        }

        private static float CalculatePolylineDistance(List<Vector2> points)
        {
            float distance = 0f;

            for (int i = 1; i < points.Count; i++)
            {
                distance += Vector2.Distance(points[i - 1], points[i]);
            }

            return distance;
        }

        private static void AppendWorldLeg(List<Vector2> destination, List<Vector2> leg)
        {
            if (leg == null || leg.Count == 0)
            {
                return;
            }

            int start = 0;

            if (destination.Count > 0 &&
                Vector2.Distance(destination[destination.Count - 1], leg[0]) <= 0.0005f)
            {
                start = 1;
            }

            for (int i = start; i < leg.Count; i++)
            {
                destination.Add(leg[i]);
            }
        }

        private void HandleRouteCompletion()
        {
            if (!activeDestination.HasValue)
            {
                return;
            }

            if (shipAgent.HasPath())
            {
                return;
            }

            activeDestination = null;
            committedRouteWaypoints.Clear();
            acceptedWaypointDistances.Clear();
            activeRoute.Clear();
            activeRouteVisibleSampleIndex = 0;
            candidateRoute.Clear();
            activeRouteLine?.Clear();

            if (!hasPendingPlanning)
            {
                routeWaypoints.Clear();
            }

            UpdateQueuedRouteLine();
            RefreshWaypointMarkers();

            LogStatus("Route complete.");
        }

        // Intermediate waypoint markers are retired only in route order.
        // Removing reached points also keeps future appended orders from sending
        // the ship back through waypoints it already completed.
        private void RetirePassedWaypoints()
        {
            if (!shipAgent.HasPath() ||
                routeWaypoints.Count <= 1 ||
                acceptedWaypointDistances.Count != routeWaypoints.Count)
            {
                return;
            }

            bool removedAny = false;
            float reachedProgress =
                shipAgent.RouteProgressWorld + shipAgent.WaypointReachDistance;

            while (routeWaypoints.Count > 1 &&
                   acceptedWaypointDistances.Count > 1 &&
                   reachedProgress >= acceptedWaypointDistances[0])
            {
                routeWaypoints.RemoveAt(0);
                acceptedWaypointDistances.RemoveAt(0);
                removedAny = true;
            }

            if (removedAny)
            {
                committedRouteWaypoints.Clear();
                committedRouteWaypoints.AddRange(routeWaypoints);
                RefreshWaypointMarkers();
            }
        }

        // Finds the nearest currently traversable cell to a requested target.
        private Vector2Int FindClosestTraversableCell(Vector2Int desired)
        {
            return FindClosestTraversableCell(desired, displayedTraversalMask);
        }

        // Candidate corridors may request wider clearance without changing the visible legal-water mask.
        private Vector2Int FindClosestTraversableCell(
            Vector2Int desired,
            NavigationTraversalMask traversalMask)
        {
            if (!map.InBounds(desired.x, desired.y))
            {
                desired = new Vector2Int(map.Width / 2, map.Height / 2);
            }

            if (IsTraversableForSafety(desired, traversalMask))
            {
                return desired;
            }

            // BFS outward until we find the nearest cell the current safety mask allows.
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

                    if (IsTraversableForSafety(next, traversalMask))
                    {
                        return next;
                    }

                    queue.Enqueue(next);
                }
            }

            return new Vector2Int(1, 1);
        }

        // Checks map bounds plus the path service safety mask so UI and routing agree.
        private bool IsTraversableForSafety(Vector2Int cell)
        {
            return IsTraversableForSafety(cell, displayedTraversalMask);
        }

        private bool IsTraversableForSafety(
            Vector2Int cell,
            NavigationTraversalMask traversalMask)
        {
            if (!map.InBounds(cell.x, cell.y))
            {
                return false;
            }

            if (traversalMask == null)
            {
                return map.IsWalkable(cell.x, cell.y);
            }

            return !traversalMask.IsBlocked(cell);
        }

        // Draws the remaining active ship route into the active LineRenderer.
        private void UpdateActiveRouteLine()
        {
            if (activeRouteLine == null)
            {
                return;
            }

            if (!shipAgent.HasPath() && !activeDestination.HasValue)
            {
                activeRouteLine.Clear();
                return;
            }

            if (!activeRoute.IsValid ||
                activeRoute.PredictedSamples.Count < 2 ||
                shipAgent.MovementProfile == null)
            {
                activeRouteLine.Clear();
                return;
            }

            Vector2 shipPosition = GetShipPosition();
            AdvanceVisibleRouteSampleIndex(shipPosition);

            activeRouteLine.DrawRemaining(
                activeRoute.PredictedSamples,
                activeRouteVisibleSampleIndex,
                shipPosition,
                shipAgent.MovementProfile.cruiseSpeedKnots,
                shipAgent.MovementProfile.flankSpeedKnots);
        }

        // Advances locally along the predicted track so crossing looped route legs
        // cannot remove a future section merely because it passes near the ship.
        private void AdvanceVisibleRouteSampleIndex(Vector2 shipPosition)
        {
            int lastVisibleStart =
                Mathf.Max(0, activeRoute.PredictedSamples.Count - 2);

            activeRouteVisibleSampleIndex = Mathf.Clamp(
                activeRouteVisibleSampleIndex,
                0,
                lastVisibleStart);

            while (activeRouteVisibleSampleIndex < lastVisibleStart)
            {
                Vector2 current =
                    activeRoute.PredictedSamples[activeRouteVisibleSampleIndex].Position;

                Vector2 next =
                    activeRoute.PredictedSamples[activeRouteVisibleSampleIndex + 1].Position;

                float currentDistanceSqr = (shipPosition - current).sqrMagnitude;
                float nextDistanceSqr = (shipPosition - next).sqrMagnitude;

                if (nextDistanceSqr > currentDistanceSqr)
                {
                    break;
                }

                activeRouteVisibleSampleIndex++;
            }
        }

        // The committed predicted route is the only course line shown.
        // A raw ship-to-waypoint line would falsely imply the ship travels straight through obstacles.
        private void UpdateQueuedRouteLine()
        {
            if (queuedRouteLine != null)
            {
                queuedRouteLine.positionCount = 0;
            }
        }

        // Clears both debug line renderers.
        private void ClearLines()
        {
            if (activeRouteLine != null)
            {
                activeRouteLine.Clear();
            }

            if (queuedRouteLine != null)
            {
                queuedRouteLine.positionCount = 0;
            }
        }

        // Displays the immediate order, later pass-through orders, and the final destination distinctly.
        private void RefreshWaypointMarkers()
        {
            if (routeWaypoints.Count == 0)
            {
                DestroyAllWaypointMarkers();
                return;
            }

            bool showNext =
                nextWaypointMarkerSprite != null &&
                routeWaypoints.Count > 1;

            if (showNext)
            {
                if (nextWaypointMarkerRenderer == null)
                {
                    nextWaypointMarkerRenderer = CreateWaypointMarkerRenderer(
                        "NextWaypointMarker",
                        nextWaypointMarkerSprite,
                        Color.white);
                }

                PositionWaypointMarker(
                    nextWaypointMarkerRenderer,
                    routeWaypoints[0].World);
            }
            else
            {
                DestroyWaypointMarker(ref nextWaypointMarkerRenderer);
            }

            bool showLast =
                nextWaypointMarkerSprite != null;

            if (showLast)
            {
                if (lastWaypointMarkerRenderer == null)
                {
                    lastWaypointMarkerRenderer = CreateWaypointMarkerRenderer(
                        "LastWaypointMarker",
                        nextWaypointMarkerSprite,
                        lastWaypointMarkerColor);
                }

                lastWaypointMarkerRenderer.color = lastWaypointMarkerColor;
                PositionWaypointMarker(
                    lastWaypointMarkerRenderer,
                    routeWaypoints[routeWaypoints.Count - 1].World);
            }
            else
            {
                DestroyWaypointMarker(ref lastWaypointMarkerRenderer);
            }

            int firstOrdinaryWaypoint = showNext ? 1 : 0;
            int ordinaryWaypointEnd = showLast
                ? routeWaypoints.Count - 1
                : routeWaypoints.Count;

            int ordinaryMarkerCount =
                waypointMarkerSprite != null
                    ? Mathf.Max(0, ordinaryWaypointEnd - firstOrdinaryWaypoint)
                    : 0;

            if (waypointMarkerSprite != null)
            {
                while (waypointMarkerPool.Count < ordinaryMarkerCount)
                {
                    SpriteRenderer marker = CreateWaypointMarkerRenderer(
                        "WaypointMarker",
                        waypointMarkerSprite,
                        Color.white);

                    waypointMarkerPool.Add(marker);
                }
            }

            while (waypointMarkerPool.Count > ordinaryMarkerCount)
            {
                int lastIndex = waypointMarkerPool.Count - 1;
                SpriteRenderer marker = waypointMarkerPool[lastIndex];
                waypointMarkerPool.RemoveAt(lastIndex);
                Destroy(marker.gameObject);
            }

            for (int i = 0; i < ordinaryMarkerCount; i++)
            {
                SpriteRenderer marker = waypointMarkerPool[i];
                PositionWaypointMarker(
                    marker,
                    routeWaypoints[i + firstOrdinaryWaypoint].World);
            }
        }

        // Creates marker renderers at runtime so the inspector only needs the marker art assets.
        private SpriteRenderer CreateWaypointMarkerRenderer(
            string markerName,
            Sprite sprite,
            Color color)
        {
            GameObject markerObject = new GameObject(markerName);
            Transform markerTransform = markerObject.transform;
            markerTransform.SetParent(
                waypointMarkerRoot != null ? waypointMarkerRoot : transform,
                false);
            markerTransform.localScale = Vector3.one * waypointMarkerScale;

            SpriteRenderer marker = markerObject.AddComponent<SpriteRenderer>();
            marker.sprite = sprite;
            marker.color = color;
            marker.sortingOrder = waypointMarkerOrderInLayer;
            return marker;
        }

        // A removed route order should not leave hidden marker objects in the scene.
        private void DestroyAllWaypointMarkers()
        {
            DestroyWaypointMarker(ref nextWaypointMarkerRenderer);
            DestroyWaypointMarker(ref lastWaypointMarkerRenderer);

            for (int i = waypointMarkerPool.Count - 1; i >= 0; i--)
            {
                Destroy(waypointMarkerPool[i].gameObject);
                waypointMarkerPool.RemoveAt(i);
            }
        }

        private static void DestroyWaypointMarker(ref SpriteRenderer marker)
        {
            if (marker == null)
            {
                return;
            }

            Destroy(marker.gameObject);
            marker = null;
        }

        // Keeps marker position updates identical for the highlighted and ordinary sprites.
        private static void PositionWaypointMarker(SpriteRenderer marker, Vector2 position)
        {
            marker.transform.position = new Vector3(
                position.x,
                position.y,
                marker.transform.position.z);
        }

        // Reads the ship transform as a 2D world position.
        private Vector2 GetShipPosition()
        {
            Vector3 p = shipAgent.transform.position;
            return new Vector2(p.x, p.y);
        }

        // Creates a waypoint from a clicked cell while keeping the clicked point inside the hex.
        private Waypoint CreateWaypoint(Vector2Int cell, Vector2 clickedWorld)
        {
            Vector2 center = map.GetWorldCenter(cell.x, cell.y);
            Vector2 clamped = ClampPointToCell(clickedWorld, center);
            return new Waypoint(cell, clamped);
        }

        // Pulls a clicked point back toward the cell center so destinations stay inside their hex.
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

        // Clamps requested cells to the map definition bounds before generation/routing uses them.
        private Vector2Int ClampCellToBounds(Vector2Int cell)
        {
            int x = Mathf.Clamp(cell.x, 0, mapDefinition.Width - 1);
            int y = Mathf.Clamp(cell.y, 0, mapDefinition.Height - 1);
            return new Vector2Int(x, y);
        }

        // Small logging wrapper so sandbox spam can be turned off from the inspector.
        private void LogStatus(string message)
        {
            if (logStatus)
            {
                UnityEngine.Debug.Log($"[PathfindingSandbox] {message}");
            }
        }

        // Tiny value type for queued destinations: the map cell plus the exact world target.
        private struct Waypoint
        {
            public Vector2Int Cell;
            public Vector2 World;

            // Keeps waypoint construction readable at queue call sites.
            public Waypoint(Vector2Int cell, Vector2 world)
            {
                Cell = cell;
                World = world;
            }
        }

        // Generated geometry is cheap to retain while alternative exact
        // physical predictions are advanced over subsequent frames.
        private sealed class RouteGeometryCandidate
        {
            public readonly List<Vector2> Geometry = new List<Vector2>(512);
            public readonly List<float> WaypointDistances = new List<float>(32);

            public NavigationTraversalMask PlanningMask;
            public float ExtraClearance;
            public float TurnSpeedScale;
            public bool IsDirect;
            public bool IsRecovery;

            public void CopyGeometryFrom(
                RouteGeometryCandidate source,
                float turnSpeedScale,
                bool isRecovery)
            {
                Geometry.AddRange(source.Geometry);
                WaypointDistances.AddRange(source.WaypointDistances);
                PlanningMask = source.PlanningMask;
                ExtraClearance = source.ExtraClearance;
                TurnSpeedScale = turnSpeedScale;
                IsDirect = source.IsDirect;
                IsRecovery = isRecovery;
            }
        }
    }
}
