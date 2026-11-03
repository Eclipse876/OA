OA - Unit Control Demo
Windows x64 | Unity 6000.3.7f1 | 2026-10-26
Runtime source checkpoint: 256b085
Startup scene: UnitControlTesting (64 x 48 hex map)

Extract the ENTIRE ZIP to a folder, then run OA.exe. Keep OA_Data,
MonoBleedingEdge, D3D12, and UnityPlayer.dll alongside the executable.

CONTROLS
Left-click a ship or drag a box: replace selection.
Shift-left-click / Shift-drag: add ships to selection.
Empty left-click / empty box / Ctrl-E: clear selection.
Right-click: move selected ships; Shift-right-click: append waypoints;
Ctrl-Shift-right-click: insert the next waypoint. F: cruise/flank.
WASD / middle-mouse drag / mouse wheel: camera movement and zoom.

Spawn Units (top left): choose Garcia or Enterprise, then left-click
legal water to place one unselected ship. Green/red ghost indicates
valid/invalid placement. Escape or right-click cancels placement.

Ships start unselected. Empty selection issues no movement commands.
Deselecting a ship does not stop its existing orders.
Enterprise accelerates/brakes/turns more slowly, with a higher top speed.
Ships have no physical collisions. Arrival slots spread groups out;
stationary traffic may get a route detour. Moving ships can phase through.
Formations, aircraft, combat, and live crowd avoidance are not included.

VALIDATION
57/57 authored tests passed, including entering the saved scene in Play
mode, selection, queued orders, placement, and separated arrivals.
The clean distribution passed an 8-second headless startup smoke test.
An equivalent QA build rendered map, hulls, selection rings, and routes
through offscreen URP captures. Those captures exclude the IMGUI menu;
normal mouse/keyboard interaction still needs an interactive playthrough.

Built from an isolated copy of the current authored scene/runtime/assets
using the main project's package manifest, settings, and URP renderer.
Test-only capture code and Unity's DoNotShip files are excluded.
The previous pathfinding ZIP is a separate older build.
