using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    public float speed = 10;
    private Vector2 direction;
    void Update()
    {
        CameraTranslation();
        CameraZoom();

    }

    void CameraTranslation()
    {
        // 1. Get movement input (Direct check is most reliable)
        float x = 0;
        float y = 0;

        //Keyboard is a Unity specific keyword that links to the input manager package
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed) y = 1;
            if (kb.sKey.isPressed) y = -1;
            if (kb.aKey.isPressed) x = -1;
            if (kb.dKey.isPressed) x = 1;
        }

        Vector2 input = new Vector2(x, y).normalized;
        Vector3 move = new Vector3(input.x, input.y, 0) * speed * Time.deltaTime;
        transform.Translate(move, Space.World);
    }

    void CameraZoom()
    {
        //Mouse is a Unity specific keyword that links to the input manager package
        var mouse = Mouse.current;
        if (mouse == null) return;

        // Read the scroll wheel Y value
        float scrollValue = mouse.scroll.y.ReadValue();

        if (scrollValue != 0)
        {
            Camera cam = GetComponent<Camera>();

            // Zoom speed multiplier (scroll values are usually 120 or -120)
            float zoomChange = scrollValue * 0.5f;

            // Lower orthographicSize = Zoom In, Higher = Zoom Out
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize - zoomChange, 2f, 20f);
        }
    }
}
