// ShipNavigationAgent.cs:
// The ship's "follow the pretty line" script. It does not find paths itself;
// it turns, accelerates, brakes, and tries to look like a boat instead of
// snapping between points like a debug cube.
using System.Collections.Generic;
using OA.Simulation.Movement;
using OA.Simulation.Units;
using UnityEngine;

namespace OA.Presentation.Units
{
    // Moves a unit along speed-limited world-space guidance using the same
    // inertial rules that the route planner predicts.
    public sealed class ShipNavigationAgent : MonoBehaviour
    {
        [SerializeField] private UnitArchetypeDefinition archetype;
        [SerializeField] private bool initializeOnStart = true;


        [Header("Speed Mode")]
            [SerializeField] private MovementSpeedMode defaultSpeedMode = MovementSpeedMode.Cruise;

        // Steering knobs tune how far along the planned course the ship aims.
        [Header("Steering (Forward Only)")]
            [SerializeField, Min(0.01f)] private float lookAheadDistance = 1.2f;
            [SerializeField, Min(0.001f)] private float waypointReachDistance = 0.18f;

        // Current route state. Progress is measured along the route so a sliding
        // ship does not turn around and circle a sample it has already passed.
        private readonly List<ShipRoutePoint> pathPoints = new List<ShipRoutePoint>(512);
        private readonly ShipMovementModel movementModel = new ShipMovementModel();

        private ShipRouteFollowState routeFollowState;
        private MovementState movementState;
        private OA.Simulation.Navigation.HexMapRuntime navigationMap;
        private MovementSpeedMode speedMode;
        private bool forceStop;
        private bool routeChangedThisFrame;

        public UnitRuntime Runtime { get; private set; }
        public bool IsInitialized => Runtime != null;
        public MovementSpeedMode SpeedMode => speedMode;
        public MovementState CurrentMovementState => movementState;
        public float RouteProgressWorld => routeFollowState.ProgressWorld;
        public float LookAheadDistance => lookAheadDistance;
        public float WaypointReachDistance => waypointReachDistance;
        public MovementProfileDefinition MovementProfile => Runtime != null ? Runtime.Archetype.movementProfile : null;

        public void SetNavigationMap(OA.Simulation.Navigation.HexMapRuntime map)
        {
            navigationMap = map;
        }

        // Optional self-start for scene-placed ships that already have an archetype assigned.
        private void Start()
        {
            if (initializeOnStart && archetype != null)
            {
                Initialize(archetype, GetPosition2D());
            }
        }

        // Creates runtime unit state and snaps the transform to the requested start position.
        public void Initialize(UnitArchetypeDefinition archetype, Vector2 startWorldPosition)
        {
            if (archetype == null)
            {
                UnityEngine.Debug.LogError("[ShipNavigationAgent] Cannot initialize without UnitArchetypeDefinition.");
                return;
            }

            this.archetype = archetype;
            Runtime = new UnitRuntime(archetype, startWorldPosition);
            speedMode = defaultSpeedMode;
            movementState = MovementState.Create(startWorldPosition, transform.eulerAngles.z);

            transform.position = new Vector3(startWorldPosition.x, startWorldPosition.y, transform.position.z);
            transform.rotation = Quaternion.Euler(0f, 0f, movementState.HeadingDegrees);

            ClearRouteState();
            SyncRuntimeValues();
        }

        // Teleports the ship and clears movement/path state.
        public void WarpTo(Vector2 worldPosition)
        {
            transform.position = new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
            movementState = MovementState.Create(worldPosition, transform.eulerAngles.z);

            ClearRouteState();

            if (Runtime != null)
            {
                SyncRuntimeValues();
            }
        }

        public void SetSpeedMode(MovementSpeedMode mode)
        {
            speedMode = mode;
        }

        public void ToggleSpeedMode()
        {
            speedMode = speedMode == MovementSpeedMode.Cruise
                                   ? MovementSpeedMode.Flank
                                   : MovementSpeedMode.Cruise;
        }

        // Explicit stop orders are the one player-driven case allowed to use maximum deceleration.
        public void Stop()
        {
            pathPoints.Clear();
            routeFollowState.Reset();
            forceStop = true;
            routeChangedThisFrame = true;
        }

