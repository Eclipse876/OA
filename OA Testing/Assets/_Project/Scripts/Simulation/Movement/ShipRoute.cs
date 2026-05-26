using System.Collections.Generic;
using UnityEngine;

namespace OA.Simulation.Movement
{
    public enum RouteSegmentIntent
    {
        Cruise = 0,
        Flank = 1,
        Slow = 2,
        Stop = 3
    }

    public struct ShipRoutePoint
    {
        public Vector2 Position;
        public float SpeedLimitKnots;
        
        public ShipRoutePoint(Vector2 position, float speedLimitKnots = 0f)
        {
            Position = position;
            SpeedLimitKnots = Mathf.Max(0f, speedLimitKnots);
        }
    }

    public struct ShipRouteSample
    {
        public Vector2 Position;
        public float SpeedLimitKnots;
        public RouteSegmentIntent SegmentIntent;
        
        public ShipRouteSample(
            Vector2 position, 
            float speedLimitKnots,
            RouteSegmentIntent segmentIntent)
        {
            Position = position;
            SpeedLimitKnots = Mathf.Max(0f, speedLimitKnots);
            SegmentIntent = segmentIntent;
        }
    }

    public sealed class ShipRoute
    {
        public readonly List<ShipRoutePoint> ControlPoints = new List<ShipRoutePoint>(128);
        public readonly List<ShipRouteSample> PredictedSamples = new List<ShipRouteSample>(512);

        public float TotalDistanceWorld;
        public float EstimatedTimeSeconds;
        public bool IsValid;

        public void Clear()
        {
            ControlPoints.Clear();
            PredictedSamples.Clear();
            TotalDistanceWorld = 0f;
            EstimatedTimeSeconds = 0f;
            IsValid = false;
        }

        public void CopyFrom(ShipRoute other)
        {
            Clear();

            if (other == null)
            {
                return;
            }

            ControlPoints.AddRange(other.ControlPoints);
            PredictedSamples.AddRange(other.PredictedSamples);
            TotalDistanceWorld = other.TotalDistanceWorld;
            EstimatedTimeSeconds = other.EstimatedTimeSeconds;
            IsValid = other.IsValid;
        }
    }
}
