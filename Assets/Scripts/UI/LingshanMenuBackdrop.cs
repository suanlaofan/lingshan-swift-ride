using UnityEngine;
using UnityEngine.UI;

/// <summary>Show only the menu over black, then restore the exact gameplay camera settings.</summary>
public sealed class LingshanMenuBackdrop : MonoBehaviour
{
    private Camera targetCamera;
    private int previousMask;
    private CameraClearFlags previousClearFlags;
    private Color previousColor;
    private bool applied;

    public static LingshanMenuBackdrop Show(Canvas canvas)
    {
        var backdrop = canvas.gameObject.AddComponent<LingshanMenuBackdrop>();
        backdrop.targetCamera = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        if (backdrop.targetCamera == null) return backdrop;
        const int menuLayer = 5; // Built-in UI layer; all menu children have now been created.
        foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = menuLayer;
        backdrop.previousMask = backdrop.targetCamera.cullingMask;
        backdrop.previousClearFlags = backdrop.targetCamera.clearFlags;
        backdrop.previousColor = backdrop.targetCamera.backgroundColor;
        backdrop.targetCamera.cullingMask = 1 << menuLayer;
        backdrop.targetCamera.clearFlags = CameraClearFlags.SolidColor;
        backdrop.targetCamera.backgroundColor = Color.black;
        backdrop.applied = true;
        Debug.Log("LINGSHAN_MENU_BLACK_BACKGROUND " + canvas.name);
        return backdrop;
    }

    public void Restore()
    {
        if (!applied) return;
        if (targetCamera != null)
        {
            targetCamera.cullingMask = previousMask;
            targetCamera.clearFlags = previousClearFlags;
            targetCamera.backgroundColor = previousColor;
        }
        applied = false;
        Debug.Log("LINGSHAN_MENU_GAMEPLAY_RESTORED");
    }

    private void OnDisable() { Restore(); }
    private void OnDestroy() { Restore(); }
}
