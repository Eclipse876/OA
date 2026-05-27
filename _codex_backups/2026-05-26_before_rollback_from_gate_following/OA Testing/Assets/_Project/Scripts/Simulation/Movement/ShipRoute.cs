// ShipRoute.cs:
// Route data has two related but deliberately separate parts:
// guidance points tell the movement model what to chase, while predicted samples
// show the physical, inertial track the ship is expected to travel.
using System.Collections.Generic;
using UnityEngine;

namespace OA.Simulation.Movement
{
    // Planned route intent for one period of travel. Predicted physical speed drives route-line colors.
    public enum RouteSegmentIntent
    {
        Cruise = 0,
        Flank = 1,
        Slow = 2,
        Stop = 3
    }

    // Reason a candidate route could not be accepted by physical prediction.
    public enum ShipRouteFailureReason
    {
        None = 0,
        InvalidRequest = 1,
        NoGuidanceCourse = 2,
        PredictedTrackBlocked = 3,
        PredictionBudgetExceeded = 4,
        MissedRequiredWaypoint = 5
    }

    // A route-order constraint checked against the predicted and live physical track.
    // Intermediate orders are pass-through gates; only the final order requires a stop.
    public struct ShipRouteGate
    {
        public Vector2 Position;
        public float RadiusWorld;
        public bool MustStop;
        public int PredictedCrossingSampleIndex;
        public float GuidanceTargetDistanceWorld;

        public ShipRouteGate(
            Vector2 position,
            float radiusWorld,
            bool mustStop)
        {
            Position = position;
            RadiusWorld = Mathf.Max(0.001f, radiusWorld);
            MustStop = mustStop;
            PredictedCrossingSampleIndex = -1;
            GuidanceTargetDistanceWorld = -1f;
        }
    }

    // A speed-limited guidance point on the geometric route followed by the ship's steering logic.
    public struct ShipRoutePoint
    {
        public Vector2 Position;
        public float DistanceFromStartWorld;
        public float SpeedLimitKnots;
        public RouteSegmentIntent SegmentIntent;

        public ShipRoutePoint(
            Vector2 position,
            float distanceFromStartWorld,
            float speedLimitKnots,
            RouteSegmentIntent segmentIntent)
        {
            Position = position;
            DistanceFromStartWorld = Mathf.Max(0f, distanceFromStartWorld);
            SpeedLimitKnots = Mathf.Max(0f, speedLimitKnots);
            SegmentIntent = segmentIntent;
        }
    }

    // A physical prediction sample. Position and achieved speed make the displayed line honest.
    public struct ShipRouteSample
    {
        public Vector2 Position;
        public float SpeedKnots;
        public float SpeedLimitKnots;
        public RouteSegmentIntent SegmentIntent;

        public ShipRouteSample(
            Vector2 position,
            float speedKnots,
            float speedLimitKnots,
            RouteSegmentIntent segmentIntent)
        {
            Position = position;
            SpeedKnots = Mathf.Max(0f, speedKnots);
            SpeedLimitKnots = Mathf.Max(0f, speedLimitKnots);
            SegmentIntent = segmentIntent;
        }
    }

    // Complete planned route: private steering instructions plus public visual prediction.
    public sealed class ShipRoute
    {
        public readonly List<ShipRoutePoint> ControlPoints =
            new List<ShipRoutePoint>(256);

        public readonly List<ShipRouteSample> PredictedSamples =
            new List<ShipRouteSample>(1024);

        public readonly List<ShipRouteGate> Gates =
            new List<ShipRouteGate>(32);

        public float TotalDistanceWorld;
        public float EstimatedTimeSeconds;
        public float ManeuverScore;
        public int HeadingReversalCount;
        public float GateDeparturePenalty;
        public float MaximumGateBacktrackWorld;
        public bool IsValid;

        public ShipRouteFailureReason FailureReason;
        public Vector2 FailurePosition;
        public float FailureTimeSeconds;
        public int FailureGateIndex;

        public void Clear()
        {
            ControlPoints.Clear();
            PredictedSamples.Clear();
            Gates.Clear();
            TotalDistanceWorld = 0f;
            EstimatedTimeSeconds = 0f;
            ManeuverScore = float.PositiveInfinity;
            HeadingReversalCount = 0;
            GateDeparturePenalty = 0f;
            MaximumGateBacktrackWorld = 0f;
            IsValid = false;

            FailureReason = ShipRouteFailureReason.None;
            FailurePosition = default;
            FailureTimeSeconds = 0f;
            FailureGateIndex = -1;
        }

        // Records why a candidate route failed after clearing incomplete samples.
        public void Reject(
            ShipRouteFailureReason reason,
            Vector2 position,
            float timeSeconds,
            int gateIndex = -1)
        {
            Clear();

            FailureReason = reason;
            FailurePosition = position;
            FailureTimeSeconds = Mathf.Max(0f, timeSeconds);
            FailureGateIndex = gateIndex;
        }

        // Copies a successfully tested candidate into the committed active route.
        public void CopyFrom(ShipRoute other)
        {
            Clear();

            if (other == null)
            {
                return;
            }

            ControlPoints.AddRange(other.ControlPoints);
            PredictedSamples.AddRange(other.PredictedSamples);
            Gates.AddRange(other.Gates);
            TotalDistanceWorld = other.TotalDistanceWorld;
            EstimatedTimeSeconds = other.EstimatedTimeSeconds;
            ManeuverScore = other.ManeuverScore;
            HeadingReversalCount = other.HeadingReversalCount;
            GateDeparturePenalty = other.GateDeparturePenalty;
            MaximumGateBacktrackWorld = other.MaximumGateBacktrackWorld;
            IsValid = other.IsValid;

            FailureReason = other.FailureReason;
            FailurePosition = other.FailurePosition;
            FailureTimeSeconds = other.FailureTimeSeconds;
            FailureGateIndex = other.FailureGateIndex;
        }
    }
}
