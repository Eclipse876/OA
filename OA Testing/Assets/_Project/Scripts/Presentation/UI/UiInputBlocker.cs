using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OA.Presentation.UI
{
    public static class UiInputBlocker
    {
        private static readonly List<VisualElement> blockingElements =
            new List<VisualElement>(16);

        public static void RegisterBlockingElement(VisualElement element)
        {
            if (element == null || blockingElements.Contains(element))
            {
                return;
            }

            blockingElements.Add(element);
        }

        public static void UnregisterBlockingElement(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            blockingElements.Remove(element);
        }

        public static bool IsPointerOverBlockingUi()
        {
            return TryGetPointerScreenPosition(out Vector2 screenPosition) &&
                   IsPointerOverBlockingUi(screenPosition);
        }

        public static bool IsPointerOverBlockingUi(Vector2 screenPosition)
        {
            if (blockingElements.Count == 0)
            {
                return false;
            }

            Vector2 panelPosition = new Vector2(
                screenPosition.x,
                Screen.height - screenPosition.y);

            for (int i = blockingElements.Count - 1; i >= 0; i--)
            {
                VisualElement element = blockingElements[i];
                if (element == null || element.panel == null)
                {
                    blockingElements.RemoveAt(i);
                    continue;
                }

                if (!element.visible ||
                    element.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }

                if (element.worldBound.Contains(panelPosition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetPointerScreenPosition(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
            {
                screenPosition = default;
                return false;
            }

            screenPosition = Mouse.current.position.ReadValue();
            return true;
#else
            screenPosition = Input.mousePosition;
            return true;
#endif
        }
    }
}
