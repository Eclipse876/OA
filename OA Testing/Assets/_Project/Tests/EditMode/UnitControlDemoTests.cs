using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OA.Presentation.Debug;
using OA.Presentation.Units;
using OA.Simulation.Movement;
using OA.Simulation.Navigation;
using OA.Simulation.Units;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

public sealed class UnitControlDemoTests
{
    private readonly List<Object> owned = new List<Object>();
    private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    [TearDown] public void Cleanup()
    {
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }

    private UnitArchetypeDefinition Definition(string name, bool carrier = false)
    {
        var texture = Own(new Texture2D(2, 4));
        var sprite = Own(Sprite.Create(texture, new Rect(0, 0, 2, 4), Vector2.one * 0.5f, 1f));
        var presentation = Own(ScriptableObject.CreateInstance<PresentationProfileDefinition>());
        presentation.worldSprite = sprite;
        var movement = Own(ScriptableObject.CreateInstance<MovementProfileDefinition>());
        movement.cruiseSpeedKnots = carrier ? 26f : 20f;
        movement.flankSpeedKnots = carrier ? 38f : 35f;
        movement.lengthMeters = carrier ? 342f : 126f;
        movement.draftClass = carrier ? ShipDraftClass.Deep : ShipDraftClass.Shallow;
        var definition = Own(ScriptableObject.CreateInstance<UnitArchetypeDefinition>());
        definition.displayName = name;
        definition.movementProfile = movement;
        definition.presentationProfile = presentation;
        return definition;
    }

    private UnitControlDemo Demo(out HexMapRuntime map, out UnitArchetypeDefinition garcia,
        out UnitArchetypeDefinition carrier)
    {
        map = new HexMapRuntime(32, 24, 32.22222f);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map.SetWorldCenter(x, y, new Vector2((x + (y & 1) * 0.5f) * map.CellSize,
                    y * map.CellSize * 0.8660254f));
        map.SetBlocked(12, 12, true);
        map.SetDepthClass(20, 4, WaterDepthClass.Shallow);
        map.MarkWorldCentersReady();
        garcia = Definition("Garcia");
        carrier = Definition("Enterprise", true);
        var root = Own(new GameObject("Unit control fixture"));
        root.SetActive(false);
        var demo = root.AddComponent<UnitControlDemo>();
        var cameraObject = Own(new GameObject("Unit control camera"));
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 400;
        camera.transform.position = new Vector3(400, 300, -10);
        var service = root.AddComponent<HexMapPathService>();
        demo.Initialize(map, new TestGrid(map), service, camera, new[] { garcia, carrier });
        return demo;
    }

