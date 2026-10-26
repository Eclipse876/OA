using System.Collections.Generic;
using NUnit.Framework;
using OA.Presentation.Units;
using OA.Simulation.Units;
using UnityEditor;
using UnityEngine;

public sealed class ShipPickingTests
{
    private readonly List<Object> owned = new List<Object>();
    private T Own<T>(T value) where T : Object { owned.Add(value); return value; }

    [TearDown]
    public void Cleanup()
    {
        for (int i = owned.Count - 1; i >= 0; i--)
            if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear();
    }

    [TestCase(0f)]
    [TestCase(37f)]
    [TestCase(123f)]
    public void CloseZoom_VisibleHullAwayFromCenterlineIsSelectable(float heading)
    {
        SelectableShip ship = Ship(heading, out Camera camera);
        Vector2 pointer = camera.WorldToScreenPoint(ship.Hull.transform.TransformPoint(new Vector2(-0.8f, -1f)));
        Vector2 centerline = camera.WorldToScreenPoint(ship.Hull.transform.TransformPoint(new Vector2(0f, -1f)));
        Assert.Greater(Vector2.Distance(pointer, centerline), 12f,
            "The fixture must exercise the visible beam beyond the old 12-pixel centerline strip.");
        Assert.AreEqual(0f, ship.ScreenPickDistance(camera, pointer), 0.001f);
    }

    [Test]
    public void TightMesh_DoesNotSelectTransparentCornerInsideSpriteBounds()
    {
        SelectableShip ship = Ship(37f, out Camera camera);
        // The carrier's source texture has wide transparent corners. This
        // position is inside its texture rectangle, away from its tight mesh.
        Vector2 pointer = camera.WorldToScreenPoint(ship.Hull.transform.TransformPoint(new Vector2(4f, 4f)));
        Assert.Greater(ship.ScreenPickDistance(camera, pointer), 12f);
    }

    [Test]
    public void FarZoom_RetainsMinimumScreenPixelToleranceAroundTinyHull()
    {
        SelectableShip ship = Ship(73f, out Camera camera);
        camera.orthographicSize = 10000f;
        Vector2 center = camera.WorldToScreenPoint(ship.Hull.transform.TransformPoint(new Vector2(0f, -1f)));
        Assert.LessOrEqual(ship.ScreenPickDistance(camera, center + Vector2.right * 10f), 12f);
        Assert.Greater(ship.ScreenPickDistance(camera, center + Vector2.right * 20f), 12f);
    }

    private SelectableShip Ship(float heading, out Camera camera)
    {
        var definition = AssetDatabase.LoadAssetAtPath<UnitArchetypeDefinition>(
            "Assets/_Project/Data/Scenarios/UnitControl/EnterpriseArchetype.asset");
        Assert.IsNotNull(definition);
        var ship = SelectableShip.Create(definition, null);
        Own(ship.gameObject);
        ship.transform.rotation = Quaternion.Euler(0f, 0f, heading);
        ship.Agent.WarpTo(new Vector2(150f, 275f));

        var cameraObject = Own(new GameObject("Picking regression camera"));
        camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.pixelRect = new Rect(0f, 0f, 1600f, 1200f);
        camera.transform.position = new Vector3(150f, 275f, -10f);
        return ship;
    }
}
