# OA restart guide

Reviewed 2026-09-23 against the working tree, Git history, scene/asset references, and the running Unity editor. This is a status assessment and proposed roadmap; the navigation and new movement systems below have not been implemented in this review.

## Where the project stands

OA is a substantial single-ship naval movement and map-generation sandbox, with the beginnings of a tactical UI. It is not yet an integrated combat game. The most useful next milestone is reliable navigation on the scaled map, followed by separate aircraft and submarine movement prototypes.

Your recollection is confirmed by history. The September 22 squash preserves an August 24 “Map scale increase” commit whose description explicitly says long-range routing does not work yet, UI scaling needs work, and units need icons when zoomed out. Earlier work introduced native hex A*, physical ship-route prediction, procedural islands/depth bands, route lines, and the UI skeleton. August 31 added F-14 art/animation. September 23 contains a Unity upgrade followed by its revert.

| Location | Role |
| --- | --- |
| `OA Testing/` | Main Unity project, currently pinned to 6000.3.7f1. Open this folder in Unity Hub. It shares the enclosing OA Git repository. |
| `OA Testing/Assets/Scenes/PathfindingTesting.unity` | Current navigation sandbox and the scene shown in your screenshots. |
| `OA Testing/Assets/_Project/` | Main authored scripts, profiles, map assets, art, UI, and tests. |
| `OA Testing/Assets/Scenes/UI Dev.unity` | Separate UI development scene. |
| `Pathfinding Dev File/` | Earlier, simpler pathfinding prototype; useful as historical reference. |
| `_codex_backups/` | Previous implementation snapshots, not the active implementation. |
| Root `Assets`, `Library`, `Logs`, `UserSettings` | Additional root-level Unity remnants; root lacks the main project's Packages/ProjectSettings pair. Do not confuse it with `OA Testing`. |

There were already hundreds of working-tree changes, mainly Terrain Grid System files, package updates, and deleted exported demo packages. Preserve/review these separately before making a baseline commit. The Unity-version revert did not remove the newer package versions presently in the working tree. No commit or broad cleanup was performed during this review.

## What exists, and what is still scaffolding

| System | Current implementation |
| --- | --- |
| World | Seeded organic island generation, five water-depth categories, five land-elevation categories, movement costs, baked map data, native Tilemap presentation and texture overlays. |
| Navigation | `HexMapPathService`: OA's own A* over six-neighbor hexes, heap and reused search buffers, cached draft/clearance masks, world-center spatial buckets. This is the service assigned to PathfindingTesting. |
| Ship motion | Speed in knots, acceleration/braking, cruise/flank, agility presets, rudder/yaw response, drift, corner speed limits, arrival, confined-turn recovery, and physical route prediction. |
| Player controls | Click destination; Shift-click appends; Ctrl+Shift-click inserts ahead; F toggles cruise/flank; R rerolls. Camera supports WASD, middle-button drag, and wheel zoom. |
| Presentation | Garcia profile and hull, physical route lines, queued waypoints, camera framing, hover terrain data. UI Toolkit HUD/minimap code exists, but is not a complete game UI. |
| Units/orders | Archetype, movement/presentation profiles, runtime identity and Move/Stop order data exist. The generic order queue is not yet wired into the sandbox's waypoint execution. |
| Aircraft | F-14 textures, model/animation assets and animator controller exist. No aircraft movement controller was found. |
| Submarines | No submarine movement/depth-state implementation. Water depth categories and surface-ship draft rules are foundations, not diving behavior. |
| Combat, sensors, logistics, AI | Mostly folder scaffolding. Several UI classes are empty placeholders. |
| Tests/build | Four authored EditMode tests cover basic hex lookup/neighbors and map generation. No authored PlayMode test cases were found. Build Settings currently enables SampleScene, not PathfindingTesting. |

The A* Pathfinding Project package, `AStarHexPathService`, Terrain Grid System, and its presenter remain in the project. Their presence does not mean the active sandbox routes through them. Avoid spending the first restart session tuning the inactive adapter.

### How a movement order works

`PathfindingSandboxController` captures the ship state and destination/waypoint list. It first tries direct geometry; otherwise `HexMapPathService` finds legal hex corridors. `PathRouteSmoother` removes unnecessary corners. `KinematicRoutePlanner` adds steering and speed constraints. `ShipRoutePrediction` runs `ShipRouteFollower` and `ShipMovementModel` over the candidate and rejects blocked, stalled, over-budget, or stale results. Once accepted, `ShipNavigationAgent` follows the guidance using those same movement rules; `RouteLineRenderer` displays the predicted physical track.

