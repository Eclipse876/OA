using System.Collections.Generic;
using OA.Simulation.Units;
using UnityEngine;

namespace OA.TechDemo
{
    public sealed class ShipAgentController : MonoBehaviour
    {
        private const float MinTurnRadius = 0.01f;

        private readonly List<Vector3> _pathWorldPoints = new List<Vector3>(256);

        private int _activeWaypointIndex = -1;
        private float _currentSpeed;

        public UnitRuntime Runtime { get; private set; }
        public MovementProfileDefinition MovementProfile => Runtime.Archetype.movementProfile;

        public void Initialize(UnitArchetypeDefinition archetype, Vector3 startWorldPosition)
        {
            Runtime = new UnitRuntime(archetype, new Vector2(startWorldPosition.x, startWorldPosition.z));
            transform.position = startWorldPosition;
            transform.rotation = Quaternion.identity;
            _currentSpeed = 0f;
            _activeWaypointIndex = -1;

            SyncRuntimeValues();
        }

        public void WarpTo(Vector3 worldPosition)
        {
            transform.position = worldPosition;
            _currentSpeed = 0f;
            _pathWorldPoints.Clear();
            _activeWaypointIndex = -1;
            SyncRuntimeValues();
        }

        public void SetPath(IReadOnlyList<Vector3> waypoints)
        {
            _pathWorldPoints.Clear();

            for (int i = 0; i < waypoints.Count; i++)
            {
                _pathWorldPoints.Add(waypoints[i]);
            }

            _activeWaypointIndex = _pathWorldPoints.Count > 0 ? 0 : -1;
        }

        public bool HasPath()
        {
            return _activeWaypointIndex >= 0 && _activeWaypointIndex < _pathWorldPoints.Count;
        }

        public Vector3 GetFinalDestinationOrCurrent()
        {
            if (_pathWorldPoints.Count == 0)
            {
                return transform.position;
            }

            return _pathWorldPoints[_pathWorldPoints.Count - 1];
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

            if (!HasPath())
            {
                ApplyCoastToStop(dt);
                SyncRuntimeValues();
                return;
            }

            Vector3 target = _pathWorldPoints[_activeWaypointIndex];
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            float distanceToWaypoint = toTarget.magnitude;

            if (distanceToWaypoint <= MovementProfile.stoppingDistance)
            {
                _activeWaypointIndex++;
                if (!HasPath())
                {
                    _currentSpeed = 0f;
                    SyncRuntimeValues();
                    return;
                }

                target = _pathWorldPoints[_activeWaypointIndex];
                toTarget = target - transform.position;
                toTarget.y = 0f;
                distanceToWaypoint = toTarget.magnitude;
            }

            if (distanceToWaypoint > 0.001f)
            {
                float desiredHeading = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                float currentHeading = transform.eulerAngles.y;
                float headingError = Mathf.DeltaAngle(currentHeading, desiredHeading);

                float maxTurnRate = Mathf.Max(0f, MovementProfile.turnRateDegreesPerSecond);
                if (MovementProfile.turningRadius > MinTurnRadius && _currentSpeed > 0.01f)
                {
                    float radiusLimitedTurnRate = Mathf.Rad2Deg * (_currentSpeed / MovementProfile.turningRadius);
                    maxTurnRate = Mathf.Min(maxTurnRate, radiusLimitedTurnRate);
                }

                float newHeading = Mathf.MoveTowardsAngle(currentHeading, desiredHeading, maxTurnRate * dt);
                transform.rotation = Quaternion.Euler(0f, newHeading, 0f);

                float finalDistance = CalculateRemainingPathDistance();
                float speedForBraking = MovementProfile.deceleration > 0.001f
                    ? Mathf.Sqrt(Mathf.Max(0f, 2f * MovementProfile.deceleration *
                        Mathf.Max(finalDistance - MovementProfile.stoppingDistance, 0f)))
                    : MovementProfile.maxSpeed;

                float turnTension = Mathf.InverseLerp(170f, 0f, Mathf.Abs(headingError));
                float speedForTurning = Mathf.Lerp(MovementProfile.maxSpeed * 0.2f, MovementProfile.maxSpeed, turnTension);
                float targetSpeed = Mathf.Min(MovementProfile.maxSpeed, speedForBraking, speedForTurning);

                float accelRate = _currentSpeed < targetSpeed ? MovementProfile.acceleration : MovementProfile.deceleration;
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, Mathf.Max(0f, accelRate) * dt);
                transform.position += transform.forward * (_currentSpeed * dt);
            }

            SyncRuntimeValues();
        }

        private void ApplyCoastToStop(float dt)
        {
            if (_currentSpeed <= 0f)
            {
                return;
            }

            _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, Mathf.Max(0f, MovementProfile.deceleration) * dt);
            transform.position += transform.forward * (_currentSpeed * dt);
        }

        private float CalculateRemainingPathDistance()
        {
            if (!HasPath())
            {
                return 0f;
            }

            float total = Vector3.Distance(transform.position, _pathWorldPoints[_activeWaypointIndex]);
            for (int i = _activeWaypointIndex; i < _pathWorldPoints.Count - 1; i++)
            {
                total += Vector3.Distance(_pathWorldPoints[i], _pathWorldPoints[i + 1]);
            }

            return total;
        }

        private void SyncRuntimeValues()
        {
            Runtime.SetPosition(new Vector2(transform.position.x, transform.position.z));
            Runtime.SetHeadingDegrees(transform.eulerAngles.y);
            Runtime.SetCurrentSpeed(_currentSpeed);
        }
    }
}
