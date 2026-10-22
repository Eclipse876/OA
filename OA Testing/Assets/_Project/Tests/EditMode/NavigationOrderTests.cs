using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OA.Presentation.Debug;
using OA.Presentation.Units;
using OA.Simulation.Movement;
using OA.Simulation.Navigation;
using OA.Simulation.Units;
using UnityEngine;
using UnityEditor;
using UnityEngine.TestTools;

public sealed class NavigationOrderTests
{
    private readonly List<Object> owned = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }

    private T Own<T>(T value) where T : Object { owned.Add(value); return value; }

    private static HexMapRuntime Map(int width = 24, int height = 16, float size = 32.22222f)
    {
        var map = new HexMapRuntime(width, height, size);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map.SetWorldCenter(x, y, new Vector2((x + (y % 2) * 0.5f) * size, y * size * 0.8660254f));
        map.MarkWorldCentersReady();
        return map;
    }

    private MovementProfileDefinition Profile()
    {
        var p = Own(ScriptableObject.CreateInstance<MovementProfileDefinition>());
        p.lengthMeters = 126f;
        p.cruiseSpeedKnots = 20f;
        p.flankSpeedKnots = 35f;
        p.agility = ShipAgilityClass.Low;
        return p;
    }

    private HexMapPathService Service(HexMapRuntime map, float radius = 0f, ShipDraftClass draft = ShipDraftClass.Shallow)
    {
        var go = Own(new GameObject("Navigation test service"));
        var service = go.AddComponent<HexMapPathService>();
        service.RebuildGraph(map, new NavigationProfile(radius, draft));
        return service;
    }

    [Test]
    public void HandoffTiming_RecoversAfterSlowPlan_AndSeparatesWaypointWorkloads()
    {
        var timing = new RoutePlanningTiming();
        timing.RecordSuccess(60000, 6, 1.64f);
        float slow = timing.EstimateLead(60000, 6, 200000f, 0.25f, 0.08f, 3f);
        Assert.Greater(slow, 1.7f);
        Assert.Less(timing.EstimateLead(60000, 1, 200000f, 0.25f, 0.08f, 3f), 0.5f,
            "A difficult queue must not slow unrelated direct orders.");

        for (int i = 0; i < 5; i++) timing.RecordSuccess(60000, 6, 0.28f);
        float recovered = timing.EstimateLead(60000, 6, 200000f, 0.25f, 0.08f, 3f);
        Assert.Less(recovered, 0.42f, "The learned reservation must decrease again.");
        Assert.GreaterOrEqual(recovered, 0.36f);
        Assert.Less(timing.EstimateLead(40000, 6, 200000f, 0.25f, 0.08f, 3f), recovered,
            "Shorter routes in the same bucket should not inherit a fixed delay.");

        timing.RecordSuccess(60000, 1, 0.4f, true);
        timing.RecordSuccess(60000, 1, 0.1f, false);
        Assert.Greater(timing.EstimateLead(60000, 1, 200000f, 0.25f, 0.08f, 3f, true), 0.47f,
            "A direct cruise must not erase the timing allowance for an obstructed route.");
        Assert.AreEqual(0.25f,
            timing.EstimateLead(60000, 1, 200000f, 0.25f, 0.08f, 3f, false), 0.001f);
    }

    [Test]
    public void WorldToCell_SequentialLookupsMatchFullNearestCenterSearch()
    {
        var map = Map(64, 32);
        var targets = new[]
        {
            map.GetWorldCenter(3, 4) + new Vector2(1.37f, -0.81f),
            map.GetWorldCenter(58, 25) + new Vector2(-2.19f, 0.63f),
            map.GetWorldCenter(12, 27) + new Vector2(0.77f, -1.11f),
            map.GetWorldCenter(50, 6) + new Vector2(-1.53f, 1.29f)
        };

        Vector2 previous = map.GetWorldCenter(2, 2) + new Vector2(0.31f, 0.47f);
        AssertSequentialLookupsMatchScan(map, previous, targets);

        var irregularMap = new HexMapRuntime(31, 21, 19.5f);
        for (int y = 0; y < irregularMap.Height; y++)
        {
            for (int x = 0; x < irregularMap.Width; x++)
            {
                Vector2 regular = new Vector2(
                    (x + (y % 2) * 0.5f) * irregularMap.CellSize,
                    y * irregularMap.CellSize * 0.8660254f);
                float phase = x * 17.13f + y * 31.71f;
                Vector2 jitter = new Vector2(
                    Mathf.Sin(phase) * irregularMap.CellSize * 0.12f,
                    Mathf.Cos(phase * 0.73f) * irregularMap.CellSize * 0.12f);
                irregularMap.SetWorldCenter(x, y, regular + jitter);
            }
        }
        irregularMap.MarkWorldCentersReady();

        Vector2[] irregularTargets =
        {
            irregularMap.GetWorldCenter(27, 17) + new Vector2(1.11f, -0.73f),
            irregularMap.GetWorldCenter(5, 18) + new Vector2(-0.93f, 0.57f),
            irregularMap.GetWorldCenter(24, 3) + new Vector2(0.49f, 1.37f)
        };
        AssertSequentialLookupsMatchScan(
            irregularMap,
            irregularMap.GetWorldCenter(2, 2) + new Vector2(0.27f, -0.39f),
            irregularTargets);
    }

    [Test]
    public void WorldToCell_EqualDistanceTieUsesLowestCellIndex()
    {
        var map = new HexMapRuntime(4, 4, 1f);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map.SetWorldCenter(x, y, new Vector2(100f + x, 100f + y));
        map.SetWorldCenter(0, 0, new Vector2(-1f, 0f));
        map.SetWorldCenter(1, 0, new Vector2(1f, 0f));
        map.MarkWorldCentersReady();

        Assert.IsTrue(map.TryWorldToCell(Vector2.zero, out Vector2Int cell));
        Assert.AreEqual(new Vector2Int(0, 0), cell);
    }

    [Test]
    public void AllowedSpeed_FromCurrentConstraintMatchesFullConstraintScan()
    {
        var profile = Profile();
        var route = new[]
        {
            new ShipRoutePoint(Vector2.zero, 0f, 20f, RouteSegmentIntent.Cruise),
            new ShipRoutePoint(Vector2.right * 20f, 20f, 9f, RouteSegmentIntent.Slow),
            new ShipRoutePoint(Vector2.right * 60f, 60f, 14f, RouteSegmentIntent.Slow),
            new ShipRoutePoint(Vector2.right * 110f, 110f, 7f, RouteSegmentIntent.Slow),
            new ShipRoutePoint(Vector2.right * 180f, 180f, 0f, RouteSegmentIntent.Stop)
        };
        float[] progressSamples = { 0f, 20f, 20.01f, 59f, 60f, 90f, 111f, 179f };

        foreach (float progress in progressSamples)
        {
            int guidanceIndex = 1;
            while (guidanceIndex < route.Length - 1 &&
                   route[guidanceIndex].DistanceFromStartWorld < progress + 0.0001f)
            {
                guidanceIndex++;
            }

            float expected = KinematicRoutePlanner.CalculateAllowedSpeedKnots(
                route,
                progress,
                MovementSpeedMode.Cruise,
                profile,
                out RouteSegmentIntent expectedIntent);
            float actual = KinematicRoutePlanner.CalculateAllowedSpeedKnots(
                route,
                progress,
                guidanceIndex,
                MovementSpeedMode.Cruise,
                profile,
                out RouteSegmentIntent actualIntent);

            Assert.AreEqual(expected, actual, 0.0001f, $"Speed mismatch at {progress}");
            Assert.AreEqual(expectedIntent, actualIntent, $"Intent mismatch at {progress}");
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Destination_UsesClosestReachableWater_ForLandOrDisconnectedWater(bool landClick)
    {
        var map = Map();
        for (int y = 0; y < map.Height; y++) map.SetBlocked(10, y, true);
        var service = Service(map);
        var mask = service.GetTraversalMask(map, new NavigationProfile(0f, ShipDraftClass.Shallow));
        Vector2 requested = map.GetWorldCenter(landClick ? 10 : 18, 8);
        Assert.IsTrue(mask.TryFindClosestReachableCell(new Vector2Int(2, 8), requested, out var destination));
        Assert.Less(destination.x, 10);
        float chosenDistance = (map.GetWorldCenter(destination.x, destination.y) - requested).sqrMagnitude;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < 10; x++)
                Assert.GreaterOrEqual((map.GetWorldCenter(x, y) - requested).sqrMagnitude + 0.01f, chosenDistance);
        Assert.IsTrue(service.TryFindPath(new Vector2Int(2, 8), destination, mask, new List<Vector2Int>()));
    }

    [Test]
    public void Destination_RespectsDraftAndClearance()
    {
        var map = Map();
        map.SetBlocked(10, 8, true);
        map.SetDepthClass(12, 8, WaterDepthClass.Shallow);
        var service = Service(map, 2f, ShipDraftClass.Deep);
        var mask = service.GetTraversalMask(map, new NavigationProfile(2f, ShipDraftClass.Deep));
        Assert.IsTrue(mask.TryFindClosestReachableCell(new Vector2Int(2, 8), map.GetWorldCenter(12, 8), out var target));
        Assert.IsFalse(mask.IsBlocked(target));
        Assert.IsTrue(service.TryFindPath(new Vector2Int(2, 8), target, mask, new List<Vector2Int>()));
    }

    [Test]
    public void Destination_OrdinaryWaterReturnsNearestReachableCell()
    {
        var map = Map();
        var service = Service(map);
        var mask = service.GetTraversalMask(
            map,
            new NavigationProfile(0f, ShipDraftClass.Shallow));
        Vector2 requested = map.GetWorldCenter(18, 11) + new Vector2(1.25f, -0.75f);

        Assert.IsTrue(mask.TryFindClosestReachableCell(
            new Vector2Int(2, 8),
            requested,
            out Vector2Int destination));
        Assert.AreEqual(FindClosestCellByScan(map, requested), destination);
    }

    [Test]
    public void Destination_RejectsInvalidStartAndStaleMask_WithoutInventingACell()
    {
        var map = Map();
        map.SetBlocked(2, 8, true);
        var service = Service(map);
        var mask = service.GetTraversalMask(map, new NavigationProfile(0f, ShipDraftClass.Shallow));
        Assert.IsFalse(mask.TryFindClosestReachableCell(new Vector2Int(2, 8), Vector2.zero, out _));
        map.SetBlocked(3, 8, true);
        Assert.IsFalse(mask.TryFindClosestReachableCell(new Vector2Int(4, 8), Vector2.zero, out _));
    }

    [Test]
    public void Prediction_AllowsLongJourneysBeyondOldStepCeiling()
    {
        var map = Map(64, 8);
        var p = Profile();
        p.simulationSecondsPerRealSecond = 1f;
        var start = map.GetWorldCenter(2, 4);
        var end = map.GetWorldCenter(60, 4);
        var service = Service(map);
        var mask = service.GetTraversalMask(map, new NavigationProfile(0f, ShipDraftClass.Shallow));
        var route = new ShipRoute();
        Assert.IsTrue(KinematicRoutePlanner.BuildGuidanceCourse(new[] { start, end }, p, MovementSpeedMode.Cruise, 1f, route));
        var prediction = new ShipRoutePrediction();
        prediction.Begin(route, MovementState.Create(start, 0f), p, MovementSpeedMode.Cruise,
            map, mask, 0.02f, 8f, 0.9f, maximumPredictionSeconds: 8000f, stagnationSeconds: 20f);
        while (prediction.IsRunning) prediction.Advance(4096);
        Assert.IsTrue(route.IsValid, route.FailureReason.ToString());
        Assert.Greater(prediction.StepsExecuted, 180000);
    }

    [Test]
    public void CornerStopRecovery_CompletesConfinedTurn()
    {
        var map = Map();
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map.SetBlocked(x, y, !(y == 5 && x >= 2 && x <= 12) && !(x == 12 && y >= 5 && y <= 12));
        var service = Service(map);
        var mask = service.GetTraversalMask(map, new NavigationProfile(0f, ShipDraftClass.Shallow));
        var p = Profile();
        var route = new ShipRoute();
        var geometry = new List<Vector2>();
        var path = new List<Vector2Int>();
        Assert.IsTrue(service.TryFindPath(new Vector2Int(2, 5), new Vector2Int(12, 12), mask, path));
        foreach (var cell in path) geometry.Add(map.GetWorldCenter(cell.x, cell.y));
        KinematicRoutePlanner.BuildGuidanceCourse(geometry, p, MovementSpeedMode.Cruise, 1f, route);
        for (int i = 1; i < route.ControlPoints.Count; i++)
        {
            var point = route.ControlPoints[i]; point.StopAtPoint = true; route.ControlPoints[i] = point;
        }
        var prediction = new ShipRoutePrediction();
        prediction.Begin(route, MovementState.Create(geometry[0], 180f), p, MovementSpeedMode.Cruise,
            map, mask, 0.02f, 8f, 0.9f, maximumPredictionSeconds: 2000f, stagnationSeconds: 20f);
        while (prediction.IsRunning) prediction.Advance(1024);
        Assert.IsTrue(route.IsValid, $"{route.FailureReason} at {route.FailurePosition}");
        Assert.Less(Vector2.Distance(route.PredictedSamples[route.PredictedSamples.Count - 1].Position,
            geometry[geometry.Count - 1]), 0.91f);
    }

    [Test]
    public void RouteSmoother_ProducesOnlyTraversableShortcutsAroundBarrier()
    {
        var map = Map(100, 40, 8f);
        for (int y = 4; y < map.Height; y++)
            map.SetBlocked(50, y, true);

        var service = Service(map);
        var mask = service.GetTraversalMask(
            map,
            new NavigationProfile(0f, ShipDraftClass.Shallow));
        Vector2Int startCell = new Vector2Int(5, 20);
        Vector2Int goalCell = new Vector2Int(95, 20);
        var cells = new List<Vector2Int>();
        Assert.IsTrue(service.TryFindPath(startCell, goalCell, mask, cells));

        Vector2 start = map.GetWorldCenter(startCell.x, startCell.y);
        Vector2 goal = map.GetWorldCenter(goalCell.x, goalCell.y);
        var route = new List<Vector2>();
        var scratch = new List<Vector2>();
        PathRouteSmoother.BuildRoute(
            map,
            mask,
            cells,
            start,
            goal,
            route,
            scratch);

        Assert.GreaterOrEqual(route.Count, 2);
        Assert.Less(route.Count, cells.Count);
        Assert.AreEqual(start, route[0]);
        Assert.AreEqual(goal, route[route.Count - 1]);
        for (int i = 1; i < route.Count; i++)
            Assert.IsTrue(RouteSegmentUtility.IsSegmentTraversable(
                map,
                mask,
                route[i - 1],
                route[i]),
                $"Illegal shortcut at segment {i - 1}.");
    }

    private ShipNavigationAgent Agent(HexMapRuntime map)
    {
        var p = Profile();
        var archetype = Own(ScriptableObject.CreateInstance<UnitArchetypeDefinition>());
        archetype.movementProfile = p;
        var agent = Own(new GameObject("Test ship")).AddComponent<ShipNavigationAgent>();
        agent.Initialize(archetype, map.GetWorldCenter(2, 5));
        agent.SetNavigationMap(map);
        agent.SetPath(new[] { map.GetWorldCenter(2, 5), map.GetWorldCenter(map.Width - 3, 5) });
        return agent;
    }

    [Test]
    public void AlreadyReachedDestination_IsAnArrivalRatherThanAnInvalidRoute()
    {
        var map = Map();
        var p = Profile();
        var position = map.GetWorldCenter(4, 5);
        var service = Service(map);
        var mask = service.GetTraversalMask(map, new NavigationProfile(0f, ShipDraftClass.Shallow));
        var route = new ShipRoute();
        Assert.IsTrue(KinematicRoutePlanner.BuildGuidanceCourse(new[] { position, position }, p, MovementSpeedMode.Cruise, 1f, route));
        var prediction = new ShipRoutePrediction();
        prediction.Begin(route, MovementState.Create(position, 0f), p, MovementSpeedMode.Cruise,
            map, mask, 0.02f, 8f, 0.9f);
        prediction.Advance(10);
        Assert.IsTrue(route.IsValid);
        Assert.IsTrue(prediction.IsComplete);
    }

    [Test]
    public void LastLeg_KeepsLocalSteeringInsteadOfCuttingAcrossToDestination()
    {
        var p = Profile();
        var state = MovementState.Create(new Vector2(5f, 3f), 90f);
        var follow = new ShipRouteFollowState { ProgressWorld = 13f, SegmentIndex = 1, ConstraintIndex = 2 };
        var route = new[] {
            new ShipRoutePoint(Vector2.zero, 0f, 20f, RouteSegmentIntent.Cruise),
            new ShipRoutePoint(new Vector2(10f, 0f), 10f, 10f, RouteSegmentIntent.Slow),
            new ShipRoutePoint(new Vector2(10f, 100f), 110f, 0f, RouteSegmentIntent.Stop)
        };
        var command = ShipRouteFollower.BuildCommand(state, route, ref follow, MovementSpeedMode.Cruise,
            p, null, null, 8f, 0.9f, false, out _);
        Assert.Less(command.SteeringTarget.y, 25f);
        Assert.AreEqual(10f, command.SteeringTarget.x);
    }

    [Test]
    public void CancelledScheduledRoute_DoesNotReplaceTheAcceptedCourse()
    {
        var map = Map();
        var agent = Agent(map);
        var expected = agent.PredictState(30, Time.fixedDeltaTime);
        var next = new ShipRoute { IsValid = true };
        next.ControlPoints.Add(new ShipRoutePoint(Vector2.zero, 0f, 10f, RouteSegmentIntent.Cruise));
        next.ControlPoints.Add(new ShipRoutePoint(Vector2.up * 100f, 100f, 0f, RouteSegmentIntent.Stop));
        Assert.IsTrue(agent.TryScheduleRoute(next, agent.SimulationStep + 10));
        agent.CancelScheduledRoute();
        for (int i = 0; i < 30; i++) Call(agent, "FixedUpdate");
        Assert.AreEqual(expected.Position, agent.CurrentMovementState.Position);
    }

    private static object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static T Get<T>(object target, string field) =>
        (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    private static Vector2Int FindClosestCellByScan(HexMapRuntime map, Vector2 position)
    {
        float bestDistanceSqr = float.PositiveInfinity;
        Vector2Int best = default;

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                float distanceSqr =
                    (map.GetWorldCenter(x, y) - position).sqrMagnitude;

                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    best = new Vector2Int(x, y);
                }
            }
        }

        return best;
    }

    private static void AssertSequentialLookupsMatchScan(
        HexMapRuntime map,
        Vector2 start,
        IReadOnlyList<Vector2> targets)
    {
        Vector2 previous = start;

        foreach (Vector2 target in targets)
        {
            int steps = Mathf.CeilToInt(
                Vector2.Distance(previous, target) / (map.CellSize * 0.35f));

            for (int step = 0; step <= steps; step++)
            {
                Vector2 query = Vector2.Lerp(previous, target, step / (float)steps);
                Vector2Int expected = FindClosestCellByScan(map, query);
                Assert.IsTrue(map.TryWorldToCell(query, out Vector2Int actual));
                Assert.AreEqual(expected, actual, $"Incorrect cell at {query}");
            }

            previous = target;
        }
    }

    [Test]
    public void MovingHandoff_ForecastMatchesLiveState_AndSwitchesOnReservedStep()
    {
        var map = Map();
        var agent = Agent(map);
        for (int i = 0; i < 40; i++) Call(agent, "FixedUpdate");
        var expected = agent.PredictState(25, Time.fixedDeltaTime);
        long handoff = agent.SimulationStep + 25;
        var next = new ShipRoute { IsValid = true };
        next.ControlPoints.Add(new ShipRoutePoint(expected.Position, 0f, 20f, RouteSegmentIntent.Cruise));
        next.ControlPoints.Add(new ShipRoutePoint(expected.Position + Vector2.up * 100f, 100f, 0f, RouteSegmentIntent.Stop));
        Assert.IsTrue(agent.TryScheduleRoute(next, handoff));
        for (int i = 0; i < 25; i++) Call(agent, "FixedUpdate");
        Assert.AreEqual(expected.Position, agent.CurrentMovementState.Position);
        Assert.AreEqual(expected.VelocityWorld, agent.CurrentMovementState.VelocityWorld);
        Assert.AreEqual(expected.HeadingDegrees, agent.CurrentMovementState.HeadingDegrees);
        Assert.IsTrue(agent.HasScheduledRoute);
        Call(agent, "FixedUpdate");
        Assert.IsFalse(agent.HasScheduledRoute);
        Assert.Less(Vector2.Distance(expected.Position, agent.CurrentMovementState.Position), 1f);
    }

    [Test]
    public void MovingOrder_LandClickAndRapidAppend_AreRetainedAndCommitted()
    {
        var map = Map(100, 20);
        map.SetBlocked(75, 8, true);
        var service = Service(map, 2.03f);
        var agent = Agent(map);
        for (int i = 0; i < 80; i++) Call(agent, "FixedUpdate");
        var go = Own(new GameObject("Order controller"));
        go.SetActive(false); // Configure explicitly without invoking scene bootstrap.
        var controller = go.AddComponent<PathfindingSandboxController>();
        Set(controller, "map", map);
        Set(controller, "pathService", service);
        Set(controller, "gridPresenter", new TestPresenter(map));
        Set(controller, "shipAgent", agent);
        Set(controller, "logStatus", false);
        Call(controller, "HandleDestinationSelection", new Vector2Int(75, 8), map.GetWorldCenter(75, 8), false, false);
        Call(controller, "HandleDestinationSelection", new Vector2Int(90, 8), map.GetWorldCenter(90, 8), true, false);
        for (int frame = 0; frame < 3000 && Get<bool>(controller, "hasPendingPlanning"); frame++)
        {
            Call(agent, "FixedUpdate");
            if (Get<bool>(controller, "pendingAwaitingHandoff") && !agent.HasScheduledRoute)
                Call(controller, "CompleteScheduledRouteHandoff");
            else
                Call(controller, "AdvancePendingRoutePlanning");
        }
        Assert.IsFalse(Get<bool>(controller, "hasPendingPlanning"), "Planning never completed");
        Assert.IsTrue(Get<ShipRoute>(controller, "activeRoute").IsValid, "Order was discarded");
        Assert.AreEqual(2, Get<System.Collections.IList>(controller, "committedRouteWaypoints").Count);
        Assert.LessOrEqual(Get<int>(controller, "pendingSnapshotRestarts"), 1,
            "Adaptive handoff reservation should prevent repeated full replans.");
    }

    [Test]
    public void ScaledGeneratedMap_LongMovingOrdersAndLandClicks_CommitReachableRoutes()
    {
        var definition = AssetDatabase.LoadAssetAtPath<HexMapDefinition>(
            "Assets/_Project/Data/Scenarios/Navigation/HexMapDefinition_Main.asset");
        Assert.IsNotNull(definition, "The scaled production map must be present for this regression.");
        var map = new HexMapRuntime(
            definition.Width,
            definition.Height,
            definition.CellSize);
        var spawn = new Vector2Int(2, 2);
        new HexMapGenerator().Generate(
            map,
            definition.Seed,
            definition.ObstacleChance,
            definition.RoughWaterChance,
            definition.SmoothingPasses,
            spawn,
            new Vector2Int(21, 31));
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map.SetWorldCenter(x, y, new Vector2((x + (y % 2) * 0.5f) * map.CellSize, y * map.CellSize * 0.8660254f));
        map.MarkWorldCentersReady();
        var service = Service(map, 126f / 31.034483f * 0.5f);
        var agent = Agent(map);
        var mask = service.GetTraversalMask(map, new NavigationProfile(agent.MovementProfile.NavigationSafetyRadiusWorld, ShipDraftClass.Shallow));
        Assert.IsFalse(mask.IsBlocked(spawn));
        agent.WarpTo(map.GetWorldCenter(spawn.x, spawn.y));
        var go = Own(new GameObject("Scaled order controller")); go.SetActive(false);
        var controller = go.AddComponent<PathfindingSandboxController>();
        Set(controller, "map", map); Set(controller, "pathService", service);
        Set(controller, "gridPresenter", new TestPresenter(map)); Set(controller, "shipAgent", agent);
        Set(controller, "logStatus", false);

        var targets = new List<Vector2Int> {
            new Vector2Int(990, 490), new Vector2Int(500, 250), new Vector2Int(30, 450),
            new Vector2Int(900, 50), new Vector2Int(300, 300), new Vector2Int(800, 400)
        };
        for (int i = 0; i < map.Blocked.Length; i++)
            if (map.Blocked[i]) { targets.Add(new Vector2Int(i % map.Width, i / map.Width)); break; }
        foreach (var target in targets)
        {
            for (int i = 0; i < 100; i++) Call(agent, "FixedUpdate");
            Call(controller, "HandleDestinationSelection", target, map.GetWorldCenter(target.x, target.y), false, false);
            for (int frame = 0; frame < 10000 && Get<bool>(controller, "hasPendingPlanning"); frame++)
            {
                Call(agent, "FixedUpdate");
                if (Get<bool>(controller, "pendingAwaitingHandoff") && !agent.HasScheduledRoute)
                    Call(controller, "CompleteScheduledRouteHandoff");
                else Call(controller, "AdvancePendingRoutePlanning");
                Call(controller, "HandleRouteCompletion");
            }
            Assert.IsFalse(Get<bool>(controller, "hasPendingPlanning"), $"Planning did not finish for {target}");
            var committed = Get<System.Collections.IList>(controller, "committedRouteWaypoints");
            Assert.AreEqual(1, committed.Count, $"No accepted destination for {target}; failure={Get<ShipRouteFailureReason>(controller, "pendingLastFailureReason")}");
            var acceptedCell = (Vector2Int)committed[0].GetType().GetField("Cell").GetValue(committed[0]);
            Assert.IsTrue(map.TryWorldToCell(agent.CurrentMovementState.Position, out var start));
            Assert.IsTrue(mask.TryFindClosestReachableCell(start, map.GetWorldCenter(target.x, target.y), out var expectedCell));
            Assert.AreEqual(expectedCell, acceptedCell, $"Old order survived instead of accepting {target}");
            Assert.IsTrue(Get<ShipRoute>(controller, "activeRoute").IsValid);
            Assert.LessOrEqual(Get<int>(controller, "pendingSnapshotRestarts"), 1,
                $"Route to {target} repeatedly missed its handoff reservation.");
            Assert.Greater(Get<double>(controller, "pendingPlanningCpuMilliseconds"), 0d);
            TestContext.WriteLine(
                $"{target}: frames={Get<int>(controller, "pendingPlanFrames")}, " +
                $"steps={Get<int>(controller, "pendingPhysicalSteps")}, " +
                $"restarts={Get<int>(controller, "pendingSnapshotRestarts")}, " +
                $"lead={Get<float>(controller, "pendingHandoffLead"):F3}s, " +
                $"cpu={Get<double>(controller, "pendingPlanningCpuMilliseconds"):F2}ms, " +
                $"predictionCpu={Get<double>(controller, "pendingPredictionCpuMilliseconds"):F2}ms, " +
                $"endpointCpu={Get<double>(controller, "pendingEndpointCpuMilliseconds"):F2}ms, " +
                $"aStarCpu={Get<double>(controller, "pendingAStarCpuMilliseconds"):F2}ms, " +
                $"smoothingCpu={Get<double>(controller, "pendingSmoothingCpuMilliseconds"):F2}ms, " +
                $"cells={Get<System.Collections.IList>(controller, "cellPath").Count}, " +
                $"geometry={Get<System.Collections.IList>(controller, "fullWorldPath").Count}, " +
                $"guidance={Get<ShipRoute>(controller, "activeRoute").ControlPoints.Count}");
        }
    }

    private PathfindingSandboxController Controller(HexMapRuntime map, HexMapPathService service,
        ShipNavigationAgent agent)
    {
        var go = Own(new GameObject("Route regression controller"));
        go.SetActive(false);
        var controller = go.AddComponent<PathfindingSandboxController>();
        Set(controller, "map", map);
        Set(controller, "pathService", service);
        Set(controller, "gridPresenter", new TestPresenter(map));
        Set(controller, "shipAgent", agent);
        Set(controller, "logStatus", false);
        return controller;
    }

    [Test]
    public void LocalRepair_ReplansOnlyFailedLeg_AndPreservesOtherWaypoints()
    {
        var map = Map(128, 64);
        map.SetBlocked(64, 32, true);
        var service = Service(map, 2.03f);
        var agent = Agent(map);
        agent.WarpTo(map.GetWorldCenter(2, 32));
        var controller = Controller(map, service, agent);
        Vector2Int[] targets = {
            new Vector2Int(30, 32), new Vector2Int(95, 32), new Vector2Int(110, 12),
            new Vector2Int(80, 52), new Vector2Int(35, 52), new Vector2Int(110, 40)
        };
        for (int i = 0; i < targets.Length; i++)
            Call(controller, "HandleDestinationSelection", targets[i],
                map.GetWorldCenter(targets[i].x, targets[i].y), i > 0, false);

        var mask = Get<NavigationTraversalMask>(controller, "pendingRequiredMask");
        bool[] originalBlocked = (bool[])mask.BlockedCells.Clone();
        Assert.IsTrue((bool)Call(controller, "TryBuildFullGeometryRoute", mask, true, null, -1));
        Call(controller, "QueueCurrentGeometryCandidate", mask, 0f, 1f, false, false, true);
        var candidates = Get<System.Collections.IList>(controller, "pendingCandidates");
        object original = candidates[candidates.Count - 1];
        Set(controller, "pendingLastFailureLeg", 1);
        Set(controller, "pendingLastFailurePosition", map.GetWorldCenter(63, 32));
        int legsBefore = Get<int>(controller, "pendingAStarLegs");
        Assert.IsTrue((bool)Call(controller, "TryQueueLocalRepair", original));
        Assert.AreEqual(1, Get<int>(controller, "pendingAStarLegs") - legsBefore,
            "Repairing one failed leg must not search all six legs again.");
        object repaired = candidates[Get<int>(controller, "pendingCandidateIndex")];
        var type = repaired.GetType();
        var points = (List<Vector2>)type.GetField("Geometry").GetValue(repaired);
        var ends = (List<int>)type.GetField("WaypointEndIndices").GetValue(repaired);
        var oldPoints = (List<Vector2>)type.GetField("Geometry").GetValue(original);
        var oldEnds = (List<int>)type.GetField("WaypointEndIndices").GetValue(original);
        Assert.AreEqual(targets.Length, ends.Count);
        for (int leg = 0; leg < ends.Count; leg++)
        {
            Assert.AreEqual(oldPoints[oldEnds[leg]], points[ends[leg]], "A requested waypoint moved.");
            if (leg == 1) continue;
            int first = leg == 0 ? 0 : ends[leg - 1];
            int oldFirst = leg == 0 ? 0 : oldEnds[leg - 1];
            CollectionAssert.AreEqual(oldPoints.GetRange(oldFirst, oldEnds[leg] - oldFirst + 1),
                points.GetRange(first, ends[leg] - first + 1), "An unaffected leg changed.");
        }
        for (int i = 1; i < points.Count; i++)
            Assert.IsTrue(RouteSegmentUtility.IsSegmentTraversable(map, mask, points[i - 1], points[i]));
        CollectionAssert.AreEqual(originalBlocked, mask.BlockedCells, "Repair changed the legal-water mask.");
    }

    [UnityTest]
    public System.Collections.IEnumerator FourCoastalWaypoints_CommitWithRealElapsedHandoffTiming()
    {
        return CoastalWaypointQueue(4);
    }

    [UnityTest]
    public System.Collections.IEnumerator FiveCoastalWaypoints_CommitWithRealElapsedHandoffTiming()
    {
        return CoastalWaypointQueue(5);
    }

    [UnityTest]
    public System.Collections.IEnumerator SixCoastalWaypoints_CommitWithRealElapsedHandoffTiming()
    {
        return CoastalWaypointQueue(6);
    }

    private System.Collections.IEnumerator CoastalWaypointQueue(int waypointCount)
    {
        var definition = AssetDatabase.LoadAssetAtPath<HexMapDefinition>(
            "Assets/_Project/Data/Scenarios/Navigation/HexMapDefinition_Main.asset");
        var recipe = new RoutePlanningReplay {
            GeneratedMap = true, MapSeed = definition.Seed,
            Width = definition.Width, Height = definition.Height, CellSize = definition.CellSize,
            ObstacleChance = definition.ObstacleChance, RoughWaterChance = definition.RoughWaterChance,
            SmoothingPasses = definition.SmoothingPasses, SpawnCell = new Vector2Int(2, 2),
            GoalCell = new Vector2Int(21, 31), ColumnStep = Vector2.right * definition.CellSize,
            RowStep = Vector2.up * definition.CellSize * 0.8660254f,
            OddRowOffset = Vector2.right * definition.CellSize * 0.5f
        };
        var map = recipe.RecreateGeneratedMap();
        var service = Service(map, 2.03f);
        var agent = Agent(map);
        agent.WarpTo(map.GetWorldCenter(2, 2));
        agent.SetPath(new[] { map.GetWorldCenter(2, 2), map.GetWorldCenter(2, 2) + Vector2.right * 160f });
        for (int i = 0; i < 40; i++) Call(agent, "FixedUpdate");
        Assert.Greater(agent.CurrentMovementState.SpeedKnots, 0f);
        var controller = Controller(map, service, agent);
        Set(controller, "mapReplayRecipe", recipe);
        // Exercises the coastline regions reported in the console. The old log
        // lacks the original queue/state, so this is a new deterministic fixture.
        Vector2Int[] targets = {
            new Vector2Int(120, 260), new Vector2Int(170, 275), new Vector2Int(240, 95),
            new Vector2Int(280, 70), new Vector2Int(320, 240), new Vector2Int(110, 200)
        };
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < waypointCount; i++)
            Call(controller, "HandleDestinationSelection", targets[i],
                map.GetWorldCenter(targets[i].x, targets[i].y), i > 0, false);
        string replayJson = (string)Call(controller, "CapturePendingRouteReplay");
        var replay = JsonUtility.FromJson<RoutePlanningReplay>(replayJson);
        Assert.AreEqual(waypointCount, replay.ResolvedWaypoints.Length);
        Assert.AreEqual(Get<MovementState>(controller, "pendingInitialMovementState").VelocityWorld,
            replay.ForecastState.VelocityWorld);
        double simulatedWallSeconds = 0d;
        while (Get<bool>(controller, "hasPendingPlanning") && elapsed.Elapsed.TotalSeconds < 10d)
        {
            // Unlike the older fast loop, elapsed wall time drives fixed steps.
            while (simulatedWallSeconds + Time.fixedDeltaTime <= elapsed.Elapsed.TotalSeconds)
            {
                Call(agent, "FixedUpdate");
                simulatedWallSeconds += Time.fixedDeltaTime;
            }
            if (Get<bool>(controller, "pendingAwaitingHandoff") && !agent.HasScheduledRoute)
                Call(controller, "CompleteScheduledRouteHandoff");
            else Call(controller, "AdvancePendingRoutePlanning");
            yield return null;
        }

        Assert.IsFalse(Get<bool>(controller, "hasPendingPlanning"), "Planning/handoff never finished.");
        Assert.IsTrue(Get<ShipRoute>(controller, "activeRoute").IsValid,
            $"Order failed: {controller.LastRouteReplayJson}");
        Assert.AreEqual(waypointCount, Get<System.Collections.IList>(controller, "committedRouteWaypoints").Count);
        double appliedMs = Get<System.Diagnostics.Stopwatch>(controller, "pendingPlanningStopwatch")
            .Elapsed.TotalMilliseconds;
        TestContext.WriteLine($"Coastal queue ({waypointCount} waypoints): input-to-applied={appliedMs:F2}ms; " +
            $"ready={Get<double>(controller, "pendingPlanReadyWallMilliseconds"):F2}ms; " +
            $"restarts={Get<int>(controller, "pendingSnapshotRestarts")}; " +
            $"localRepairs={Get<int>(controller, "pendingLocalRepairsTotal")}; " +
            $"cpu={Get<double>(controller, "pendingPlanningCpuMilliseconds"):F2}ms; " +
            $"frames={Get<int>(controller, "pendingPlanFrames")}; " +
            $"physicalSteps={Get<int>(controller, "pendingPhysicalSteps")}");
        Assert.Less(appliedMs, 4000d, "A learned reservation or retry cascade delayed the whole queue.");
        Assert.LessOrEqual(Get<int>(controller, "pendingSnapshotRestarts"), 1);
        Assert.LessOrEqual(Get<int>(controller, "pendingLocalRepairsTotal"), 4,
            "Local repair must remain bounded across a possible handoff restart.");
        var mask = Get<NavigationTraversalMask>(controller, "pendingRequiredMask");
        foreach (var sample in Get<ShipRoute>(controller, "activeRoute").PredictedSamples)
        {
            Assert.IsTrue(map.TryWorldToCell(sample.Position, out Vector2Int cell));
            Assert.IsFalse(mask.IsBlocked(cell), $"Accepted physical track entered restricted water at {cell}.");
        }

        // Replay reconstruction is verification work, not part of order latency.
        var rebuilt = replay.RecreateGeneratedMap();
        CollectionAssert.AreEqual(map.Blocked, rebuilt.Blocked);
        Assert.Less(Vector2.Distance(map.GetWorldCenter(320, 240), rebuilt.GetWorldCenter(320, 240)), 0.05f);
    }

    [Test]
    public void FailedPredictionReplay_ReproducesExactCollisionAfterJsonRoundTrip()
    {
        var map = Map(32, 16);
        map.SetBlocked(16, 8, true);
        var service = Service(map);
        var mask = service.GetTraversalMask(map, new NavigationProfile(0f, ShipDraftClass.Shallow));
        var profile = Profile();
        Vector2 start = map.GetWorldCenter(2, 8);
        Vector2 end = map.GetWorldCenter(28, 8);
        var replay = new RoutePlanningReplay {
            ForecastState = MovementState.Create(start, MovementMath.DirectionToHeadingDegrees(end - start)),
            SpeedMode = MovementSpeedMode.Cruise, FixedDeltaTime = 0.02f,
            LookAheadDistance = 8f, ArrivalDistance = 0.9f, FailedGeometry = new[] { start, end },
            MovementProfileJson = JsonUtility.ToJson(profile), PredictionLimitSeconds = 600f,
            StagnationSeconds = 20f
        };
        ShipRoute original = replay.CreateFailedCourse(profile);
        var prediction = new ShipRoutePrediction();
        prediction.Begin(original, replay.ForecastState, profile, replay.SpeedMode, map, mask,
            replay.FixedDeltaTime, replay.LookAheadDistance, replay.ArrivalDistance,
            maximumPredictionSeconds: replay.PredictionLimitSeconds, stagnationSeconds: replay.StagnationSeconds);
        while (prediction.IsRunning) prediction.Advance(4096);
        Assert.AreEqual(ShipRouteFailureReason.PredictedTrackBlocked, original.FailureReason);

        var restored = JsonUtility.FromJson<RoutePlanningReplay>(JsonUtility.ToJson(replay));
        var restoredProfile = Own(ScriptableObject.CreateInstance<MovementProfileDefinition>());
        JsonUtility.FromJsonOverwrite(restored.MovementProfileJson, restoredProfile);
        ShipRoute rerun = restored.CreateFailedCourse(restoredProfile);
        prediction.Begin(rerun, restored.ForecastState, restoredProfile, restored.SpeedMode, map, mask,
            restored.FixedDeltaTime, restored.LookAheadDistance, restored.ArrivalDistance,
            maximumPredictionSeconds: restored.PredictionLimitSeconds, stagnationSeconds: restored.StagnationSeconds);
        while (prediction.IsRunning) prediction.Advance(4096);
        Assert.AreEqual(original.FailureReason, rerun.FailureReason);
        Assert.AreEqual(original.FailurePosition, rerun.FailurePosition);
        Assert.AreEqual(original.FailureTimeSeconds, rerun.FailureTimeSeconds);
    }

    [Test]
    public void FinalRejectedOrder_IsAWarning_WithReplayAndFailedLeg()
    {
        var map = Map();
        var service = Service(map);
        var agent = Agent(map);
        var controller = Controller(map, service, agent);
        Call(controller, "HandleDestinationSelection", new Vector2Int(10, 8), map.GetWorldCenter(10, 8), false, false);
        Call(controller, "HandleDestinationSelection", new Vector2Int(18, 8), map.GetWorldCenter(18, 8), true, false);
        Set(controller, "pendingLastFailureLeg", 1);
        Set(controller, "pendingLastFailureReason", ShipRouteFailureReason.PredictedTrackBlocked);
        Set(controller, "pendingLastFailurePosition", new Vector2(4413.08f, 7348.31f));
        Set(controller, "logStatus", true);
        MovementState expectedContinuation = agent.PredictState(20, Time.fixedDeltaTime);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
            @"Route rejected\..*FailedLeg=2/2\..*HandoffRestarts=.*Replay="));
        Call(controller, "CompletePendingRouteFailure");
        var replay = JsonUtility.FromJson<RoutePlanningReplay>(controller.LastRouteReplayJson);
        Assert.AreEqual(1, replay.FailedLeg);
        Assert.AreEqual(2, replay.RequestedWaypoints.Length);
        Assert.AreEqual(new Vector2(4413.08f, 7348.31f), replay.FailurePosition);
        Assert.IsNotEmpty(replay.MovementProfileJson);
        Assert.AreEqual(expectedContinuation.Position, agent.PredictState(20, Time.fixedDeltaTime).Position,
            "Rejecting an order must preserve the already accepted course.");
    }

    private sealed class TestPresenter : INavigationGridPresenter
    {
        private readonly HexMapRuntime map;
        public TestPresenter(HexMapRuntime map) { this.map = map; }
        public void BuildOrRefresh(HexMapRuntime value, bool[] mask = null) { }
        public bool TryWorldToCell(Vector2 world, out Vector2Int cell) => map.TryWorldToCell(world, out cell);
    }
}
