using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Small compatibility layer shared by the start and finish pages. Unity's
/// screen-space overlay canvases are not composited by the PICO XR display, so
/// Android builds use a head-relative world-space canvas instead.
/// </summary>
internal static class LingshanXRMenuSupport
{
    private const float CanvasDistance = 1.55f;
    private const float CanvasScale = 0.00125f;

    public static bool UsesWorldSpaceCanvas =>
        Application.platform == RuntimePlatform.Android;

    public static bool UsesHeadsetInput =>
        UsesWorldSpaceCanvas || XRSettings.isDeviceActive;

    public static void AttachToHead(Transform menuRoot)
    {
        if (!UsesWorldSpaceCanvas || menuRoot == null)
        {
            return;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        if (menuRoot.parent != camera.transform)
        {
            menuRoot.SetParent(camera.transform, false);
        }

        menuRoot.localPosition = Vector3.zero;
        menuRoot.localRotation = Quaternion.identity;
        menuRoot.localScale = Vector3.one;
    }

    public static void ConfigureCanvas(Canvas canvas, CanvasScaler scaler)
    {
        if (canvas == null || !UsesWorldSpaceCanvas)
        {
            return;
        }

        Camera camera = Camera.main;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;

        RectTransform rect = canvas.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1920f, 1080f);
        rect.localPosition = new Vector3(0f, 0f, CanvasDistance);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one * CanvasScale;

        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.dynamicPixelsPerUnit = 1f;
        }

        // Overlay canvases are invisible in the PICO compositor. The tracked
        // raycaster keeps this canvas compatible with XRI controller/hand
        // interactors when they are present, while LingshanXRMenuInput below
        // provides a controller-navigation fallback for the emulator.
        if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
        {
            canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }
    }

    public static void ConfigureEventSystem(EventSystem eventSystem, GameObject eventSystemObject)
    {
        if (!UsesHeadsetInput || eventSystem == null || eventSystemObject == null)
        {
            return;
        }

        InputSystemUIInputModule desktopModule =
            eventSystemObject.GetComponent<InputSystemUIInputModule>();
        if (desktopModule != null)
        {
            desktopModule.enabled = false;
        }

        XRUIInputModule xrModule = eventSystemObject.GetComponent<XRUIInputModule>();
        if (xrModule == null)
        {
            xrModule = eventSystemObject.AddComponent<XRUIInputModule>();
        }

        xrModule.enableXRInput = true;
        xrModule.enableMouseInput = true;
        // Menu navigation below owns gamepad input so a single press cannot be
        // processed once by EventSystem and a second time by the fallback.
        xrModule.enableGamepadInput = false;
    }
}

/// <summary>
/// Reads the standard XR controller usages used by PICO Emulator 0.13 and
/// maps the thumb stick and primary/trigger buttons onto the existing Unity UI
/// navigation. The desktop Input System module remains the editor fallback.
/// </summary>
internal sealed class LingshanXRMenuInput
{
    private bool axisLatched;
    private bool submitLatched;

    public void Update(EventSystem eventSystem, Button first, Button second)
    {
        if (!LingshanXRMenuSupport.UsesHeadsetInput || eventSystem == null || first == null || second == null)
        {
            return;
        }

#if ENABLE_INPUT_SYSTEM
        bool keyboardNavigationHandled = ReadKeyboardNavigation(eventSystem, first, second);
#else
        bool keyboardNavigationHandled = false;
#endif

        Vector2 axis = ReadThumbstick();
#if ENABLE_INPUT_SYSTEM
        axis = ReadInputSystemNavigation(axis);
#endif
        float vertical = axis.y;
        if (!keyboardNavigationHandled && !axisLatched && Mathf.Abs(vertical) >= 0.6f)
        {
            Select(eventSystem, vertical > 0f ? first : second, "XR controller");
            axisLatched = true;
        }
        else if (Mathf.Abs(vertical) <= 0.25f)
        {
            axisLatched = false;
        }

        bool submit = ReadSubmitButton();
#if ENABLE_INPUT_SYSTEM
        submit |= ReadInputSystemSubmitButton();
#endif
#if ENABLE_INPUT_SYSTEM
        bool keyboardSubmit = ReadKeyboardSubmit();
#else
        bool keyboardSubmit = false;
#endif
        if ((submit && !submitLatched) || keyboardSubmit)
        {
            GameObject selected = eventSystem.currentSelectedGameObject;
            Button button = selected != null ? selected.GetComponent<Button>() : first;
            if (button != null && button.IsInteractable())
            {
                Debug.Log($"Lingshan menu input: invoked {button.name} via {(keyboardSubmit ? "keyboard" : "XR controller")}.");
                button.onClick.Invoke();
            }
        }

        submitLatched = submit;
    }

#if ENABLE_INPUT_SYSTEM
    private static bool ReadKeyboardNavigation(EventSystem eventSystem, Button first, Button second)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        if (keyboard.upArrowKey.wasPressedThisFrame ||
            keyboard.wKey.wasPressedThisFrame ||
            keyboard.iKey.wasPressedThisFrame)
        {
            Select(eventSystem, first, "keyboard");
            return true;
        }

