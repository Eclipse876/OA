# OA Legacy Pathfinding Tech Demo (Standalone Package)

This package is a separate Unity demo for showing the earliest working pathfinding behavior beside the current OA pathfinding demo.

## What It Represents

- Cell-by-cell grid A*
- Four-direction neighbor movement
- No safety-radius clearance mask
- No rough-water path cost weighting
- No path simplification
- Simple constant-speed waypoint following

The current demo in `PathfindingTechDemoPackage` keeps the newer behavior. Build this legacy package separately so the portfolio can embed both versions as independent WebGL demos.

## Folder To Copy Into A New Unity Project

Copy this folder into your new project:

`Assets/OAPathfindingLegacyDemo/`

The scripts are self-bootstrapping. You do not need to create a scene prefab or wire references.

## Unity Setup Steps

1. Create a new Unity 3D project (Unity 2022 LTS+ recommended).
2. Copy `Assets/OAPathfindingLegacyDemo` from this package into the new project `Assets` folder.
3. Open any scene.
4. Verify package `com.unity.ugui` is installed.
5. Hit Play, or build the scene for WebGL.

## Runtime Controls

- `Reroll Map`: creates a new random map, keeps the ship in the center spawn gate, and clears land from that gate.
- `Generate Seeded Map`: rebuilds the map from the visible seed and obstacle value.
- `Left-click` open tile: finds and follows a legacy grid path to that destination.

## Portfolio Comparison

Use this package for the "oldest working pathfinding" embed and `PathfindingTechDemoPackage` for the current embed. Each package auto-installs its own demo controller, so they should be imported into separate Unity projects or built one at a time.
