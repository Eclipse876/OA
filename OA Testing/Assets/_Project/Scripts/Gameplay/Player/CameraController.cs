// CameraController.cs:
// Lightweight map-camera movement for gameplay and navigation sandbox scenes.
// WASD pans at a predictable real-time speed, while holding the middle mouse
// button lets the player grab the map and drag it beneath the camera.
using UnityEngine;
using UnityEngine.Serialization;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Camera))]
public sealed class CameraController : MonoBehaviour
{
    [Header("Keyboard Pan")]
        [SerializeField, Min(0f)] private float speed = 10f;
        [SerializeField] private bool scaleKeyboardPanWithZoom = true;
        [SerializeField, Min(0.01f)] private float referenceOrthographicSize = 5f;

    [Header("Mouse Drag Pan")]
        [SerializeField] private bool allowMiddleMouseDrag = true;

    [Header("Zoom")]
        [SerializeField, Min(0.01f)] private float zoomSpeed = 2f;
        [SerializeField] private bool scaleZoomWithCurrentSize = true;
        [SerializeField] private bool zoomTowardMousePosition = true;
        [SerializeField, Range(0.02f, 0.5f)] private float zoomFractionPerScrollStep = 0.025f;
        [SerializeField, Min(0.1f)] private float zoomScrollStepMultiplier = 0.25f;
        [SerializeField, Min(0.01f)] private float minimumScrollStepMagnitude = 1f;
        [SerializeField, Min(0.01f)] private float maximumScrollStepMagnitude = 2f;
        [SerializeField, Min(1f)] private float nearZoomSpeedMultiplier = 384f;
        [FormerlySerializedAs("nearZoomSpeedFalloffScale")]
        [SerializeField, Min(0.1f)] private float nearZoomSpeedFalloffExponent = 2f;
        [SerializeField, Min(0.1f)] private float nearZoomMaximumAcceleratedScrollSteps = 12f;
        [SerializeField, Min(1.01f)] private float nearZoomLimitFadeScale = 8f;
        [SerializeField, Min(1f)] private float farZoomSpeedMultiplier = 20f;
        [SerializeField, Min(0.1f)] private float farZoomAccelerationPower = 0.7f;
        [SerializeField, Min(0.1f)] private float maximumAcceleratedScrollSteps = 5f;
        [FormerlySerializedAs("zoomOutMousePanStrength")]
        [SerializeField, Range(0f, 0.25f)] private float zoomMousePanStrength = 0.2f;
        [SerializeField, Min(0f)] private float zoomSmoothTime = 0.08f;
        [SerializeField, Min(0.01f)] private float minimumOrthographicSize = 2f;
        [SerializeField, Min(0.01f)] private float maximumOrthographicSize = 40f;

    [Header("Framing")]
        [SerializeField] private bool deriveMinimumOrthographicSizeFromShip = true;
        [SerializeField] private SpriteRenderer shipFocusRenderer;
        [SerializeField, Range(0.1f, 0.95f)] private float shipViewportFill = 0.82f;
        [SerializeField] private bool deriveMaximumOrthographicSizeFromMap = true;
        [SerializeField] private Transform mapBoundsRoot;
        [SerializeField, Range(0.1f, 0.99f)] private float mapViewportFill = 0.94f;
        [SerializeField] private bool fitMapFromCurrentCameraPosition = true;
        [SerializeField] private bool autoFramePositionWithZoom = true;
        [SerializeField, Min(0f)] private float positionSmoothTime = 0.12f;

    private Camera controlledCamera;
    private bool isDragging;
    private Vector2 previousMouseScreenPosition;
    private float targetOrthographicSize;
    private Vector3 targetPosition;
    private float transitionStartOrthographicSize;
    private Vector3 transitionStartPosition;
    private float transitionElapsedSeconds;
    private bool isCameraTransitioning;
    private bool hasTargetPosition;
    private bool hasExplicitMapFrameBounds;
    private Bounds explicitMapFrameBounds;

    private void Awake()
    {
        EnsureCamera();
        targetOrthographicSize = controlledCamera.orthographicSize;
        targetPosition = transform.position;
        hasTargetPosition = true;
    }

    private void Update()
    {
        EnsureCamera();
        HandleScrollZoom();
        ApplyLinearCameraTransition();
        HandleMiddleMouseDrag();
        HandleKeyboardPan();
    }

    public void SetShipFocusRenderer(SpriteRenderer renderer)
    {
        shipFocusRenderer = renderer;
    }