        if (keyboard.downArrowKey.wasPressedThisFrame ||
            keyboard.sKey.wasPressedThisFrame ||
            keyboard.kKey.wasPressedThisFrame)
        {
            Select(eventSystem, second, "keyboard");
            return true;
        }

        return false;
    }

    private static bool ReadKeyboardSubmit()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null &&
            (keyboard.enterKey.wasPressedThisFrame ||
             keyboard.numpadEnterKey.wasPressedThisFrame ||
             keyboard.spaceKey.wasPressedThisFrame ||
             keyboard.xKey.wasPressedThisFrame ||
             keyboard.qKey.wasPressedThisFrame ||
             keyboard.uKey.wasPressedThisFrame);
    }

    private static Vector2 ReadInputSystemNavigation(Vector2 current)
    {
        foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
        {
            current = ReadVector2Control(device, "thumbstick", current);
            current = ReadVector2Control(device, "primary2DAxis", current);
            current = ReadVector2Control(device, "leftStick", current);
            current = ReadVector2Control(device, "rightStick", current);
            current = ReadVector2Control(device, "joystick", current);
            current = ReadVector2Control(device, "dpad", current);
        }

        return current;
    }

    private static Vector2 ReadVector2Control(
        UnityEngine.InputSystem.InputDevice device,
        string path,
        Vector2 current)
    {
        Vector2Control control = device.TryGetChildControl<Vector2Control>(path);
        if (control == null)
        {
            return current;
        }

        Vector2 value = control.ReadValue();
        return value.sqrMagnitude > current.sqrMagnitude ? value : current;
    }

    private static bool ReadInputSystemSubmitButton()
    {
        foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
        {
            AxisControl trigger = device.TryGetChildControl<AxisControl>("trigger");

            if (IsButtonPressed(device, "primaryButton") ||
                IsButtonPressed(device, "triggerPressed") ||
                IsButtonPressed(device, "buttonSouth") ||
                IsButtonPressed(device, "buttonEast") ||
                IsButtonPressed(device, "buttonNorth") ||
                IsButtonPressed(device, "buttonWest") ||
                IsButtonPressed(device, "a") ||
                IsButtonPressed(device, "x") ||
                (trigger != null && trigger.ReadValue() >= 0.65f))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsButtonPressed(
        UnityEngine.InputSystem.InputDevice device,
        string path)
    {
        ButtonControl button = device.TryGetChildControl<ButtonControl>(path);
        return button != null && button.isPressed;
    }
#endif

    private static void Select(EventSystem eventSystem, Button button, string source)
    {
        if (eventSystem.currentSelectedGameObject == button.gameObject)
        {
            return;
        }

        eventSystem.SetSelectedGameObject(button.gameObject);
        Debug.Log($"Lingshan menu input: selected {button.name} via {source}.");
    }

    private static Vector2 ReadThumbstick()
    {
        Vector2 result = Vector2.zero;
        result = ReadThumbstick(InputDevices.GetDeviceAtXRNode(XRNode.LeftHand), result);
        result = ReadThumbstick(InputDevices.GetDeviceAtXRNode(XRNode.RightHand), result);
        return result;
    }

    private static Vector2 ReadThumbstick(UnityEngine.XR.InputDevice device, Vector2 current)
    {
        if (!device.isValid)
        {
            return current;
        }

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 primary) && primary.sqrMagnitude > current.sqrMagnitude)
        {
            current = primary;
        }

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondary2DAxis, out Vector2 secondary) && secondary.sqrMagnitude > current.sqrMagnitude)
        {
            current = secondary;
        }

        return current;
    }

    private static bool ReadSubmitButton()
    {
        return ReadSubmitButton(InputDevices.GetDeviceAtXRNode(XRNode.LeftHand)) ||
            ReadSubmitButton(InputDevices.GetDeviceAtXRNode(XRNode.RightHand));
    }

    private static bool ReadSubmitButton(UnityEngine.XR.InputDevice device)
    {
        if (!device.isValid)
        {
            return false;
        }

        bool pressed = false;
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool primary))
        {
            pressed |= primary;
        }
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool trigger))
        {
            pressed |= trigger;
        }
        return pressed;
    }
}
