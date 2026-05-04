using System.Collections.Generic;
using OA.Simulation.Units;
using UnityEngine;

namespace OA.Presentation.Units
{
    public sealed class ShipNavigationAgent : MonoBehaviour
    {
        [SerializeField] private UnitArchetypeDefinition archetype;
        [SerializeField] private bool initializeOnStart = true;

        [Header("Steering (Forward Only)")]
            [SerializeField] private float lookAheadDistance = 1.2f;
            [SerializeField] private float minForwardThrottleWhenTurning = 0.2f;
            [SerializeField] private float fullSpeedHeadingError = 25f;
            [SerializeField] private float hardTurnHeadingError = 100f;
            [SerializeField] private float waypointReachDistance = 0.18f;

        private readonly List<Vector2> pathPoints = new List<Vector2>(256);

        private int activeWaypoints = -1;
        private float currentSpeed;

        public UnitRuntime Runtime { get; private set; }
        public bool IsInitialized => Runtime != null;
        public MovementProfileDefinition MovementProfile => Runtime != null ? Runtime.Archetype.movementProfile : null;

        private void Start()
        {
            if (initializeOnStart && archetype != null)
            {
                Initialize(archetype, GetPosition2D());
            }
        }

        public void Initialize(UnitArchetypeDefinition archetype, Vector2 startWorldPosition)
        {
            if (archetype == null)
            {
                UnityEngine.Debug.LogError("[ShipNavigationAgent] Cannot initialize without UnitArchetypeDefinition.");
                return;
            }

            this.archetype = archetype;
            Runtime = new UnitRuntime(archetype, startWorldPosition);
            transform.position = new Vector3(startWorldPosition.x, startWorldPosition.y, transform.position.z);
            transform.rotation = Quaternion.identity;
            currentSpeed = 0f;
            activeWaypoints = -1;
            pathPoints.Clear();

            SyncRuntimeValues();
        }

        public void WarpTo(Vector2 worldPosition)
        {
            transform.position = new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
            currentSpeed = 0f;
            pathPoints.Clear();
            activeWaypoints = -1;

            if (Runtime != null)
            {
                SyncRuntimeValues();
            }
        }

        public void SetPath(IReadOnlyList<Vector2> points)
        {
            pathPoints.Clear();

            if (points != null)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    pathPoints.Add(points[i]);
                }
            }

            if (pathPoints.Count == 0)
            {
                activeWaypoints = -1;
                return;
            }

            activeWaypoints = 0;

            AdvanceWaypointsIfReached(GetPosition2D());

