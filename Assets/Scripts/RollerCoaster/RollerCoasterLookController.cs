using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Keeps the camera fixed to the moving coaster while allowing the rider to look around.
/// The coaster controls its parent transform; this component only changes the camera mount's
/// local rotation, so looking never changes the route or the vehicle direction.
/// </summary>
[DisallowMultipleComponent]
public sealed class RollerCoasterLookController : MonoBehaviour
{
    [Header("Look")]
    [SerializeField, Range(0.02f, 0.5f)] private float mouseSensitivity = 0.14f;
    [SerializeField, Range(10f, 89f)] private float maximumPitch = 75f;
    [SerializeField] private bool lockCursorWhenRideStarts = true;

    private float yaw;
    private float pitch;

    private void Awake()
    {
        Vector3 startingAngles = transform.localEulerAngles;
        yaw = NormalizeAngle(startingAngles.y);
        pitch = Mathf.Clamp(NormalizeAngle(startingAngles.x), -maximumPitch, maximumPitch);
        ApplyLookRotation();
    }

    private void OnEnable()
    {
        if (lockCursorWhenRideStarts)
        {
            SetCursorLocked(true);
        }
    }

    private void OnDisable()
    {
        SetCursorLocked(false);
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
            return;
        }

        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            SetCursorLocked(true);
        }

        if (mouse == null || Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Vector2 mouseDelta = mouse.delta.ReadValue();
        yaw += mouseDelta.x * mouseSensitivity;
        pitch = Mathf.Clamp(pitch - mouseDelta.y * mouseSensitivity, -maximumPitch, maximumPitch);
        ApplyLookRotation();
#endif
    }

    private void ApplyLookRotation()
    {
        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }
}
