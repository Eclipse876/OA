# Unit control demo — 2026-10-26

Open `OA Testing` in Unity 6000.3.7f1, then open `Assets/Scenes/UnitControlTesting.unity`. This is the first enabled Build Settings scene. The older large-map sandbox remains at `Assets/Scenes/PathfindingTestingScene.unity`.

The new scene has a seeded 64×48 hex map, islands, and two initially unselected ships. It uses the existing water/draft masks, physical route prediction, and fixed-step handoff. Each ship owns its course; selection only controls commands and route visibility. Deselecting a moving ship keeps its accepted orders.

| Control | Result |
| --- | --- |
| Left-click ship / drag a box | Replace selection |
| Shift-left-click / Shift-drag | Add ships to selection |
| Empty left-click / empty box / Ctrl-E | Clear selection |
| Right-click | Replace selected ships' movement orders |
| Shift-right-click | Append waypoints |
| Ctrl-Shift-right-click | Insert the next waypoint |
| F | Toggle cruise/flank for selected ships |
| WASD / middle drag / wheel | Pan / drag / zoom camera |
| Spawn Units | Choose Garcia or Enterprise, then left-click legal water to place one |
| Escape / right-click during placement | Cancel placement |

An empty additive selection preserves the existing selection. No selection means no movement commands. Newly spawned ships are unselected. The placement ghost is green on legal water and red on land, forbidden draft water, or outside the map.

The scene-only spawn menu shows each hull sprite and class/type. Enterprise is 342 m long, cruises at 26 kn, and flanks at 38 kn; Garcia is 126 m, 20/35 kn. Enterprise accelerates, brakes, and turns more slowly. These are initial gameplay values for tuning, not historical specifications.

Group destinations use separate reachable slots, with spacing based on hull size. Ships have no colliders or rigidbodies. Replacement orders can make a bounded detour around stationary ships; moving crossings may still pass through each other. There are no formations or live crowd-avoidance forces. All hinted routes still undergo the existing physical/terrain prediction.

The commander shares one navigation service and an 8 ms prediction budget across ships. Search/geometry work remains synchronous; the budget is not a hard cap on every frame. The two-ship scene is intended for selection and movement iteration, not a large-fleet performance claim.

Validation covers selection replacement/addition/clearing, selective movement and speed changes, queued/inserted orders, safe placement, distinct destination slots and actual arrivals, destroyed units, hidden routes, the assigned carrier profile in four directions, and the saved scene entering Play mode. Aircraft movement is the next separate pass.

All 57 authored cases passed in Unity 6000.3.7f1 in an isolated validation project with the current runtime source, scene/assets, main package manifest, and production rendering settings. The scene contains all 3,072 base terrain tiles in Edit mode and repaints all cells at runtime. An offscreen URP player capture confirmed map, hull, selection-ring, and route rendering; it excludes the IMGUI menu. Hardware mouse interaction still needs a normal Editor playthrough. Results are retained in `_codex_backups/2026-10-26_unit_control/validated-tests.xml`.