That shared motion model is a good foundation: the line attempts to represent what the ship can actually execute. The expensive part is proving the entire trip before committing it.

## Long-range routing: first investigation

The current map is **1000 × 500 = 500,000 cells**, with `cellSize = 32.22222`. Garcia's assigned profile uses 31.034483 meters per world unit, 60 simulated seconds per real second, 126 m length, 20 kn cruise, and 35 kn flank.

First settle the scale terminology. The history says “each hex being 500m,” but the current presenter scales *horizontal center-to-center spacing* to `cellSize`. With the assigned meters-per-unit value, that spacing is approximately **1000 m**. Clarify whether 500 m meant a radius or desired spacing before retuning movement or changing the map.

These are code-backed investigation targets, not a claim that a particular failed route has already been reproduced:

1. **The frame budget does not cover all work.** Prediction checks a 4 ms frame budget between slices of 192 steps. A* and geometric smoothing still execute synchronously; one search, smoothing pass, or prediction slice can exceed that budget. Time each stage separately before choosing an optimization.
2. **Whole-trip prediction has finite limits.** The sandbox caps prediction at 1800 uncompressed seconds, and the predictor also has a 180,000-step ceiling. Motion applies the 60× time scale internally. A long detour or slow route can therefore be physically legal but rejected with `PredictionBudgetExceeded`. Keep budget exhaustion distinct from “no legal path.”
3. **Moving-ship snapshots expire quickly.** The scene allows 1.25 world units of drift and only one restart. At the current compressed cruise speed, that is roughly 63 ms of travel. A long plan can become stale while the ship follows its previous route. Test stationary orders and in-motion replanning separately.
4. **Clearance is quantized to whole cells.** Garcia's half-length is about 2.03 world units, but `ceil(radius / cellSize)` expands obstacles by one full neighboring-cell ring. The 0.5-world-unit extra-clearance attempts remain in the same quantized band at this scale. This can close narrow passages and make nominally wider alternatives equivalent; the controller already skips duplicate mask bands.
5. **Smoothing and display costs grow too.** Smoothing repeatedly tests long segments. Physical samples are spaced at 0.5 world units, independent of zoom or map scale. Measure both prediction/sample count and route-rendering cost; do not assume all latency is A*.

The existing `PathfindingBenchmarkRunner` measures raw synchronous A* queries only. Its default 2000-query loop should not be the first test on the big map. It cannot establish end-to-end order latency or physical route success.

## Roadmap, in practical order

### 1. Establish a reproducible baseline

- Use Unity 6000.3.7f1 and the repaired editor configuration. Save your currently modified scene deliberately; it was already dirty when inspected.
- Start in PathfindingTesting. Verify one short direct trip, one turn around land, waypoint insertion, and a cruise/flank change.
- Record the seed and exact start/destination for one failing long route. Keep a second case that reroutes an already moving ship.
- Run the four existing EditMode tests. Create an intentional baseline commit after reviewing the pre-existing package/vendor changes.

**Completion criterion:** a known-good short route and repeatable long-route failure, with the failure reason and configuration recorded.

### 2. Make long routes reliable

- Add separate timings for mask construction, A*, smoothing, prediction, and rendering. Record expanded nodes, route distance, prediction steps, candidate count, restarts, and precise failure reason.
- Build a small scenario matrix: short/medium/map-spanning open water; island detour; narrow channel; disconnected water; shallow/deep draft; queued waypoints; moving replans.
- Make expensive searches/smoothing resumable or otherwise budgeted, with cancellation for superseded orders. Current reused search buffers are not safe for concurrent requests without redesign.
- Prefer a global corridor plus detailed local movement validation near the ship, turns, and coastlines. Refine the remaining route as the ship travels. Keep preview confidence explicit if the distant physical track has not yet been validated.
- Fix snapshot handoff so new routes join the current moving state without teleporting or repeatedly timing out. Revisit clearance resolution and adaptive route-line sampling.
- Introduce a regional/hierarchical graph only if measurements show global A* remains a bottleneck after the simpler fixes.

**Completion criterion:** connected map-spanning routes finish reliably; unreachable goals fail clearly; the game remains responsive; moving replans join safely; representative regression tests preserve these behaviors. Select latency targets on the actual machine after collecting the baseline.

### 3. Make scale readable and create a shared movement boundary

