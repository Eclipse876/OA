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
    // Moves a unit along world-space waypoints using simple forward-only steering.
    public sealed class ShipNavigationAgent : MonoBehaviour
    {
        [SerializeField] private UnitArchetypeDefinition archetype;
        [SerializeField] private bool initializeOnStart = true;


        [Header("Speed Mode")]
            [SerializeField] private MovementSpeedMode defaultSpeedMode = MovementSpeedMode.Cruise;
        
        // Steering knobs tune how aggressively the ship turns and slows for waypoints. 
        [Header("Steering (Forward Only)")]
            [SerializeField] private float lookAheadDistance = 1.2f;
            [SerializeField] private float waypointReachDistance = 0.18f;

        // Current route state. activeWaypoints is -1 when the ship has nothing useful to do.
        private readonly List<ShipRoutePoint> pathPoints = new List<ShipRoutePoint>(256);
        private readonly ShipMovementModel movementModel = new ShipMovementModel();

        private int activeWaypoints = -1;
        private MovementState movementState;
        private MovementSpeedMode speedMode;
        private bool forceStop;
        private bool routeChangedThisFrame;

        public UnitRuntime Runtime { get; private set; }
        public bool IsInitialized => Runtime != null;
        public MovementSpeedMode SpeedMode => speedMode;
        public MovementProfileDefinition MovementProfile => Runtime != null ? Runtime.Archetype.movementProfile : null;

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
            
            activeWaypoints = -1;
            forceStop = false;
            routeChangedThisFrame = false;
            pathPoints.Clear();

            SyncRuntimeValues();
        }

        // Teleports the ship and clears movement/path state.
        public void WarpTo(Vector2 worldPosition)
        {
            transform.position = new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
            movementState = MovementState.Create(worldPosition, transform.eulerAngles.z);

            pathPoints.Clear();
            activeWaypoints = -1;
            forceStop = false;
            routeChangedThisFrame = false;

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

        public void Stop()
        {
            pathPoints.Clear();
            activeWaypoints = -1;
            forceStop = true;
            routeChangedThisFrame = true;
        }

        // Replaces the current route with caller-provided world-space points.
        public void SetPath(IReadOnlyList<Vector2> points)
        {
            pathPoints.Clear();

            if (points != null)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    pathPoints.Add(new ShipRoutePoint(points[i]));
                }
            }

            routeChangedThisFrame = true;
            forceStop = false;

            if (pathPoints.Count == 0)
            {
                activeWaypoints = -1;
                return;
            }

            activeWaypoints = 0;

            AdvanceWaypointsIfReached(movementState.Position);

            if (activeWaypoints >= pathPoints.Count)
            {
                activeWaypoints = -1;
            }
        }

        // True when there is still an active waypoint to chase.
        public bool HasPath()
        {
            return activeWaypoints >= 0 && activeWaypoints < pathPoints.Count;
        }

        // Writes the current position plus remaining waypoints for debug line rendering.
        public void GetRemainingPath(List<Vector2> output)
        {
            output.Clear();
            if (!HasPath())
            {
                return;
            }

            output.Add(movementState.Position);
            for (int i = activeWaypoints; i < pathPoints.Count; i++)
            {
                output.Add(pathPoints[i].Position);
            }
        }

        public void SetRoute(ShipRoute route)
        {
            pathPoints.Clear();

            if (route != null && route.IsValid)
            {
                for (int i = 0; i < route.ControlPoints.Count; i++)
                {
                    pathPoints.Add(route.ControlPoints[i]);
                }
            }

            routeChangedThisFrame = true;
            forceStop = false;

            if (pathPoints.Count == 0)
            {
                activeWaypoints = -1;
                return;
            }

            activeWaypoints = 0;
            AdvanceWaypointsIfReached(movementState.Position);

            if (activeWaypoints >= pathPoints.Count)
            {
                activeWaypoints = -1;
            }
        }

        // Main steering loop: turn toward the route, adjust speed, move forward, sync runtime state.
        private void Update()
        {
            if (Runtime == null || MovementProfile == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            AdvanceWaypointsIfReached(movementState.Position);

            MovementCommand command = BuildMovementCommand();
            movementState = movementModel.Step(movementState, command, MovementProfile, dt);

            ApplyMovementState();
            AdvanceWaypointsIfReached(movementState.Position);

            if (!HasPath() && movementState.SpeedKnots <= 0.001f)
            {
                forceStop = false;
            }

            routeChangedThisFrame = false;
            SyncRuntimeValues();
        }

        // Builds a command from the current route and speed mode for the movement model.
        private MovementCommand BuildMovementCommand()
        {
            if (forceStop)
            {
                return MovementCommand.Stop(movementState.Position);
            }
            if (!HasPath())
            {
                return MovementCommand.Hold(movementState.Position, routeChangedThisFrame);
            }

            Vector2 steeringTarget = GetSteeringTarget(movementState.Position);
            float remainingDistance = CalculateRemainingDistance(movementState.Position);
            float speedLimitKnots = pathPoints[activeWaypoints].SpeedLimitKnots;

            return MovementCommand.Move(
                steeringTarget,
                remainingDistance,
                speedMode,
                speedLimitKnots,
                routeChangedThisFrame);
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


        // Skips waypoints once the ship gets close enough to count as reached.
        private void AdvanceWaypointsIfReached(Vector2 position)
        {
            while (HasPath())
            {
                float dist = Vector2.Distance(position, pathPoints[activeWaypoints].Position);
                if (dist > waypointReachDistance)
                {
                    break;
                }

                activeWaypoints++;
            }

            if (activeWaypoints >= pathPoints.Count)
            {
                activeWaypoints = -1;
            }
        }

        // Picks a look-ahead target along the remaining path for smoother steering.
        private Vector2 GetSteeringTarget(Vector2 position)
        {
            if (!HasPath())
            {
                return position;
            }

            float remaining = Mathf.Max(0.01f, lookAheadDistance);
            Vector2 cursor = position;

            for (int i = activeWaypoints; i < pathPoints.Count; i++)
            {
                Vector2 next = pathPoints[i].Position;
                float segment = Vector2.Distance(cursor, next);

                if (segment >= remaining)
                {
                    float t = remaining / Mathf.Max(0.0001f, segment);
                    return Vector2.Lerp(cursor, next, t);
                }

                remaining -= segment;
                cursor = next;
            }

            return pathPoints[pathPoints.Count - 1].Position;
        }


        // Sums remaining route distance so braking can start before the final point.
        private float CalculateRemainingDistance(Vector2 fromPosition)
        {
            if (!HasPath())
            {
                return 0f;
            }

            float total = Vector2.Distance(fromPosition, pathPoints[activeWaypoints].Position);
            for (int i = activeWaypoints; i < pathPoints.Count - 1; i++)
            {
                total += Vector2.Distance(pathPoints[i].Position, pathPoints[i + 1].Position);
            }

            return total;
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
