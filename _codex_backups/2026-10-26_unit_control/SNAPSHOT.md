# Archived unit-control implementation

Saved on 2026-10-26 before the user-requested rollback for guided learning.

## Recovery references

- Local branch: `backup/unit-control-2026-10-26` at `1056149`.
- Runtime source checkpoint: `256b085`.
- Baseline before this feature: `4d81e4f`.
- Rollback commit on the active `merge` branch: `fa969a3`. Tracked Unity project files and the two edited project guides match `4d81e4f` exactly; the project-memory update is intentional. All 42 pre-existing user-file states were verified unchanged. Unity was not relaunched after rollback; the guided first step includes checking the original scene in Play mode.
- Plain-text full implementation diff: `unit-control-implementation.patch`.
- Pre-existing user-file fingerprints: `preexisting-work-before-revert.json`.
- Final validation: `validated-tests.xml` and `validated-tests.log`, 57 passed / 57 total.
- Terrain repair log: `scene-preview-repair.log`.
- Offscreen renders: `offscreen-visuals/`; these exclude IMGUI.
- Generated unit-demo player/ZIP moved out of the Unity project into `generated-build-artifacts/`. No new archive was created for this snapshot.

These references are for explanation and recovery. Do not automatically reapply the feature or switch the user's branch. The user wants to implement it personally, with detailed guidance.

## Scope and architecture

The new `UnitControlTesting` scene uses a separate 64x48 map, the existing Garcia assets, and a new Enterprise archetype, movement profile, and presentation profile. The carrier uses the existing Enterprise sprite. Its initial movement tuning is 342 m length, 26/38 kn cruise/flank, acceleration .035, normal braking .06, maximum braking .18, Low agility, and Deep draft. Its larger hull still gives substantially slower turning than Garcia.

`UnitSelectionSet<T>` owns replace/add/remove/clear behavior without Unity input dependencies. `SelectableShip` identifies the scene object, hull, archetype, navigation agent, and planner, and owns its selection ring. Projected tight sprite triangles make the visible hull clickable even at close zoom without selecting broad transparent texture margins; a 12-pixel outside tolerance helps at distant zoom.

`UnitControlDemo` owns input, selection, scene catalog, placement, shared map/service initialization, and a round-robin prediction budget. It translates gestures into commands. Each ship owns an externally driven `PathfindingSandboxController`, avoiding separate private maps and duplicate mouse processing. The controller still performs actual terrain/draft-safe path search, motion prediction, and fixed-step handoff. Selection visibility never determines whether accepted movement continues.

`NavigationDestinationSlots` finds reachable, safe endpoints with mixed-hull spacing and reserves them across units. World bounds use the actual hex Voronoi half-planes rather than circular half-cell bounds. `ShipTrafficHints` can suggest bounded geometric detours around stationary non-selected ships. Hints must not become user waypoints or alter the queue count. Moving crossings may still phase; this is not full crowd simulation or formations.

The scene-only spawn menu offers the two archetypes with sprite/name/type, followed by a green/red placement ghost. Terrain, draft, and strict map bounds constrain placement. Escape/right-click cancels, and successful placement exits after spawning one unselected ship. No colliders or rigidbodies are added.

Input contract: LMB/drag replaces selection, Shift adds, empty ordinary selection/Ctrl-E clears. RMB replaces orders, Shift-RMB appends, Ctrl-Shift-RMB inserts. F affects selected ships only. No selection issues no movement commands. New ships start unselected; deselecting preserves accepted movement and hides the course.

## Lessons and pitfalls for the guided rebuild

1. A saved scene reference to a ScriptableObject is shared, not a private copy. Create a separate small-map asset; do not retune the production map while preparing a test scene.
2. Grid dimensions alone do not generate islands. OA's `Hex Map Baker` generates cells, then `ApplyFromRuntime` saves the arrays and recipe. The presenter paints that data and establishes world centers; the path service uses it for legal navigation.
3. Keep input at the scene-commander level and movement per unit. Simply adding a second old sandbox controller would give both controllers the same clicks and duplicate bootstrap work.
4. Use `[SerializeField]` references deliberately, and explain scene objects, components, and assets before wiring them. Prefab/asset references differ from runtime instances.
5. `ShipNavigationAgent.Start` must not initialize a unit again after explicit factory/scene initialization. Predictor and live agent need the same hull-scaled steering/arrival distances.
6. VeryLow carrier handling initially stalled/orbited near an endpoint. Low agility with the larger hull and lower acceleration/braking passed actual profile orders in four directions. Do not bypass physical prediction to conceal a tuning failure.
7. The automated scene builder initially loaded asset references before a single-scene transition. Unity unloaded dependencies held only by stack locals, leaving missing agent archetypes and null terrain tile references. Loading after the transition and checking all 3,072 saved terrain cells fixed this. This is a reason to teach manual scene setup first.
8. Dynamically generated overlay textures/tiles are runtime resources. For a persistent editor preview, paint existing TileBase assets without a procedural overlay. Do not assume successful navigation tests prove the map is visible.
9. Route meshes under translated/rotated ships need world-to-local vertex conversion. The existing `RouteLineRenderer.AddVertex` already performs this; do not "fix" a nonexistent transform problem.
10. A deselected ship can finish planning later. Commit/handoff code must respect its visibility state or it can redraw a hidden route.
11. Unity asset metadata requires exactly 32 hex characters in the GUID. A malformed test metadata file initially hid five tests. The final run actually discovered and passed all 57.
12. Synthetic Sprite.OverrideGeometry fixtures were rejected by this Unity version. Final picking tests use the real Enterprise asset.
13. A shared prediction budget does not cap synchronous A* or geometry work. This is a two-ship functional demo, not a large-fleet frame-time guarantee.

## Guided milestones

At every milestone, the user makes the edits, Unity compiles, a short visible check passes, and only the relevant changes are committed. Keep the older sandbox usable.

1. Duplicate the legacy scene through Unity and create a separate 64x48 map asset. Bake using the existing `OA > Navigation > Hex Map Baker`, assign both controller Map Definition and presenter Preview Definition, and inspect a persistent base-terrain preview. First preserve existing single-ship navigation.
2. Explain archetypes versus instances. Add the Enterprise profiles and hull and make each ship own independent movement/planning state while sharing map/path services.
3. Implement selection state and click/box input with visible rings. Check add/replace/clear before connecting movement.
4. Route RMB/Shift-RMB/Ctrl-Shift-RMB to selected units only. Check that deselection neither cancels movement nor allows new orders to an unselected ship.
5. Build the scene-only catalog UI and placement mode. Check UI click consumption, terrain/draft legality, cancellation, and default unselected state.
6. Add safe destination reservations, then modest stationary-traffic hints. Preserve user waypoint semantics and actual motion prediction.
7. Run integration checks and authored tests; review the Git diff together before pushing. Aircraft remain a separate next milestone.