            if (activeWaypoints >= pathPoints.Count)
            {
                activeWaypoints = -1;
            }
        }

        public bool HasPath()
        {
            return activeWaypoints >= 0 && activeWaypoints < pathPoints.Count;
        }

        public void GetRemainingPath(List<Vector2> output)
        {
            output.Clear();
            if (!HasPath())
            {
                return;
            }

            output.Add(GetPosition2D());
            for (int i = activeWaypoints; i < pathPoints.Count; i++)
            {
                output.Add(pathPoints[i]);
            }
        }

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

            Vector2 position = GetPosition2D();


            if (HasPath())
            {

                AdvanceWaypointsIfReached(position);

                if (HasPath())
                {
                    Vector2 steeringTarget = GetSteeringTarget(position);
                    Vector2 toTarget = steeringTarget - position;

                    if (toTarget.sqrMagnitude > 0.00001f)
                    {
                        float desiredHeading = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
                        float currentHeading = transform.eulerAngles.z;

                        float maxTurnRate = GetEffectiveTurnRate();
                        float newHeading = Mathf.MoveTowardsAngle(currentHeading, desiredHeading, maxTurnRate * dt);
                        transform.rotation = Quaternion.Euler(0f, 0f, newHeading);

                        float headingError = Mathf.Abs(Mathf.DeltaAngle(newHeading, desiredHeading));
                        float remainingDistance = CalculateRemainingDistance(position);

                        float brakingSpeed = MovementProfile.deceleration > 0.001f
                            ? Mathf.Sqrt(Mathf.Max(0f, 2f * MovementProfile.deceleration *
                            Mathf.Max(0f, remainingDistance - MovementProfile.stoppingDistance)))
                            : MovementProfile.maxSpeed;

                        float cruiseTarget = Mathf.Min(MovementProfile.maxSpeed, brakingSpeed);

                        float align01 = Mathf.Clamp01(1f - (headingError / Mathf.Max(1f, fullSpeedHeadingError)));
                        align01 *= align01;

                        float minTurnSpeed = cruiseTarget * minForwardThrottleWhenTurning;
                        float targetSpeed = Mathf.Max(minTurnSpeed, cruiseTarget * align01);

                        if (headingError >= hardTurnHeadingError)
                        {
                            targetSpeed = Mathf.Min(targetSpeed, cruiseTarget * 0.45f);
                        }

                        targetSpeed = Mathf.Clamp(targetSpeed, 0f, MovementProfile.maxSpeed);

                        float accel = currentSpeed < targetSpeed ? MovementProfile.acceleration
                                                                 : MovementProfile.deceleration;

                        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, Mathf.Max(0f, accel) * dt);

                    }
                }
            }

            else if (currentSpeed > 0f)
            {
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, Mathf.Max(0f, MovementProfile.deceleration) * dt);
            }

            Vector2 forward = GetForward2D();
            position += forward * (currentSpeed * dt);

            transform.position = new Vector3(position.x, position.y, transform.position.z);
            
            AdvanceWaypointsIfReached(position);

            if(!HasPath() && currentSpeed <= 0.0001f)
            {
                currentSpeed = 0f;
            }

            SyncRuntimeValues();
        }

        private Vector2 GetPosition2D()
        {
            Vector3 pos = transform.position;
            return new Vector2(pos.x, pos.y);
        }

        private Vector2 GetForward2D()
        {
            Vector3 r = transform.right;
            Vector2 fwd = new Vector2(r.x, r.y);
            if (fwd.sqrMagnitude <= 0.00001f)
            {
                return Vector2.right;
            }

            return fwd.normalized;
        }

        private void AdvanceWaypointsIfReached(Vector2 position)
        {
            while (HasPath())
            {
                float dist = Vector2.Distance(position, pathPoints[activeWaypoints]);
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
                Vector2 next = pathPoints[i];
                float segment = Vector2.Distance(cursor, next);

                if (segment >= remaining)
                {
                    float t = remaining / Mathf.Max(0.0001f, segment);
                    return Vector2.Lerp(cursor, next, t);
                }

                remaining -= segment;
                cursor = next;
            }

            return pathPoints[pathPoints.Count - 1];
        }

        private float GetEffectiveTurnRate()
        {
            float turnRate = Mathf.Max(0f, MovementProfile.turnRateDegreesPerSecond);

            if (MovementProfile.turningRadius > 0.001f && currentSpeed > 0.01f)
            {
                float radiusLimitedTurnRate = Mathf.Rad2Deg * (currentSpeed / MovementProfile.turningRadius);
                turnRate = Mathf.Min(turnRate, radiusLimitedTurnRate);
            }

            return turnRate;
        }

        private float CalculateRemainingDistance(Vector2 fromPosition)
        {
            if (!HasPath())
            {
                return 0f;
            }

            float total = Vector2.Distance(fromPosition, pathPoints[activeWaypoints]);
            for (int i = activeWaypoints; i < pathPoints.Count - 1; i++)
            {
                total += Vector2.Distance(pathPoints[i], pathPoints[i + 1]);
            }

            return total;
        }

        private void SyncRuntimeValues()
        {
            if (Runtime == null)
            {
                return;
            }

            Vector2 position = GetPosition2D();
            Runtime.SetPosition(position);
            Runtime.SetHeadingDegrees(transform.eulerAngles.z);
            Runtime.SetCurrentSpeed(currentSpeed);
        }
    }
}
