# Project Memory

## Working agreement: guided implementation and stable Git checkpoints

Updated: 2026-10-26

- The user develops and shares OA through GitHub and the Unity project. "Stable version" means a compiling, usable project checkpoint they can push, not a packaged executable. Do not generate builds or ZIPs unless explicitly requested.
- The user wants to learn by implementing the Unity setup and C# changes themselves. Explain each small step, its purpose, the relevant code/Unity concepts, and its observable success criteria. Provide code for the user to enter when needed; do not silently edit source, scenes, Inspector values, or run implementation commands on their behalf. Make direct changes only when they explicitly request them.
- Work toward small tested checkpoints; preserve the existing navigation sandbox while creating the separate unit-control scene. Avoid a long stretch of broken intermediate code.
- The October 26 automated unit-selection pass is archived on local branch `backup/unit-control-2026-10-26` at `1056149`; runtime checkpoint `256b085`. Plain-text patch, test evidence, architecture notes, and pitfalls are in `_codex_backups/2026-10-26_unit_control/`. Read `SNAPSHOT.md` before reconstructing it.
- At the user's request, this unit-control pass was rolled back to the tracked project state at `4d81e4f`, preserving earlier navigation fixes and all pre-existing user changes, including the rename to `Assets/Scenes/PathfindingTestingScene.unity`. Git history was preserved rather than reset. The original single-ship/left-click controls return until the user implements the new bindings.
- Archived implementation: 64x48 map, Garcia/Enterprise, empty default selection, RTS click/box/Shift-add/Ctrl-E, only RMB/Shift-RMB/Ctrl-Shift-RMB movement, selected-only speed control, scene-only unit catalog and legal-water placement ghost, no colliders/rigidbodies, reachable separated arrival slots and bounded stationary-traffic hints. Aircraft are a later pass.
- Validation at snapshot: 57/57 authored tests passed with production packages/URP settings; an actual scene Play-mode test and offscreen rendering passed. Hardware pointer interaction and IMGUI menu appearance were not manually verified. Do not describe the archived feature as fully interactively tested.
- Teach the rebuild in order: separate scene/map assets; ship definitions and per-ship navigation ownership; selection without movement; selected-only right-click orders; spawn UI/placement; destination slots and modest separation; integration verification. Begin with the user's manual Unity scene/map setup, not an automatic scene-builder script.

## Deferred: Balanced Island-Base Map Generation

Date noted: 2026-06-07

Keep the current map generator organic for now. Do not implement map fairness yet; revisit this when the proper game mechanics exist and can define what "fair" actually means.

The likely future approach is a balance layer over natural generation:

- Generate natural maps first, then evaluate them for start sites instead of forcing symmetry.
- Identify island-base candidates from shoreline land tiles with adjacent safe harbor water.
- Score starts using island size, shoreline access, harbor safety, nearby maneuver room, access to normal/deep ocean, reef/coastal/deep variety, and distance from map edges.
- Select 2-8 player starts with comparable scores and good geographic separation.
- Prefer rerolling seeds up to a configured attempt count before applying repairs.
- If repair is needed, keep it light: adjust a small harbor radius, fix unsafe shallow harbors, and ensure main-ocean connectivity.
- Store future start metadata rather than spawning real base/port gameplay objects until those systems exist.

Possible future data/API shape:

- `HexMapDefinition.enableBalancedStarts`
- `HexMapDefinition.balancedPlayerCount` from 2 to 8
- `HexMapDefinition.balanceAttempts`, default around 64
- Serialized `HexMapStartSite[] startSites`
- `HexMapStartSite.playerIndex`
- `HexMapStartSite.baseCell`
- `HexMapStartSite.harborCell`
- `HexMapStartSite.islandId`
- `HexMapStartSite.score`

Possible candidate rules:

- Label landmasses with BFS.
- Ignore tiny islands unless no better option exists.
- Base tile should usually be Land, Hill, or Large Hill.
- Harbor should be adjacent passable water, preferably Coastal or Deep, not Shallow.
- Harbor should connect to the main ocean component.
- Starts should have local maneuver room and not sit too close to the map edge.

Possible validation/tests:

- Deterministic output per seed.
- Base and harbor adjacency.
- Harbor passability and main-ocean connectivity.
- 2, 4, and 8 player start selection.
- Score spread tolerance for accepted maps.
- Fallback repair behavior when rerolls fail.

Design assumption to preserve: fairness should be "similar but natural," not mirrored. Resource, port, logistics, detection, and combat mechanics should decide the final balance scoring later.
