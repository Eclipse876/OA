using System.Collections.Generic;
using UnityEngine;

namespace OA.LegacyTechDemo
{
    public sealed class LegacyShipAgent : MonoBehaviour
    {
        private readonly List<Vector3> _pathWorldPoints = new List<Vector3>(256);

        private int _activeWaypointIndex = -1;

        public float Speed { get; set; } = 6f;
        public float WaypointReachDistance { get; set; } = 0.08f;

        public void WarpTo(Vector3 worldPosition)
        {
            transform.position = worldPosition;
            transform.rotation = Quaternion.identity;
            _pathWorldPoints.Clear();
            _activeWaypointIndex = -1;
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

        private void Update()
        {
            if (!HasPath())
            {
                return;
            }

            Vector3 target = _pathWorldPoints[_activeWaypointIndex];
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;

            float distance = toTarget.magnitude;
            if (distance <= WaypointReachDistance)
            {
                _activeWaypointIndex++;
                return;
            }

            Vector3 direction = toTarget / Mathf.Max(distance, 0.0001f);
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.position += direction * Mathf.Min(Speed * Time.deltaTime, distance);
        }
    }
}
