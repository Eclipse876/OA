# Navigation reliability update — 2026-09-27

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