    [Test]
    public void SelectionStartsEmpty_ReplacesAddsBoxesAndClears_WithoutIssuingMovement()
    {
        var demo = Demo(out var map, out var garcia, out var carrier);
        var a = demo.SpawnAt(garcia, map.GetWorldCenter(5, 8));
        var b = demo.SpawnAt(carrier, map.GetWorldCenter(8, 8));
        var c = demo.SpawnAt(garcia, map.GetWorldCenter(25, 18));
        Assert.AreEqual(0, demo.SelectedUnits.Count);
        Assert.AreEqual(0, demo.MoveSelected(map.GetWorldCenter(20, 8), false, false));
        Assert.IsFalse(a.Agent.HasPath());
        demo.SelectClick(a, false);
        demo.SelectClick(b, true);
        demo.SelectClick(a, true);
        Assert.AreEqual(2, demo.SelectedUnits.Count);
        demo.SelectClick(c, false);
        CollectionAssert.AreEqual(new[] { c }, demo.SelectedUnits);
        var camera = (Camera)typeof(UnitControlDemo).GetField("worldCamera", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(demo);
        Vector2 p = camera.WorldToScreenPoint(a.transform.position);
        Vector2 q = camera.WorldToScreenPoint(b.transform.position);
        Rect box = UnitControlDemo.SelectionRect(q + Vector2.one * 5f, p - Vector2.one * 5f);
        demo.SelectBox(box, false);
        CollectionAssert.AreEquivalent(new[] { a, b }, demo.SelectedUnits);
        demo.SelectClick(c, true);
        Assert.AreEqual(3, demo.SelectedUnits.Count);
        demo.SelectBox(new Rect(-1000, -1000, 1, 1), false);
        Assert.AreEqual(0, demo.SelectedUnits.Count);
        Assert.IsFalse(a.IsSelected);
        Assert.IsFalse(b.IsSelected);
        Assert.IsFalse(c.Agent.HasPath());
    }

    [Test]
    public void GroupOrdersUseDistinctSlots_OnlySelectedShipsMove_AndDeselectKeepsOrders()
    {
        var demo = Demo(out var map, out var garcia, out var carrier);
        var a = demo.SpawnAt(garcia, map.GetWorldCenter(4, 8));
        var b = demo.SpawnAt(carrier, map.GetWorldCenter(6, 8));
        var idle = demo.SpawnAt(garcia, map.GetWorldCenter(25, 18));
        demo.SelectClick(a, false);
        demo.SelectClick(b, true);
        Assert.AreEqual(2, demo.MoveSelected(map.GetWorldCenter(20, 8), false, false));
        Vector2 aDestination = a.Planner.CurrentDestinationWorld.Value;
        Vector2 bDestination = b.Planner.CurrentDestinationWorld.Value;
        Assert.GreaterOrEqual(Vector2.Distance(aDestination, bDestination), a.SeparationRadius + b.SeparationRadius);
        CompletePlans(demo);
        Assert.IsTrue(a.Agent.HasPath());
        Assert.IsTrue(b.Agent.HasPath());
        Assert.IsFalse(idle.Agent.HasPath());
        MovementState expected = a.Agent.PredictState(10, Time.fixedDeltaTime);
        demo.ClearSelection();
        Assert.AreEqual(0, demo.MoveSelected(map.GetWorldCenter(10, 10), false, false));
        for (int i = 0; i < 10; i++) FixedStep(a.Agent);
        Assert.AreEqual(expected.Position, a.Agent.CurrentMovementState.Position);
        Assert.AreEqual(0, a.GetComponentsInChildren<Collider>().Length);
        Assert.AreEqual(0, a.GetComponentsInChildren<Collider2D>().Length);
        Assert.AreEqual(0, b.GetComponentsInChildren<Rigidbody2D>().Length);
    }

    [Test]
    public void AppendedAndInsertedOrdersStayWithSelectedUnits_AndSpeedToggleIsSelective()
    {
        var demo = Demo(out var map, out var garcia, out var carrier);
        var a = demo.SpawnAt(garcia, map.GetWorldCenter(4, 8));
        var b = demo.SpawnAt(carrier, map.GetWorldCenter(6, 8));
        demo.SelectClick(a, false);
        demo.MoveSelected(map.GetWorldCenter(18, 8), false, false);
        demo.MoveSelected(map.GetWorldCenter(22, 8), true, false);
        demo.MoveSelected(map.GetWorldCenter(14, 8), true, true);
        var queue = (System.Collections.IList)typeof(PathfindingSandboxController).GetField("pendingRouteWaypoints",
            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(a.Planner);
        Assert.AreEqual(3, queue.Count);
        Vector2 first = (Vector2)queue[0].GetType().GetField("World").GetValue(queue[0]);
        Assert.Less(Vector2.Distance(first, map.GetWorldCenter(14, 8)), 1f);
        Assert.IsFalse(b.Planner.IsPlanning);
        demo.ToggleSelectedSpeed();
        Assert.AreEqual(MovementSpeedMode.Flank, a.Agent.SpeedMode);
        Assert.AreEqual(MovementSpeedMode.Cruise, b.Agent.SpeedMode);
    }

    [Test]
    public void DeselectingPendingShip_DoesNotCancelItsOrderOrRedrawHiddenRoute()
    {
        var demo = Demo(out var map, out var garcia, out _);
        var unit = demo.SpawnAt(garcia, map.GetWorldCenter(4, 8));
        demo.SelectClick(unit, false);
        demo.MoveSelected(map.GetWorldCenter(20, 8), false, false);
        demo.ClearSelection();
        CompletePlans(demo);
        Assert.IsTrue(unit.Agent.HasPath());
        foreach (MeshRenderer renderer in unit.GetComponentsInChildren<MeshRenderer>(true))
            Assert.IsFalse(renderer.gameObject.activeSelf, "A hidden route reappeared after handoff.");
        Object.DestroyImmediate(unit.gameObject);
        demo.SelectBox(new Rect(-1000, -1000, 1, 1), false);
        Assert.AreEqual(0, demo.Units.Count);
        Assert.AreEqual(0, demo.SelectedUnits.Count);
    }

    [Test]
    public void ShipsFinishAtSeparatedSlots_WithoutPhysicalBlocking()
    {
        var demo = Demo(out var map, out var garcia, out var carrier);
        var a = demo.SpawnAt(garcia, map.GetWorldCenter(4, 8));
        var b = demo.SpawnAt(carrier, map.GetWorldCenter(6, 8));
        demo.SelectClick(a, false);
        demo.SelectClick(b, true);
        Assert.AreEqual(2, demo.MoveSelected(map.GetWorldCenter(20, 8), false, false));
        CompletePlans(demo);
        for (int step = 0; step < 10000 && (a.Agent.HasPath() || b.Agent.HasPath()); step++)
        {
            FixedStep(a.Agent);
            FixedStep(b.Agent);
            demo.TickPlanning();
        }
        Assert.IsFalse(a.Agent.HasPath(), "Garcia did not settle.");
        Assert.IsFalse(b.Agent.HasPath(), "Carrier did not settle.");
        Assert.Greater(Vector2.Distance(a.transform.position, b.transform.position),
            garcia.movementProfile.HalfLengthWorldUnits + carrier.movementProfile.HalfLengthWorldUnits);
    }

    [TestCase(0f)]
    [TestCase(90f)]
    [TestCase(180f)]
    [TestCase(270f)]
    public void EnterpriseProfile_CanAcceptNearbyOrdersInEveryDirection(float heading)
    {
        var demo = Demo(out var map, out _, out _);
        var carrier = AssetDatabase.LoadAssetAtPath<UnitArchetypeDefinition>(
            "Assets/_Project/Data/Scenarios/UnitControl/EnterpriseArchetype.asset");
        var unit = demo.SpawnAt(carrier, map.GetWorldCenter(23, 16));
        demo.SelectClick(unit, false);
        Vector2 target = (Vector2)unit.transform.position + MovementMath.HeadingDegreesToVector(heading) * 145f;
        Assert.AreEqual(1, demo.MoveSelected(target, false, false));
        CompletePlans(demo);
        Assert.IsTrue(unit.Agent.HasPath(), $"Carrier rejected heading {heading}.");
    }

    [Test]
    public void PlacementRejectsLandOffMapAndDraft_CancelsAndSpawnsUnselected()
    {
        var demo = Demo(out var map, out var garcia, out var carrier);
        Assert.IsFalse(demo.CanPlace(garcia, map.GetWorldCenter(12, 12)));
        Assert.IsFalse(demo.CanPlace(carrier, map.GetWorldCenter(20, 4)));
        Assert.IsTrue(demo.CanPlace(garcia, map.GetWorldCenter(20, 4)));
        Assert.IsFalse(demo.CanPlace(garcia, Vector2.one * -10000f));
        demo.BeginPlacement(1);
        Assert.IsTrue(demo.IsPlacing);
        Assert.AreEqual(0, demo.MoveSelected(map.GetWorldCenter(20, 8), false, false));
        Assert.IsFalse(demo.TryPlace(map.GetWorldCenter(12, 12)));
        Assert.IsTrue(demo.IsPlacing);
        Assert.IsTrue(demo.TryPlace(map.GetWorldCenter(8, 8)));
        Assert.IsFalse(demo.IsPlacing);
        Assert.AreEqual(1, demo.Units.Count);
        Assert.AreEqual(0, demo.SelectedUnits.Count);
        Assert.IsFalse(demo.Units[0].Agent.HasPath());
        demo.BeginPlacement(0);
        demo.CancelPlacement();
        Assert.IsFalse(demo.IsPlacing);
        Assert.AreEqual(1, demo.Units.Count);
    }

    [Test]
    public void NewSceneContainsTwoShipsAndSmallMap_WithDistinctCarrierProfile()
    {
        const string scenePath = "Assets/Scenes/UnitControlTesting.unity";
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            var ships = new List<SelectableShip>();
            UnitControlDemo demo = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                ships.AddRange(root.GetComponentsInChildren<SelectableShip>(true));
                if (root.TryGetComponent(out UnitControlDemo found)) demo = found;
            }
            Assert.AreEqual(2, ships.Count);
            Assert.IsNotNull(demo);
            Tilemap terrain = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<Tilemap>() is Tilemap foundTerrain) terrain = foundTerrain;
            Assert.IsNotNull(terrain);
            Assert.AreEqual(64 * 48, CountTiles(terrain), "Saved scene is missing its terrain preview.");
            var definition = AssetDatabase.LoadAssetAtPath<HexMapDefinition>(
                "Assets/_Project/Data/Scenarios/UnitControl/HexMapDefinition_UnitControl.asset");
            Assert.AreEqual(64, definition.Width);
            Assert.AreEqual(48, definition.Height);
            MovementProfileDefinition garcia = ships.Find(s => s.Archetype.displayName.Contains("Garcia")).Archetype.movementProfile;
            MovementProfileDefinition carrier = ships.Find(s => s.Archetype.displayName.Contains("Enterprise")).Archetype.movementProfile;
            Assert.Greater(carrier.cruiseSpeedKnots, garcia.cruiseSpeedKnots);
            Assert.Greater(carrier.flankSpeedKnots, garcia.flankSpeedKnots);
            Assert.Less(carrier.accelerationKnotsPerSecond, garcia.accelerationKnotsPerSecond);
            Assert.Less(carrier.decelerationKnotsPerSecond, garcia.decelerationKnotsPerSecond);
            Assert.Less(ShipKinematicUtility.CalculateTurnRateDegreesPerSecond(carrier.cruiseSpeedKnots,
                    carrier, ShipAgilityPresets.Resolve(carrier)),
                ShipKinematicUtility.CalculateTurnRateDegreesPerSecond(garcia.cruiseSpeedKnots,
                    garcia, ShipAgilityPresets.Resolve(garcia)));
            foreach (var ship in ships)
            {
                Assert.IsNotNull(ship.Hull.sprite);
                Assert.IsNotNull(new SerializedObject(ship.Agent).FindProperty("archetype").objectReferenceValue,
                    "Scene-placed agent lost its assigned archetype.");
                Assert.IsFalse(ship.IsSelected);
                Assert.AreEqual(0, ship.GetComponentsInChildren<Collider2D>().Length);
            }
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private static void CompletePlans(UnitControlDemo demo)
    {
        for (int frame = 0; frame < 3000; frame++)
        {
            bool pending = false;
            foreach (var ship in demo.Units) { FixedStep(ship.Agent); pending |= ship.Planner.IsPlanning; }
            demo.TickPlanning();
            if (!pending) return;
        }
        Assert.Fail("Selective move planning did not complete.");
    }

    [UnityTest]
    public System.Collections.IEnumerator SceneRuntimeBootsUnselected_AndBothShipsAcceptSelectiveOrders()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/UnitControlTesting.unity", OpenSceneMode.Single);
        yield return new EnterPlayMode();
        var demo = Object.FindFirstObjectByType<UnitControlDemo>();
        Assert.IsNotNull(demo);
        Assert.AreEqual(64, demo.Map.Width);
        Assert.AreEqual(48, demo.Map.Height);
        Assert.AreEqual(2, demo.Units.Count);
        Assert.AreEqual(0, demo.SelectedUnits.Count);
        var terrain = Object.FindFirstObjectByType<TilemapHexGridPresenter>().GetComponentInChildren<Tilemap>();
        Assert.AreEqual(64 * 48, CountTiles(terrain), "Runtime map must paint every cell.");
        Assert.AreEqual(0, demo.MoveSelected(demo.Map.GetWorldCenter(15, 12), false, false));
        foreach (SelectableShip ship in demo.Units)
        {
            Assert.IsFalse(ship.Agent.HasPath());
            Assert.IsFalse(ship.IsSelected);
            demo.SelectClick(ship, true);
        }
        Assert.AreEqual(2, demo.MoveSelected(demo.Map.GetWorldCenter(15, 12), false, false));
        float deadline = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < deadline &&
            (demo.Units[0].Planner.IsPlanning || demo.Units[1].Planner.IsPlanning)) yield return null;
        foreach (SelectableShip ship in demo.Units)
        {
            Assert.IsFalse(ship.Planner.IsPlanning);
            Assert.IsTrue(ship.Agent.HasPath(), $"No accepted route for {ship.name}.");
        }
        demo.ClearSelection();
        yield return new WaitForFixedUpdate();
        Assert.AreEqual(0, demo.SelectedUnits.Count);
        Assert.IsTrue(demo.Units[0].Agent.HasPath());
        yield return new ExitPlayMode();
    }

    private static void FixedStep(ShipNavigationAgent agent) => typeof(ShipNavigationAgent)
        .GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(agent, null);

    private static int CountTiles(Tilemap tilemap)
    {
        int count = 0;
        foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
            if (tilemap.HasTile(cell)) count++;
        return count;
    }

    private sealed class TestGrid : INavigationGridPresenter
    {
        private readonly HexMapRuntime map;
        public TestGrid(HexMapRuntime value) { map = value; }
        public void BuildOrRefresh(HexMapRuntime value, bool[] mask = null) { }
        public bool TryWorldToCell(Vector2 world, out Vector2Int cell) => map.TryWorldToCell(world, out cell);
    }
}