        // Compatibility route setter for simple callers that do not provide speed-limit data.
        public void SetPath(IReadOnlyList<Vector2> points)
        {
            pathPoints.Clear();
            routeFollowState.Reset();
            forceStop = false;
            routeChangedThisFrame = true;

            if (points == null || points.Count < 2)
            {
                return;
            }

            float distance = 0f;

            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0)
                {
                    distance += Vector2.Distance(points[i - 1], points[i]);
                }

                RouteSegmentIntent intent = i == points.Count - 1
                    ? RouteSegmentIntent.Stop
                    : RouteSegmentIntent.Cruise;

                pathPoints.Add(new ShipRoutePoint(
                    points[i],
                    distance,
                    0f,
                    intent));
            }
        }

        // Accepts the speed-limited guidance course that produced the visible predicted line.
        public void SetRoute(ShipRoute route)
        {
            pathPoints.Clear();
            routeFollowState.Reset();
            forceStop = false;
            routeChangedThisFrame = true;

            if (route == null || !route.IsValid || route.ControlPoints.Count < 2)
            {
                return;
            }

            pathPoints.AddRange(route.ControlPoints);
        }

        // True while the ship is still executing a route rather than sitting at final arrival.
        public bool HasPath()
        {
            return pathPoints.Count >= 2 && !routeFollowState.IsComplete;
        }

        // Writes remaining guidance points for diagnostic callers; the displayed line uses prediction samples.
        public void GetRemainingPath(List<Vector2> output)
        {
            output.Clear();

            if (!HasPath())
            {
                return;
            }

            output.Add(movementState.Position);

            for (int i = 0; i < pathPoints.Count; i++)
            {
                if (pathPoints[i].DistanceFromStartWorld >= routeFollowState.ProgressWorld)
                {
                    output.Add(pathPoints[i].Position);
                }
            }
        }

        // FixedUpdate gives route prediction and live execution a matching, stable physics step.
        private void FixedUpdate()
        {
            if (Runtime == null || MovementProfile == null)
            {
                return;
            }

            MovementCommand command = BuildMovementCommand();

            movementState = movementModel.Step(
                movementState,
                command,
                MovementProfile,
                Time.fixedDeltaTime);

            ApplyMovementState();

            if (forceStop &&
                movementState.SpeedKnots <= 0.001f &&
                movementState.VelocityWorld.sqrMagnitude <= 0.000001f)
            {
                forceStop = false;
            }

            routeChangedThisFrame = false;
            SyncRuntimeValues();
        }

        // Builds a command from route progress, look-ahead guidance, and the active speed intent.
        private MovementCommand BuildMovementCommand()
        {
            if (forceStop)
            {
                return MovementCommand.Stop(movementState.Position);
            }

            if (pathPoints.Count < 2)
            {
                return MovementCommand.Hold(movementState.Position, routeChangedThisFrame);
            }

            return ShipRouteFollower.BuildCommand(
                movementState,
                pathPoints,
                ref routeFollowState,
                speedMode,
                MovementProfile,
                navigationMap,
                lookAheadDistance,
                waypointReachDistance,
                routeChangedThisFrame,
                out _);
        }

        private void ClearRouteState()
        {
            pathPoints.Clear();
            routeFollowState.Reset();
            forceStop = false;
            routeChangedThisFrame = false;
        }

        private void ApplyMovementState()
        {
            transform.position = new Vector3(
                movementState.Position.x,
                movementState.Position.y,
                transform.position.z);

            transform.rotation = Quaternion.Euler(0f, 0f, movementState.HeadingDegrees);
        }

        // Reads transform.position as a 2D point.
        private Vector2 GetPosition2D()
        {
            Vector3 pos = transform.position;
            return new Vector2(pos.x, pos.y);
        }

        // Mirrors transform/speed values back into UnitRuntime for simulation-side readers.
        private void SyncRuntimeValues()
        {
            if (Runtime == null)
            {
                return;
            }

            Runtime.SetPosition(movementState.Position);
            Runtime.SetHeadingDegrees(movementState.HeadingDegrees);
            Runtime.SetCurrentSpeed(movementState.SpeedKnots);
        }
    }
}
