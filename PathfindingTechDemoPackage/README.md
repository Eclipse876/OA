# OA Pathfinding Tech Demo (Drop-In Package)

This package gives you a functional Unity tech demo for:
- Random grid terrain generation
- A* ship pathfinding with safety radius
- Live runtime tuning of ship movement traits

## Folder To Copy Into A New Unity Project

Copy this folder into your new project:

`Assets/OAPathfindingDemo/`

All scripts are self-bootstrapping. You do not need to create a scene prefab or wire references.

## Unity Setup Steps

1. Create a new Unity 3D project (Unity 2022 LTS+ recommended).
2. Copy `Assets/OAPathfindingDemo` from this package into the new project `Assets` folder.
3. Open any scene (an empty scene is fine).
4. Verify package `com.unity.ugui` is installed (it is included by default in normal templates).
5. Hit Play.

The demo auto-creates:
- Camera and light
- Grid map visuals
- A test ship using OA-style `UnitArchetypeDefinition` + `MovementProfileDefinition`
- Runtime UI panel for map generation + trait tuning

## Runtime Controls

- `Generate Random Map` button: creates a new map with current seed and obstacle rate.
- `Left-click` open tile: finds and follows a path to that destination.
- Trait rows (`Set` buttons): modify movement profile live:
  - Max Speed
  - Acceleration
  - Deceleration
  - Turn Rate (deg/s)
  - Turning Radius
  - Safety Radius
  - Stopping Distance
- `Apply All Traits`: apply all text fields at once.

## Notes

- Safety radius is used by pathfinding clearance checks, so larger values can intentionally invalidate tight routes.
- This is intentionally a prototype architecture designed for easy extension. Add new movement traits by adding another trait row in:
  - `Assets/OAPathfindingDemo/Scripts/TechDemo/PathfindingDemoUi.cs`
  - and corresponding logic in:
  - `Assets/OAPathfindingDemo/Scripts/TechDemo/PathfindingDemoController.cs`
