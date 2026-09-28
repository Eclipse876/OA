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
    }

    [Test]
    public void ScaledBakedMap_LongMovingOrdersAndLandClicks_CommitReachableRoutes()
    {
        var definition = AssetDatabase.LoadAssetAtPath<HexMapDefinition>(
            "Assets/_Project/Data/Scenarios/Navigation/HexMapDefinition_Main.asset");
        Assert.IsNotNull(definition, "The scaled production map must be present for this regression.");
        var map = HexMapRuntime.FromDefinition(definition);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map.SetWorldCenter(x, y, new Vector2((x + (y % 2) * 0.5f) * map.CellSize, y * map.CellSize * 0.8660254f));
        map.MarkWorldCentersReady();
        var service = Service(map, 126f / 31.034483f * 0.5f);
        var agent = Agent(map);
        var mask = service.GetTraversalMask(map, new NavigationProfile(agent.MovementProfile.NavigationSafetyRadiusWorld, ShipDraftClass.Shallow));
        Vector2Int spawn = default;
        bool found = false;
        for (int i = 0; i < mask.BlockedCells.Length; i++)
            if (!mask.BlockedCells[i]) { spawn = new Vector2Int(i % map.Width, i / map.Width); found = true; break; }
        Assert.IsTrue(found);
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
        }
    }

    private sealed class TestPresenter : INavigationGridPresenter
    {
        private readonly HexMapRuntime map;
        public TestPresenter(HexMapRuntime map) { this.map = map; }
        public void BuildOrRefresh(HexMapRuntime value, bool[] mask = null) { }
        public bool TryWorldToCell(Vector2 world, out Vector2Int cell) => map.TryWorldToCell(world, out cell);
    }
}