    public void SetMapFrameBounds(Bounds bounds)
    {
        EnsureCamera();

        if (bounds.size.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        explicitMapFrameBounds = bounds;
        hasExplicitMapFrameBounds = true;
        targetOrthographicSize = ClampOrthographicSize(
            targetOrthographicSize > 0f
                ? targetOrthographicSize
                : controlledCamera != null
                    ? controlledCamera.orthographicSize
                    : minimumOrthographicSize);
    }

    public void SnapToCurrentZoomFrame()
    {
        EnsureCamera();

        if (controlledCamera == null || !controlledCamera.orthographic)
        {
            return;
        }

        targetOrthographicSize = ClampOrthographicSize(
            controlledCamera.orthographicSize);
        controlledCamera.orthographicSize = targetOrthographicSize;

        targetPosition = GetFramePositionForZoom(targetOrthographicSize);
        transform.position = targetPosition;
        hasTargetPosition = true;
        isCameraTransitioning = false;
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
                           GetCurrentKeyboardPanSpeed() *
                           Time.unscaledDeltaTime;

        FinishCameraTransition();
        transform.position += movement;
        targetPosition += movement;
        hasTargetPosition = true;
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
        FinishCameraTransition();
        transform.position += pan;
        targetPosition += pan;
        hasTargetPosition = true;
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

        scroll = NormalizeScrollStep(scroll);

        if (isCameraTransitioning)
        {
            targetOrthographicSize = controlledCamera.orthographicSize;
            targetPosition = transform.position;
            isCameraTransitioning = false;
        }

        float currentTarget = targetOrthographicSize > 0f
            ? targetOrthographicSize
            : controlledCamera.orthographicSize;
        Vector2 mouseScreenPosition = ReadMousePosition();
        Vector3 worldUnderMouse = zoomTowardMousePosition
            ? GetTargetWorldPointForScreenPosition(
                mouseScreenPosition,
                currentTarget)
            : targetPosition;

        float nextOrthographicSize = ClampOrthographicSize(
            GetZoomedOrthographicSize(currentTarget, scroll));

        if (zoomTowardMousePosition)
        {
            Vector3 fullyAnchoredPosition =
                GetTargetCameraPositionForWorldPoint(
                    mouseScreenPosition,
                    worldUnderMouse,
                    nextOrthographicSize);

            targetPosition = Vector3.Lerp(
                GetTargetCameraPosition(),
                fullyAnchoredPosition,
                Mathf.Clamp01(zoomMousePanStrength));
            hasTargetPosition = true;
        }
        else if (autoFramePositionWithZoom)
        {
            targetPosition = GetFramePositionForZoom(nextOrthographicSize);
            hasTargetPosition = true;
        }

        targetOrthographicSize = nextOrthographicSize;
        BeginCameraTransition();
    }

    private float GetCurrentKeyboardPanSpeed()
    {
        if (!scaleKeyboardPanWithZoom ||
            controlledCamera == null ||
            !controlledCamera.orthographic)
        {
            return speed;
        }

        float zoomFactor = controlledCamera.orthographicSize /
                           Mathf.Max(0.01f, referenceOrthographicSize);

        return speed * Mathf.Max(1f, zoomFactor);
    }

    private float GetZoomedOrthographicSize(float currentSize, float scroll)
    {
        if (!scaleZoomWithCurrentSize || controlledCamera == null)
        {
            return currentSize - scroll * zoomSpeed;
        }

        float fraction = Mathf.Clamp(zoomFractionPerScrollStep, 0.02f, 0.95f);
        float minimumSize = GetEffectiveMinimumOrthographicSize();
        float zoomScale = Mathf.Max(
            1f,
            currentSize / Mathf.Max(0.01f, minimumSize));

        float farAcceleration = Mathf.Min(
            Mathf.Max(1f, farZoomSpeedMultiplier),
            Mathf.Pow(zoomScale, Mathf.Max(0.1f, farZoomAccelerationPower)));
        float nearZoomBlend = Mathf.Pow(
            1f / Mathf.Max(1f, zoomScale),
            Mathf.Max(0.1f, nearZoomSpeedFalloffExponent));
        float nearZoomAcceleration = Mathf.Lerp(
            1f,
            Mathf.Max(1f, nearZoomSpeedMultiplier),
            nearZoomBlend);
        float acceleratedSteps =
            Mathf.Abs(scroll) *
            Mathf.Max(0.1f, zoomScrollStepMultiplier) *
            farAcceleration *
            nearZoomAcceleration;

        float nearLimitBlend = 1f - Mathf.InverseLerp(
            1f,
            Mathf.Max(1.01f, nearZoomLimitFadeScale),
            zoomScale);
        float maximumSteps = Mathf.Lerp(
            Mathf.Max(0.1f, maximumAcceleratedScrollSteps),
            Mathf.Max(
                maximumAcceleratedScrollSteps,
                nearZoomMaximumAcceleratedScrollSteps),
            nearLimitBlend);

        acceleratedSteps = Mathf.Min(acceleratedSteps, maximumSteps);

        float multiplier = Mathf.Pow(
            1f + fraction,
            -Mathf.Sign(scroll) * acceleratedSteps);

        float zoomedSize = currentSize * multiplier;
        return IsFinite(zoomedSize) ? zoomedSize : currentSize;
    }

    private float NormalizeScrollStep(float scroll)
    {
        float minMagnitude = Mathf.Max(0.01f, minimumScrollStepMagnitude);
        float maxMagnitude = Mathf.Max(
            minMagnitude,
            maximumScrollStepMagnitude);
        float magnitude = Mathf.Max(
            Mathf.Abs(scroll),
            minMagnitude);

        magnitude = Mathf.Min(magnitude, maxMagnitude);

        return Mathf.Sign(scroll) * magnitude;
    }

    private void BeginCameraTransition()
    {
        if (controlledCamera == null || !controlledCamera.orthographic)
        {
            return;
        }

        targetOrthographicSize = ClampOrthographicSize(targetOrthographicSize);
        transitionStartOrthographicSize = controlledCamera.orthographicSize;
        transitionStartPosition = transform.position;
        transitionElapsedSeconds = 0f;
        isCameraTransitioning = true;
    }

    private void ApplyLinearCameraTransition()
    {
        if (!isCameraTransitioning || controlledCamera == null)
        {
            return;
        }

        float duration = Mathf.Max(
            0.0001f,
            Mathf.Max(zoomSmoothTime, positionSmoothTime));

        transitionElapsedSeconds += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(transitionElapsedSeconds / duration);

        controlledCamera.orthographicSize = Mathf.Lerp(
            transitionStartOrthographicSize,
            targetOrthographicSize,
            t);

        transform.position = Vector3.Lerp(
            transitionStartPosition,
            targetPosition,
            t);

        if (t >= 1f)
        {
            controlledCamera.orthographicSize = targetOrthographicSize;
            transform.position = targetPosition;
            isCameraTransitioning = false;
        }
    }

    private void FinishCameraTransition()
    {
        if (!isCameraTransitioning || controlledCamera == null)
        {
            return;
        }

        controlledCamera.orthographicSize = targetOrthographicSize;
        transform.position = targetPosition;
        isCameraTransitioning = false;
    }

    private float ClampOrthographicSize(float size)
    {
        float minimumSize = GetEffectiveMinimumOrthographicSize();
        float maximumSize = GetEffectiveMaximumOrthographicSize();

        return Mathf.Clamp(size, minimumSize, maximumSize);
    }

    private float GetEffectiveMinimumOrthographicSize()
    {
        float minimumSize = Mathf.Max(0.01f, minimumOrthographicSize);

        if (!deriveMinimumOrthographicSizeFromShip ||
            shipFocusRenderer == null ||
            !TryGetRendererBounds(shipFocusRenderer, out Bounds shipBounds))
        {
            return minimumSize;
        }

        return Mathf.Max(
            minimumSize,
            GetOrthographicSizeForCenteredBounds(shipBounds, shipViewportFill));
    }

    private float GetEffectiveMaximumOrthographicSize()
    {
        float maximumSize = Mathf.Max(0.01f, maximumOrthographicSize);

        if (deriveMaximumOrthographicSizeFromMap &&
            TryGetMapFrameBounds(out Bounds mapBounds))
        {
            float mapSize = fitMapFromCurrentCameraPosition
                ? GetOrthographicSizeForBoundsFromCurrentPosition(
                    mapBounds,
                    mapViewportFill)
                : GetOrthographicSizeForCenteredBounds(
                    mapBounds,
                    mapViewportFill);

            maximumSize = Mathf.Min(maximumSize, mapSize);
        }

        return Mathf.Max(GetEffectiveMinimumOrthographicSize(), maximumSize);
    }

    private Vector3 GetFramePositionForZoom(float orthographicSize)
    {
        float minimumSize = GetEffectiveMinimumOrthographicSize();
        float maximumSize = GetEffectiveMaximumOrthographicSize();
        float blend = maximumSize > minimumSize
            ? Mathf.InverseLerp(minimumSize, maximumSize, orthographicSize)
            : 0f;

        Vector3 closeFocus = GetShipFocusPosition();
        Vector3 farFocus = TryGetMapFrameBounds(out Bounds mapBounds)
            ? mapBounds.center
            : closeFocus;

        Vector3 framePosition = Vector3.Lerp(closeFocus, farFocus, blend);
        framePosition.z = transform.position.z;
        return framePosition;
    }

    private Vector3 GetTargetWorldPointForScreenPosition(
        Vector2 screenPosition,
        float orthographicSize)
    {
        Vector2 viewport = GetClampedViewportPoint(screenPosition);
        Vector3 cameraPosition = GetTargetCameraPosition();
        float aspect = GetCameraAspect();

        return new Vector3(
            cameraPosition.x + (viewport.x - 0.5f) * 2f * aspect * orthographicSize,
            cameraPosition.y + (viewport.y - 0.5f) * 2f * orthographicSize,
            cameraPosition.z);
    }

    private Vector3 GetTargetCameraPositionForWorldPoint(
        Vector2 screenPosition,
        Vector3 worldPoint,
        float orthographicSize)
    {
        Vector2 viewport = GetClampedViewportPoint(screenPosition);
        float aspect = GetCameraAspect();

        Vector3 cameraPosition = new Vector3(
            worldPoint.x - (viewport.x - 0.5f) * 2f * aspect * orthographicSize,
            worldPoint.y - (viewport.y - 0.5f) * 2f * orthographicSize,
            GetTargetCameraPosition().z);

        return cameraPosition;
    }

    private Vector2 GetClampedViewportPoint(Vector2 screenPosition)
    {
        Vector2 viewport = controlledCamera.ScreenToViewportPoint(screenPosition);
        return new Vector2(
            Mathf.Clamp01(viewport.x),
            Mathf.Clamp01(viewport.y));
    }

    private Vector3 GetTargetCameraPosition()
    {
        if (!hasTargetPosition)
        {
            targetPosition = transform.position;
            hasTargetPosition = true;
        }

        return targetPosition;
    }

    private Vector3 GetShipFocusPosition()
    {
        if (shipFocusRenderer != null &&
            TryGetRendererBounds(shipFocusRenderer, out Bounds shipBounds))
        {
            Vector3 center = shipBounds.center;
            center.z = transform.position.z;
            return center;
        }

        return transform.position;
    }

    private bool TryGetMapFrameBounds(out Bounds bounds)
    {
        if (hasExplicitMapFrameBounds)
        {
            bounds = explicitMapFrameBounds;
            return bounds.size.sqrMagnitude > 0.000001f;
        }

        if (mapBoundsRoot != null &&
            TryGetRendererBounds(mapBoundsRoot, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private float GetOrthographicSizeForCenteredBounds(
        Bounds bounds,
        float viewportFill)
    {
        float fill = Mathf.Clamp(viewportFill, 0.1f, 0.99f);
        float aspect = GetCameraAspect();

        return Mathf.Max(
            bounds.extents.y / fill,
            bounds.extents.x / (aspect * fill),
            0.01f);
    }

    private float GetOrthographicSizeForBoundsFromCurrentPosition(
        Bounds bounds,
        float viewportFill)
    {
        float fill = Mathf.Clamp(viewportFill, 0.1f, 0.99f);
        float aspect = GetCameraAspect();
        Vector3 cameraPosition = transform.position;

        float halfHeight = Mathf.Max(
            Mathf.Abs(bounds.min.y - cameraPosition.y),
            Mathf.Abs(bounds.max.y - cameraPosition.y));

        float halfWidth = Mathf.Max(
            Mathf.Abs(bounds.min.x - cameraPosition.x),
            Mathf.Abs(bounds.max.x - cameraPosition.x));

        return Mathf.Max(
            halfHeight / fill,
            halfWidth / (aspect * fill),
            0.01f);
    }

    private float GetCameraAspect()
    {
        if (controlledCamera == null)
        {
            return 1f;
        }

        int pixelHeight = controlledCamera.pixelHeight > 0
            ? controlledCamera.pixelHeight
            : Screen.height;

        int pixelWidth = controlledCamera.pixelWidth > 0
            ? controlledCamera.pixelWidth
            : Screen.width;

        if (pixelHeight <= 0 || pixelWidth <= 0)
        {
            return Mathf.Max(0.01f, controlledCamera.aspect);
        }

        return Mathf.Max(0.01f, (float)pixelWidth / pixelHeight);
    }

    private static bool TryGetRendererBounds(
        Renderer renderer,
        out Bounds bounds)
    {
        bounds = default;

        if (renderer == null ||
            !renderer.enabled ||
            !renderer.gameObject.activeInHierarchy)
        {
            return false;
        }

        bounds = renderer.bounds;
        return bounds.size.sqrMagnitude > 0.000001f;
    }

    private static bool TryGetRendererBounds(
        Transform root,
        out Bounds bounds)
    {
        bounds = default;

        if (root == null)
        {
            return false;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (!TryGetRendererBounds(renderers[i], out Bounds rendererBounds))
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = rendererBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(rendererBounds);
            }
        }

        return hasBounds;
    }

    private void EnsureCamera()
    {
        if (controlledCamera == null)
        {
            controlledCamera = GetComponent<Camera>();
        }
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
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