- Add the zoomed-out unit icon transition already identified in history. `mapIconSprite` currently has no consuming code and Garcia's assigned map icon is the hull, despite separate friendly/hostile icon art being available.
- Separate destination/order coordination from ship-specific prediction. Share identity, selection, clock, commands, and route/status presentation; keep ship, aircraft, and submarine movement policies distinct.
- Establish one authoritative world scale and simulation clock. Current time compression lives in each movement profile, which becomes problematic when different unit types interact.
- Wire the order queue into execution and define completion/cancellation before adding patrol and return orders.

**Completion criterion:** unit types can share selection/orders and UI without pretending they have identical steering or arrival behavior.

### 4. Aircraft vertical slice

Use the F-14 as a single-plane prototype. Implement heading, airspeed, acceleration, minimum flight speed, bounded turn rate/radius, target altitude and climb/descent rate. Begin with direct travel across land/water and simple airspace boundaries; surface-water masks should not constrain ordinary flight.

A fixed-wing aircraft should transition to a loiter/orbit or next waypoint when reaching a destination. A ship-style Stop command must not freeze it in midair. Add Move, Patrol/Loiter, and Return behaviors, then fuel/endurance. Carrier launch/recovery and combat can follow once basic flight is dependable.

**Completion criterion:** it flies across the map, turns plausibly, changes altitude, enters a stable orbit, and follows replacement orders without using ship braking/pivot logic. A small aircraft prototype can proceed alongside navigation diagnosis once the shared scale/clock decisions are made.

### 5. Submarine vertical slice

Reuse suitable hull steering, but add current/target depth, dive/climb rate, maximum operating depth, surfaced/periscope/submerged states, and speed limits by state. Make navigation depth-aware: seabed clearance and legal depth transitions must be checked along the route.

Existing named water categories need explicit gameplay depth ranges or bathymetry before they can constrain a submarine in meters. `depthMeters` currently represents ship draft data and is not a diving state. Add Dive, Surface, and depth-hold orders, including failure when a requested depth cannot be reached safely. Save sonar/detection and combat for the next milestone.

**Completion criterion:** a submarine can navigate, dive, hold depth, and surface while respecting seabed and operating limits.

Keep balanced island starts deferred until ports/resources/combat give “fairness” a useful definition, consistent with the existing project notes.

## Unity sprite repair performed

The Sprite package was already present in both the manifest and Unity's registered packages. Reinstalling it was not the first useful fix.

- Removed the duplicate `Unity.InputSystem` entry in `OA Testing/Assets/_Project/Scripts/Gameplay/OA.Gameplay.asmdef`. Editor.log repeatedly reported this assembly-definition error and package type-load exceptions before the repair.
- Added a `Navigation` sorting layer for ID `495339909`, already referenced by the sandbox tilemap, Garcia sprite, and route renderers. Restored historical `WakeEffect` and `Units` IDs still referenced by older scenes/prefabs. Navigation precedes Default so runtime waypoint markers that use Default remain above the map.
- Preserved sprite GUIDs, sprite slicing, materials, package versions, and the unsaved scene. No Library deletion or editor upgrade was needed.

**Verified:** Unity's log recorded a successful script build and assembly reload; `Unity.2D.Sprite.Editor.dll` was rebuilt; the open scene displayed Garcia's hull; the Sprite Editor opened and displayed Garcia's sprite. Static checks confirmed all authored assembly-definition JSON parses without duplicate literal references and all nine authored renderer sorting-layer references resolve.

The exact contribution of the missing layers versus the assembly-loading failure to invisibility was not isolated independently; both were real defects and were repaired. No new long-range runtime benchmark or EditMode/PlayMode test run was performed in this review.

Unity also presented an API-update dialog for Terrain Grid System's `VRCheck.cs`. A pre-update copy is in `_codex_backups/2026-09-23_editor_repair/`. The automated click was rejected by approval review; on subsequent observation the dialog had cleared, and the file used `GetSubsystems` instead of `GetInstances`. This was observed separately from the two direct repairs above. Unity also changed VisualScriptingSettings during reload; review that generated change with the other existing updates.

For reference, Unity documents the inspector's package-install control in its [Sprite texture import settings](https://docs.unity.com/en-us/engine/6000.7/manual/materials-and-shaders/textures/textures-reference/texture-type-sprite). The local manifest, editor log, and successful Sprite Editor opening are the decisive evidence for this project's repair.

## Suggested first coding session

Capture one long-range failure and one moving-replan failure, instrument the five planning stages, and settle the 500 m versus 1000 m center-spacing convention. That produces a specific navigation fix to pursue while keeping the aircraft prototype small and independent.
