using System.Collections.Generic;
using OA.Presentation.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OA.Presentation.Units
{
    public sealed class UnitSelectionController : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField]
        private List<SelectableShip> units =
            new List<SelectableShip>();

        private readonly List<SelectableShip> selectedShips =
            new List<SelectableShip>();

        public IReadOnlyList<SelectableShip> SelectedShips => selectedShips;

        private void Start()
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            if (worldCamera == null)
            {
                UnityEngine.Debug.LogError(
                    "UnitSelectionController needs a World Camera.",
                    this);
                enabled = false;
                return;
            }

            ClearSelection();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;

            bool controlHeld = keyboard != null &&
                (keyboard.leftCtrlKey.isPressed ||
                 keyboard.rightCtrlKey.isPressed);

            if (controlHeld && keyboard.eKey.wasPressedThisFrame)
            {
                ClearSelection();
                return;
            }

            var mouse = Mouse.current;

            if (mouse == null || !mouse.leftButton.wasReleasedThisFrame)
            {
                return;
            }

            Vector2 pointer = mouse.position.ReadValue();

            if (!worldCamera.pixelRect.Contains(pointer) ||
                UiInputBlocker.IsPointerOverBlockingUi(pointer))
            {
                return;
            }

            bool additive = keyboard != null &&
                (keyboard.leftShiftKey.isPressed ||
                 keyboard.rightShiftKey.isPressed);

            Select(PickShip(pointer), additive);
        }

        private void Select(SelectableShip ship, bool additive)
        {
            if (!additive)
            {
                ClearSelection();
            }

            if (ship == null || selectedShips.Contains(ship))
            {
                return;
            }

            selectedShips.Add(ship);
            ship.SetSelected(true);
        }

        public void ClearSelection()
        {
            foreach (SelectableShip ship in selectedShips)
            {
                if (ship != null)
                {
                    ship.SetSelected(false);
                }
            }

            selectedShips.Clear();
        }

        private SelectableShip PickShip(Vector2 pointer)
        {
            SelectableShip nearest = null;
            float nearestDistance = float.PositiveInfinity;

            foreach (SelectableShip ship in units)
            {
                if (ship == null ||
                    !ship.isActiveAndEnabled ||
                    ship.Agent == null)
                {
                    continue;
                }

                var archetype = ship.Agent.Archetype;

                if (archetype == null || archetype.movementProfile == null)
                {
                    continue;
                }

                Vector3 projected =
                    worldCamera.WorldToScreenPoint(ship.transform.position);

                if (projected.z <= 0f)
                {
                    continue;
                }

                float halfLength =
                    archetype.movementProfile.HalfLengthWorldUnits;

                Vector2 center = projected;

                Vector2 edge = worldCamera.WorldToScreenPoint(
                    ship.transform.position +
                    worldCamera.transform.right * halfLength);

                float radius = Mathf.Max(
                    12f,
                    Vector2.Distance(center, edge) + 4f);

                float distance = Vector2.Distance(pointer, center);

                if (distance <= radius && distance < nearestDistance)
                {
                    nearest = ship;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private void OnDisable()
        {
            ClearSelection();
        }
    }
}