using System.Collections.Generic;
using OA.Presentation.Units;
using OA.Simulation.Movement;
using OA.Simulation.Navigation;
using OA.Simulation.Units;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OA.Presentation.Debug
{
    [DefaultExecutionOrder(-200)]
    public sealed class UnitControlDemo : MonoBehaviour
    {
        [SerializeField] private HexMapDefinition mapDefinition;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private TilemapHexGridPresenter gridPresenter;
        [SerializeField] private HexMapPathService pathService;
        [SerializeField] private SelectableShip[] startingUnits;
        [SerializeField] private Vector2Int[] startingCells;
        [SerializeField] private UnitArchetypeDefinition[] availableUnits;
        [SerializeField, Min(0.5f)] private float planningBudgetMilliseconds = 8f;

        private readonly List<SelectableShip> units = new List<SelectableShip>();
        private readonly UnitSelectionSet<SelectableShip> selection = new UnitSelectionSet<SelectableShip>();
        private readonly List<SelectableShip> orderedUnits = new List<SelectableShip>();
        private readonly List<NavigationSlotReservation> reservations = new List<NavigationSlotReservation>();
        private readonly List<NavigationSlotReservation> traffic = new List<NavigationSlotReservation>();
        private readonly List<Vector2> hints = new List<Vector2>(2);
        private INavigationGridPresenter presenter;
        private HexMapRuntime map;
        private bool dropdownOpen;
        private int placementIndex = -1;
        private SpriteRenderer placementGhost;
        private Vector2 dragStart;
        private Vector2 pointer;
        private bool selecting;
        private bool additiveDrag;
        private int nextPlanner;
        private string status = "Select a ship or drag a selection box.";

        public IReadOnlyList<SelectableShip> Units => units;
        public IReadOnlyList<SelectableShip> SelectedUnits => selection.Items;
        public HexMapRuntime Map => map;
        public bool IsPlacing => placementIndex >= 0;
        public void ToggleSpawnMenu() { dropdownOpen = !dropdownOpen; }
        private Rect SpawnButtonRect => new Rect(16, 16, 160, 36);
        private Rect DropdownRect => new Rect(16, 58, 340, (availableUnits?.Length ?? 0) * 58 + 12);

        private void Awake()
        {
            if (mapDefinition == null || worldCamera == null || gridPresenter == null || pathService == null)
                return; // Allows tests to configure the same controller explicitly.

            Initialize(mapDefinition.CreateRuntimeCopy(), gridPresenter, pathService, worldCamera, availableUnits);
            if (startingUnits != null)
                for (int i = 0; i < startingUnits.Length; i++)
                {
                    Vector2Int requested = startingCells[i];
                    SelectableShip unit = startingUnits[i];
                    var mask = GetMask(unit.Archetype);
                    if (mask.TryFindClosestReachableCell(requested,
                        map.GetWorldCenter(requested.x, requested.y), out Vector2Int cell))
                        Register(unit, map.GetWorldCenter(cell.x, cell.y));
                    else UnityEngine.Debug.LogWarning($"[UnitControl] Invalid starting water for {unit.name}.", unit);
                }

            var cameraControls = worldCamera.GetComponent<global::CameraController>();
            if (cameraControls != null && units.Count > 0)
            {
                cameraControls.SetShipFocusRenderer(units[0].Hull);
                Bounds bounds = new Bounds(map.GetWorldCenter(0, 0), Vector3.zero);
                for (int y = 0; y < map.Height; y++)
                    for (int x = 0; x < map.Width; x++) bounds.Encapsulate(map.GetWorldCenter(x, y));
                bounds.Expand(map.CellSize);
                cameraControls.SetMapFrameBounds(bounds);
            }
        }

        public void Initialize(HexMapRuntime runtimeMap, INavigationGridPresenter grid,
            HexMapPathService navigation, Camera camera, UnitArchetypeDefinition[] catalog)
        {
            map = runtimeMap;
            presenter = grid;
            pathService = navigation;
            worldCamera = camera;
            availableUnits = catalog;
            presenter.BuildOrRefresh(map);
            pathService.RebuildGraph(map, new NavigationProfile(0f, ShipDraftClass.Shallow));
            ClearSelection();
        }

        private void Register(SelectableShip unit, Vector2 position)
        {
            unit.Initialize(map, pathService, presenter, worldCamera, position);
            units.Add(unit);
            unit.SetSelected(false);
        }

        private NavigationTraversalMask GetMask(UnitArchetypeDefinition archetype)
        {
            MovementProfileDefinition profile = archetype.movementProfile;
            return pathService.GetTraversalMask(map,
                new NavigationProfile(profile.NavigationSafetyRadiusWorld, profile.draftClass));
        }

        public void SelectClick(SelectableShip unit, bool additive)
        {
            PruneDestroyedUnits();
            if (unit == null) unit = null;
            if (unit != null && !units.Contains(unit)) return;
            selection.Select(unit, additive);
            RefreshSelection();
        }

        public void SelectBox(Rect screenRect, bool additive)
        {
            PruneDestroyedUnits();
            var hits = new List<SelectableShip>();
            foreach (SelectableShip unit in units)
            {
                Vector3 screen = worldCamera.WorldToScreenPoint(unit.transform.position);
                if (screen.z > 0f && screenRect.Contains(screen)) hits.Add(unit);
            }
            selection.Select(hits, additive);
            RefreshSelection();
        }

        public void ClearSelection()
        {
            selection.Clear();
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            foreach (SelectableShip unit in units)
                if (unit != null) unit.SetSelected(selection.Contains(unit));
        }

        public int MoveSelected(Vector2 destination, bool append, bool insert)
        {
            PruneDestroyedUnits();
            if (selection.Count == 0 || map == null || IsPlacing ||
                !NavigationDestinationSlots.IsWorldInsideMap(map, destination)) return 0;

            orderedUnits.Clear();
            orderedUnits.AddRange(selection.Items);
            Vector2 centroid = Vector2.zero;
            float spacing = map.CellSize * 0.3f;
            foreach (SelectableShip unit in orderedUnits)
            {
                centroid += (Vector2)unit.transform.position;
                spacing = Mathf.Max(spacing, unit.SeparationRadius * 2.2f);
            }
            centroid /= orderedUnits.Count;
            Vector2 forward = (destination - centroid).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector2.right;
            Vector2 lateral = new Vector2(-forward.y, forward.x);
            orderedUnits.Sort((a, b) => {
                int order = Vector2.Dot(a.transform.position, lateral).CompareTo(Vector2.Dot(b.transform.position, lateral));
                return order != 0 ? order : a.GetInstanceID().CompareTo(b.GetInstanceID());
            });

            reservations.Clear();
            traffic.Clear();
            foreach (SelectableShip unit in units)
            {
                if (selection.Contains(unit)) continue;
                reservations.Add(new NavigationSlotReservation(
                    unit.Planner.CurrentDestinationWorld ?? (Vector2)unit.transform.position,
                    unit.SeparationRadius));
                if (unit.Agent.CurrentMovementState.SpeedKnots < 0.25f)
                    traffic.Add(new NavigationSlotReservation(unit.transform.position, unit.SeparationRadius));
            }

            int issued = 0;
            for (int i = 0; i < orderedUnits.Count; i++)
            {
                SelectableShip unit = orderedUnits[i];
                Vector2 desired = destination + lateral * ((i - (orderedUnits.Count - 1) * 0.5f) * spacing);
                var mask = GetMask(unit.Archetype);
                if (!map.TryWorldToCell(unit.Agent.CurrentMovementState.Position, out Vector2Int start) ||
                    !NavigationDestinationSlots.TryFindSlot(map, mask, start, desired,
                        unit.SeparationRadius, reservations, out Vector2 slot)) continue;

                hints.Clear();
                // Queue detours only for immediate replacement orders. Existing
                // future waypoint legs cannot use a hint based on today's position.
                if (!append && ShipTrafficHints.TryGetDetour(map, mask,
                    unit.Agent.CurrentMovementState.Position, slot, unit.SeparationRadius,
                    traffic, out Vector2 approach, out Vector2 departure))
                {
                    hints.Add(approach);
                    hints.Add(departure);
                }
                if (unit.Planner.IssueMove(slot, append, insert, hints))
                {
                    reservations.Add(new NavigationSlotReservation(
                        slot, unit.SeparationRadius));
                    issued++;
                }
            }
            status = issued > 0 ? $"Order issued to {issued} ship(s)." : "No reachable, separated destination slots.";
            return issued;
        }

        public void ToggleSelectedSpeed()
        {
            PruneDestroyedUnits();
            foreach (SelectableShip unit in selection.Items) unit.Planner.RequestSpeedModeToggle();
        }

        public bool CanPlace(UnitArchetypeDefinition definition, Vector2 position)
        {
            return definition != null && map != null && NavigationDestinationSlots.IsWorldInsideMap(map, position) &&
                map.TryWorldToCell(position, out Vector2Int cell) && !GetMask(definition).IsBlocked(cell);
        }

        public SelectableShip SpawnAt(UnitArchetypeDefinition definition, Vector2 position)
        {
            if (!CanPlace(definition, position)) return null;
            var unit = SelectableShip.Create(definition, transform);
            Register(unit, position);
            return unit;
        }

        public void BeginPlacement(int catalogIndex)
        {
            CancelPlacement();
            if (availableUnits == null || catalogIndex < 0 || catalogIndex >= availableUnits.Length) return;
            placementIndex = catalogIndex;
            dropdownOpen = false;
            selecting = false;
            var ghostObject = new GameObject("Placement Preview");
            ghostObject.transform.SetParent(transform, false);
            placementGhost = ghostObject.AddComponent<SpriteRenderer>();
            UnitArchetypeDefinition definition = availableUnits[catalogIndex];
            placementGhost.sprite = definition.presentationProfile.worldSprite;
            placementGhost.sortingOrder = 60;
            ghostObject.transform.rotation = Quaternion.Euler(0f, 0f, -90f);
            Vector2 size = placementGhost.sprite.bounds.size;
            ghostObject.transform.localScale = Vector3.one *
                (definition.movementProfile.LengthWorldUnits / Mathf.Max(size.x, size.y));
            status = $"Place {definition.displayName}. Escape / right-click cancels.";
        }

        public bool TryPlace(Vector2 position)
        {
            if (!IsPlacing || SpawnAt(availableUnits[placementIndex], position) == null) return false;
            CancelPlacement();
            status = "Unit placed. Click it to select.";
            return true;
        }

        public void CancelPlacement()
        {
            placementIndex = -1;
            if (placementGhost != null)
            {
                if (Application.isPlaying) Destroy(placementGhost.gameObject);
                else DestroyImmediate(placementGhost.gameObject);
                placementGhost = null;
            }
        }

        public void TickPlanning()
        {
            PruneDestroyedUnits();
            if (units.Count == 0) return;
            var budget = System.Diagnostics.Stopwatch.StartNew();
            float slice = Mathf.Max(0.25f, planningBudgetMilliseconds / units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                SelectableShip unit = units[(nextPlanner + i) % units.Count];
                if (unit == null) continue;
                float remaining = planningBudgetMilliseconds - (float)budget.Elapsed.TotalMilliseconds;
                unit.Planner.TickExternal(remaining > 0f ? Mathf.Min(slice, remaining) : 0f);
            }
            nextPlanner = (nextPlanner + 1) % units.Count;
        }

        private void PruneDestroyedUnits()
        {
            for (int i = units.Count - 1; i >= 0; i--)
                if (units[i] == null) { selection.Remove(units[i]); units.RemoveAt(i); }
        }

        private void Update()
        {
            if (map == null || worldCamera == null) return;
            PruneDestroyedUnits();
            ReadControls();
            TickPlanning();
        }

        private void ReadControls()
        {
            bool leftDown, leftUp, rightDown, shift, control, clear, escape, fast;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            var keys = Keyboard.current;
            if (mouse == null) return;
            pointer = mouse.position.ReadValue();
            leftDown = mouse.leftButton.wasPressedThisFrame;
            leftUp = mouse.leftButton.wasReleasedThisFrame;
            rightDown = mouse.rightButton.wasPressedThisFrame;
            shift = keys != null && (keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed);
            control = keys != null && (keys.leftCtrlKey.isPressed || keys.rightCtrlKey.isPressed);
            clear = control && keys.eKey.wasPressedThisFrame;
            escape = keys != null && keys.escapeKey.wasPressedThisFrame;
            fast = keys != null && keys.fKey.wasPressedThisFrame;
#else
            pointer = Input.mousePosition;
            leftDown = Input.GetMouseButtonDown(0);
            leftUp = Input.GetMouseButtonUp(0);
            rightDown = Input.GetMouseButtonDown(1);
            shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            clear = control && Input.GetKeyDown(KeyCode.E);
            escape = Input.GetKeyDown(KeyCode.Escape);
            fast = Input.GetKeyDown(KeyCode.F);
#endif
            if (clear) { ClearSelection(); selecting = false; }
            if (escape || (IsPlacing && rightDown)) { CancelPlacement(); dropdownOpen = false; selecting = false; return; }
            bool overUi = IsPointerOverUi(pointer);
            Vector2 world = worldCamera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, -worldCamera.transform.position.z));
            if (IsPlacing)
            {
                placementGhost.transform.position = new Vector3(world.x, world.y, 0f);
                placementGhost.color = CanPlace(availableUnits[placementIndex], world)
                    ? new Color(0.3f, 1f, 0.5f, 0.65f) : new Color(1f, 0.25f, 0.2f, 0.65f);
                if (leftDown && !overUi) TryPlace(world);
                return;
            }
            if (fast && !overUi) ToggleSelectedSpeed();
            if (leftDown && !overUi) { selecting = true; dragStart = pointer; additiveDrag = shift; dropdownOpen = false; }
            if (leftUp && selecting)
            {
                selecting = false;
                if (overUi) return;
                if (Vector2.Distance(dragStart, pointer) >= 6f) SelectBox(SelectionRect(dragStart, pointer), additiveDrag);
                else SelectClick(PickShip(pointer), additiveDrag);
            }
            if (rightDown && !overUi) { selecting = false; dropdownOpen = false; MoveSelected(world, shift, shift && control); }
        }

        private SelectableShip PickShip(Vector2 screen)
        {
            SelectableShip nearest = null;
            float best = 12f;
            foreach (SelectableShip unit in units)
            {
                if (unit == null) continue;
                float distance = unit.ScreenPickDistance(worldCamera, screen);
                if (distance <= best) { nearest = unit; best = distance; }
            }
            return nearest;
        }

        public static Rect SelectionRect(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        public bool IsPointerOverUi(Vector2 screen)
        {
            Vector2 gui = new Vector2(screen.x, Screen.height - screen.y);
            return SpawnButtonRect.Contains(gui) || (dropdownOpen && DropdownRect.Contains(gui));
        }

        private void OnGUI()
        {
            if (map == null) return;
            if (GUI.Button(SpawnButtonRect, "Spawn Units")) ToggleSpawnMenu();
            if (dropdownOpen)
            {
                GUI.Box(DropdownRect, GUIContent.none);
                for (int i = 0; i < availableUnits.Length; i++)
                {
                    Rect row = new Rect(22, 64 + i * 58, 328, 52);
                    if (GUI.Button(row, GUIContent.none)) BeginPlacement(i);
                    DrawSprite(new Rect(row.x + 6, row.y + 4, 42, 44), availableUnits[i].presentationProfile.worldSprite);
                    GUI.Label(new Rect(row.x + 56, row.y + 17, 268, 24), availableUnits[i].displayName);
                }
            }
            GUI.Box(new Rect(16, Screen.height - 72, Mathf.Min(710, Screen.width - 32), 56),
                $"Selected: {selection.Count}    {status}\nLMB / drag: select  |  Shift: add  |  Ctrl-E: clear  |  RMB: move  |  Shift-RMB: queue  |  Ctrl-Shift-RMB: insert  |  F: speed");
            if (selecting && Vector2.Distance(dragStart, pointer) >= 6f)
            {
                Rect box = SelectionRect(dragStart, pointer);
                box.y = Screen.height - box.yMax;
                Color old = GUI.color;
                GUI.color = new Color(0.25f, 1f, 0.65f, 0.15f);
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = new Color(0.3f, 1f, 0.65f, 0.9f);
                GUI.DrawTexture(new Rect(box.x, box.y, box.width, 1), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(box.x, box.yMax - 1, box.width, 1), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(box.x, box.y, 1, box.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(box.xMax - 1, box.y, 1, box.height), Texture2D.whiteTexture);
                GUI.color = old;
            }
        }

        private static void DrawSprite(Rect rect, Sprite sprite)
        {
            if (sprite == null) return;
            Rect source = sprite.textureRect;
            float scale = Mathf.Min(rect.width / source.width, rect.height / source.height);
            Rect target = new Rect(rect.center.x - source.width * scale * 0.5f,
                rect.center.y - source.height * scale * 0.5f, source.width * scale, source.height * scale);
            GUI.DrawTextureWithTexCoords(target, sprite.texture, new Rect(source.x / sprite.texture.width,
                source.y / sprite.texture.height, source.width / sprite.texture.width, source.height / sprite.texture.height));
        }

        private void OnDestroy() { CancelPlacement(); }
    }
}
