# Navigation reliability update — 2026-09-27

## Performance update — 2026-10-19

Planning CPU is below half a second across the seven production 1000 × 500 generated-map regression orders. These successive moving orders, including a corner-to-corner route and a land click, complete with zero handoff restarts. Reserved handoff lead is 0.26–0.50 seconds for the representative routes and 0.58 seconds for the extreme corner-to-corner route. The regression advances simulation steps directly; these reservations are not a measurement of interactive player latency.

The original delay was not primarily raw A*. Exact whole-route motion prediction, repeated long-segment smoothing, full-mask destination scans, and missed 0.25-second handoffs compounded into the observed 0.25 → 0.6 → 1.5 second retry staircase. The current implementation:

- caches repeated world-to-cell lookup work and starts speed-constraint scans near current route progress;
- uses adaptive fixed-step handoff reservations and preserves whole-order diagnostics across retries;
- replaces the smoother's pathological all-candidate backward search with recursive legal-segment reduction followed by a small quality-restoring cleanup pass;
- returns ordinary same-component water destinations directly, retaining the full nearest-reachable scan for land and disconnected water;
- reuses A* world-center values inside the expansion loop; and
- budgets 10 ms per frame for prediction and estimates 200,000 prediction steps per planning second for the initial handoff.

The EditMode navigation suite now has 17 passing cases. On the exact production-scale generated map, measured planning CPU ranges from roughly 79 to 432 ms; the worst route is approximately 233 ms prediction, 128 ms A*, and 63 ms smoothing. The Windows x64 player builds successfully with `PathfindingTesting` as its startup scene and completes a headless startup smoke test.

Remaining boundary: A* and smoothing are still synchronous, so a difficult global search can still create a single-frame spike. Whole-route physical prediction remains intentionally exact and is the largest cost on unobstructed map-spanning trips. The headless player smoke test verifies startup, while interactive frame pacing and visual behavior still require a manual playthrough.

## Windows handoff — 2026-10-20

The verified Windows build is packaged at `OA Testing/Builds/OA_Pathfinding_Windows_2026-10-19_c16b8cf.zip` (about 43.5 MB compressed). It contains the runtime files and a controls/validation readme, with Unity's `BurstDebugInformation_DoNotShip` folder excluded. Extract the entire archive before running `OA.exe`. The runtime source checkpoint is `c16b8cf`; this archive has not been uploaded or sent externally.

## Reliability baseline — 2026-09-27

The supplied test log contained seven stale-start rejections and one predicted-track collision. The successful orders in that log show that geometric reachability alone was not the main failure: several valid physical routes were discarded after the moving ship outpaced their starting snapshot.

## Player-facing behavior

- Clicks on land, forbidden shallow water, or disconnected water resolve to the closest reachable water-cell center, measured from the click in world space. Reachability includes the current ship's draft and clearance mask. Valid clicks retain their within-cell position, subject to the existing safe-stop adjustment.
- Resolved waypoints and their markers use that reachable destination. Rapid Shift-clicks retain pending waypoints instead of losing the previous click while planning.
- Moving orders plan from a forecast point on the currently accepted course. The ship switches at the reserved fixed simulation step, without teleporting or freezing. If planning misses that step, it extends the lead time and keeps the requested order.
- When ordinary turns and slower-turn candidates fail, a conservative candidate brakes at geometry corners and turns before continuing. This uses the existing pivot movement model and still undergoes full physical validation.
- Prediction duration grows with route length, terrain cost, and corner/acceleration allowances. The incremental predictor's old fixed 180,000-step ceiling no longer rejects long journeys by itself.
- Steering continues to follow the local course on the final leg; it no longer immediately aims at the distant endpoint simply because that is the next control point.
- Clicking an already reached position is accepted as arrival. A cancelled scheduled course cannot later replace the ship's accepted route or be displayed as accepted.

## Boundaries

This does not make an invalid world or physically impossible starting state navigable. If the ship has no legal starting water, the order reports that condition. Candidate motion remains checked against restricted water; validation is not bypassed to force a success. The old accepted course is retained if all new physical candidates are rejected.

The A* and smoothing stages remain synchronous. This change targets failed orders and safe route handoff; it is not a guarantee of a strict frame-time budget for every possible map. Connectivity is cached lazily per traversal mask; finding the nearest reachable destination scans that mask's cells.

The legacy synchronous `KinematicRoutePlanner.BuildRoute` helper is not called by the sandbox and retains its separate historical prediction implementation. The sandbox uses `BuildGuidanceCourse` plus `ShipRoutePrediction`.

## Verification

The authored EditMode suite includes:

- The four existing map tests.
- Nearest reachable destination for land and disconnected water, plus draft/clearance and stale-mask checks.
- A confined turn using conservative corner stops.
- A journey that needs more than 180,000 prediction steps.
- Exact equality between forecast and live movement at a scheduled handoff.
- Rapid waypoint appends while replanning a moving ship.
- Seven successive long-distance/land orders against `HexMapDefinition_Main.asset` (1000 × 500 cells), checking that the accepted endpoint belongs to the new order and is the closest reachable cell.
- Already-reached arrival, local final-leg steering, and scheduled-route cancellation.

Tests run in Unity 6000.3.7f1 in an isolated project containing the actual authored runtime scripts, tests, and scaled map. Results are retained locally in `_codex_backups/2026-09-27_navigation_validation/results.xml`; that disposable project is ignored by Git. The main open project also reported successful compilation and assembly reload during this work.

## In-editor check

1. Open `Assets/Scenes/PathfindingTesting.unity` and enter Play mode after compilation finishes.
2. Click across the map, then issue another destination while Garcia is moving. The previous course continues until the new route hands over.
3. Click inland and then an isolated/forbidden water area. Confirm that the destination marker moves to reachable coastal water rather than dropping the order.
4. Shift-click several destinations quickly; confirm they remain queued. Try Ctrl+Shift-click to insert an intermediate destination.
5. Test an island detour in cruise and flank. A difficult turn may use the slower corner-stop fallback.

The routing log now includes `CornerStops` and `HandoffRestarts`. A handoff restart means the order was retained and rescheduled, not discarded. `PlanWallTime` includes the wait for the reserved handoff, so it is not solely CPU time.

No scene or movement-profile retuning was required. Obsolete serialized maximum-prediction and stale-snapshot inspector fields are ignored; Unity will clean them up when the scene is next saved. The new handoff lead defaults to 0.25 seconds for a moving ship and grows only when required.
