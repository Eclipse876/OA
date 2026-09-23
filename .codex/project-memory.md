# Project Memory

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
