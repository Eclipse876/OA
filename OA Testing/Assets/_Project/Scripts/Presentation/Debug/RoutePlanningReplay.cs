using OA.Simulation.Movement;
using OA.Simulation.Navigation;
using OA.Simulation.Units;
using UnityEngine;

namespace OA.Presentation.Debug
{
    // Emitted with a final rejected order so its map, handling, forecast state,
    // and waypoint queue can be reconstructed without relying on screenshots.
    [System.Serializable]
    public sealed class RoutePlanningReplay
    {
        public int FormatVersion = 1;
        public bool GeneratedMap;
        public string MapDefinitionName;
        public int MapSeed;
        public int Width;
        public int Height;
        public float CellSize;
        public float ObstacleChance;
        public float RoughWaterChance;
        public int SmoothingPasses;
        public Vector2Int SpawnCell;
        public Vector2Int GoalCell;
        public Vector2 WorldOrigin;
        public Vector2 ColumnStep;
        public Vector2 RowStep;
        public Vector2 OddRowOffset;
        public MovementState OrderState;
        public MovementState ForecastState;
        public MovementSpeedMode SpeedMode;
        public float FixedDeltaTime;
        public float LookAheadDistance;
        public float ArrivalDistance;
        public string MovementProfileJson;
        public Vector2[] RequestedWaypoints;
        public Vector2[] ResolvedWaypoints;
        public int FailedLeg;
        public ShipRouteFailureReason FailureReason;
        public Vector2 FailurePosition;
        public float FailureTimeSeconds;
        public Vector2[] FailedGeometry;
        public bool CornerStops;
        public float TurnSpeedScale = 1f;
        public float PredictionLimitSeconds;
        public float StagnationSeconds;

        public ShipRoute CreateFailedCourse(MovementProfileDefinition profile)
        {
            var route = new ShipRoute();
            if (KinematicRoutePlanner.BuildGuidanceCourse(
                FailedGeometry, profile, SpeedMode, TurnSpeedScale, route) && CornerStops)
            {
                for (int i = 1; i < route.ControlPoints.Count; i++)
                {
                    ShipRoutePoint point = route.ControlPoints[i];
                    point.StopAtPoint = true;
                    route.ControlPoints[i] = point;
                }
            }
            return route;
        }

        public HexMapRuntime RecreateGeneratedMap()
        {
            if (!GeneratedMap)
                throw new System.InvalidOperationException(
                    "This replay uses a baked or custom map; load its original map asset.");

            var map = new HexMapRuntime(Width, Height, CellSize);
            new HexMapGenerator().Generate(map, MapSeed, ObstacleChance,
                RoughWaterChance, SmoothingPasses, SpawnCell, GoalCell);
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                    map.SetWorldCenter(x, y, WorldOrigin + ColumnStep * x +
                        RowStep * y + ((y & 1) == 0 ? Vector2.zero : OddRowOffset));
            map.MarkWorldCentersReady();
            return map;
        }
    }
}
