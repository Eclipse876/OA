// CameraController.cs:
// Lightweight map-camera movement for gameplay and navigation sandbox scenes.
// WASD pans at a predictable real-time speed, while holding the middle mouse
// button lets the player grab the map and drag it beneath the camera.
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Camera))]
public sealed class CameraController : MonoBehaviour
{
    [Header("Keyboard Pan")]
        [SerializeField, Min(0f)] private float speed = 10f;

    [Header("Mouse Drag Pan")]
        [SerializeField] private bool allowMiddleMouseDrag = true;

    [Header("Zoom")]
        [SerializeField, Min(0.01f)] private float zoomSpeed = 2f;
        [SerializeField, Min(0.01f)] private float minimumOrthographicSize = 2f;
        [SerializeField, Min(0.01f)] private float maximumOrthographicSize = 40f;

    private Camera controlledCamera;
    private bool isDragging;
    private Vector2 previousMouseScreenPosition;

    private void Awake()
    {
        controlledCamera = GetComponent<Camera>();
    }

    private void Update()
    {
        HandleScrollZoom();
        HandleMiddleMouseDrag();
        HandleKeyboardPan();
    }

    // WASD movement uses unscaled time so map navigation still works while paused.
    private void HandleKeyboardPan()
    {
        Vector2 input = ReadKeyboardInput();

        if (input.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        Vector3 movement = new Vector3(input.x, input.y, 0f) *
                           speed *
                           Time.unscaledDeltaTime;

        transform.position += movement;
    }

    // Physical Mouse Button 3 is middle/wheel-button input.
    // Screen-space mouse delta is converted into world-space camera movement.
    private void HandleMiddleMouseDrag()
    {
        if (!allowMiddleMouseDrag || controlledCamera == null)
        {
            isDragging = false;
            return;
        }

        Vector2 mousePosition = ReadMousePosition();

        if (MiddleMousePressedThisFrame())
        {
            previousMouseScreenPosition = mousePosition;
            isDragging = true;
        }

        if (!isDragging)
        {
            return;
        }

        if (!MiddleMouseIsHeld())
        {
            isDragging = false;
            return;
        }

        Vector2 mouseDelta = mousePosition - previousMouseScreenPosition;
        previousMouseScreenPosition = mousePosition;

        if (mouseDelta.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        float planeDistance = Mathf.Abs(transform.position.z);
        Vector3 before = controlledCamera.ScreenToWorldPoint(
            new Vector3(0f, 0f, planeDistance));

        Vector3 after = controlledCamera.ScreenToWorldPoint(
            new Vector3(mouseDelta.x, mouseDelta.y, planeDistance));

        Vector3 pan = before - after;
        pan.z = 0f;
        transform.position += pan;
    }

    // Scroll-wheel zoom changes map scale while keeping the view readable.
    private void HandleScrollZoom()
    {
        if (controlledCamera == null || !controlledCamera.orthographic)
        {
            return;
        }

        float scroll = ReadScrollInput();

        if (Mathf.Abs(scroll) <= 0.0001f)
        {
            return;
        }

        controlledCamera.orthographicSize = Mathf.Clamp(
            controlledCamera.orthographicSize - scroll * zoomSpeed,
            minimumOrthographicSize,
            maximumOrthographicSize);
    }

    private static float ReadScrollInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null)
        {
            return 0f;
        }

        // Input System scroll values are approximately 120 units per wheel notch.
        return Mouse.current.scroll.ReadValue().y / 120f;
#else
        return Input.mouseScrollDelta.y;
#endif
    }


    private static Vector2 ReadKeyboardInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null)
        {
            return Vector2.zero;
        }

        float x = 0f;
        float y = 0f;

        if (Keyboard.current.aKey.isPressed)
        {
            x -= 1f;
        }

        if (Keyboard.current.dKey.isPressed)
        {
            x += 1f;
        }

        if (Keyboard.current.sKey.isPressed)
        {
            y -= 1f;
        }

        if (Keyboard.current.wKey.isPressed)
        {
            y += 1f;
        }

        return new Vector2(x, y);
#else
        float x = 0f;
        float y = 0f;

        if (Input.GetKey(KeyCode.A))
        {
            x -= 1f;
        }

        if (Input.GetKey(KeyCode.D))
        {
            x += 1f;
        }

        if (Input.GetKey(KeyCode.S))
        {
            y -= 1f;
        }

        if (Input.GetKey(KeyCode.W))
        {
            y += 1f;
        }

        return new Vector2(x, y);
#endif
    }

    private static Vector2 ReadMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    private static bool MiddleMousePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null &&
               Mouse.current.middleButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(2);
#endif
    }

    private static bool MiddleMouseIsHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null &&
               Mouse.current.middleButton.isPressed;
#else
        return Input.GetMouseButton(2);
#endif
    }
}
