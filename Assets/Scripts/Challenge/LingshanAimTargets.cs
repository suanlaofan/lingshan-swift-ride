using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

/// <summary>Lightweight world targets and tracked controller rays. No lights, colliders, or physics bodies.</summary>
public sealed class LingshanAimTargets : IDisposable
{
    public const float AimRadius = 1.35f;
    public const float MaximumRange = 55f;
    private readonly GameObject root;
    private readonly Transform[] targets;
    private readonly MeshRenderer[] cores;
    private readonly LineRenderer[] rings;
    private readonly LineRenderer[] rays = new LineRenderer[2];
    private readonly Mesh mesh;
    private readonly Material gold, locked, laser;
    private readonly Camera camera;
    private readonly Transform trackingSpace;
    private readonly LingshanTargetWindow[] windows;
    private int activeIndex = -1;
    private bool leftAimed, rightAimed, desktopAimed;

    public bool IsAimed => leftAimed || rightAimed || desktopAimed;
    public bool HasTrackedController { get; private set; }
    public bool UsesDesktopAim => Application.isEditor || (Application.platform != RuntimePlatform.Android && !XRSettings.isDeviceActive);

    public LingshanAimTargets(RollerCoasterRide ride, LingshanTargetWindow[] layout)
    {
        windows = layout;
        camera = Camera.main;
        trackingSpace = camera == null ? null : camera.transform.parent;
        gold = Resources.Load<Material>("LingshanChallenge/CharmGold");
        locked = Resources.Load<Material>("LingshanChallenge/CharmLocked");
        laser = Resources.Load<Material>("LingshanChallenge/ControllerRay");
        if (gold == null || locked == null || laser == null)
            throw new InvalidOperationException("Apply Lingshan PICO profile to create bundled charm materials.");
        root = new GameObject("Lingshan World Charms");
        root.transform.SetParent(ride.transform, false);
        targets = new Transform[layout.Length];
        cores = new MeshRenderer[layout.Length];
        rings = new LineRenderer[layout.Length];
        mesh = BuildCharmMesh();
        for (int i = 0; i < layout.Length; i++)
        {
            // Alternate sides within a comfortable forward view; fixed world positions make aim meaningful.
            ride.EvaluateRouteDistance(layout[i].EndDistance - 3f, out Vector3 position, out Vector3 forward, out Vector3 up);
            Vector3 right = Vector3.Cross(up, forward).normalized;
            var target = new GameObject("Charm " + (i + 1).ToString("00"));
            targets[i] = target.transform;
            target.transform.SetParent(root.transform, false);
            target.transform.position = position + right * (i % 2 == 0 ? -3.5f : 3.5f) + up * (i % 3 == 0 ? 3.8f : 2.6f);
            if (i == 11)
            {
                // The final bend crosses a terrain ridge. Raise this target clear of its line of sight.
                ride.EvaluateRouteDistance((layout[i].StartDistance + layout[i].EndDistance) * .5f,
                    out _, out _, out Vector3 approachUp);
                target.transform.position += approachUp * 4f;
            }
            target.AddComponent<MeshFilter>().sharedMesh = mesh;
            cores[i] = target.AddComponent<MeshRenderer>();
            cores[i].sharedMaterial = gold;
            cores[i].shadowCastingMode = ShadowCastingMode.Off;
            cores[i].receiveShadows = false;
            rings[i] = MakeLine("Target Ring", target.transform, gold, 0.055f);
            rings[i].useWorldSpace = false;
            rings[i].loop = true;
            rings[i].positionCount = 24;
            for (int p = 0; p < 24; p++)
            {
                float angle = p * Mathf.PI * 2f / 24f;
                rings[i].SetPosition(p, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * AimRadius);
            }
            target.SetActive(false);
        }
        for (int hand = 0; hand < 2; hand++)
        {
            rays[hand] = MakeLine(hand == 0 ? "Left Aim Ray" : "Right Aim Ray", root.transform, laser, 0.002f);
            rays[hand].positionCount = 2;
            rays[hand].enabled = false;
        }
    }

    public bool TryGetActivePosition(out Vector3 position)
    {
        position = activeIndex >= 0 && activeIndex < targets.Length ? targets[activeIndex].position : Vector3.zero;
        return activeIndex >= 0 && activeIndex < targets.Length;
    }

    public void Update(float distance, int nextTarget, bool visible, bool playing)
    {
        activeIndex = nextTarget < windows.Length && distance >= windows[nextTarget].StartDistance &&
            distance <= windows[nextTarget].EndDistance ? nextTarget : -1;
        for (int i = 0; i < targets.Length; i++)
        {
            bool show = visible && i >= nextTarget && i <= nextTarget + 1 && distance >= windows[i].StartDistance - 30f;
            if (targets[i].gameObject.activeSelf != show) targets[i].gameObject.SetActive(show);
            if (show && camera != null)
            {
                rings[i].transform.rotation = Quaternion.LookRotation(targets[i].position - camera.transform.position);
                if (playing) targets[i].Rotate(Vector3.up, Time.deltaTime * 28f, Space.World);
                cores[i].sharedMaterial = gold;
                rings[i].sharedMaterial = gold;
            }
        }
        leftAimed = rightAimed = desktopAimed = false;
        HasTrackedController = false;
        for (int hand = 0; hand < 2; hand++)
        {
            Ray ray = default;
            bool valid = visible && TryControllerRay(hand == 0 ? XRNode.LeftHand : XRNode.RightHand, out ray);
            rays[hand].enabled = valid && playing;
            if (!valid) continue;
            HasTrackedController = true;
            bool aimed = TestAim(ray, out float hitDistance);
            if (hand == 0) leftAimed = aimed; else rightAimed = aimed;
            rays[hand].SetPosition(0, ray.origin);
            rays[hand].SetPosition(1, ray.GetPoint(aimed ? hitDistance : 15f));
            rays[hand].sharedMaterial = aimed ? locked : laser;
        }
        if (visible && playing && UsesDesktopAim && camera != null)
            desktopAimed = TestAim(new Ray(camera.transform.position, camera.transform.forward), out _);
        if (IsAimed && activeIndex >= 0)
        {
            cores[activeIndex].sharedMaterial = locked;
            rings[activeIndex].sharedMaterial = locked;
        }
    }

    public bool PressedHandIsAimed(bool leftPressed, bool rightPressed, bool desktopPressed) =>
        (leftPressed && leftAimed) || (rightPressed && rightAimed) || (UsesDesktopAim && desktopPressed && desktopAimed);

    public bool TestAim(Ray ray, out float hitDistance)
    {
        hitDistance = 0f;
        if (!TryGetActivePosition(out Vector3 target) || !RayHitsTarget(ray, target, AimRadius, MaximumRange, out hitDistance)) return false;
        // Ignore trigger volumes; solid scene geometry blocks collection.
        return !Physics.Raycast(ray, hitDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    public static bool RayHitsTarget(Ray ray, Vector3 center, float radius, float maxRange, out float distance)
    {
        distance = 0f;
        Vector3 direction = ray.direction.normalized;
        if (direction.sqrMagnitude < 0.5f) return false;
        Vector3 delta = center - ray.origin;
        float projected = Vector3.Dot(delta, direction);
        if (projected <= 0f || delta.sqrMagnitude > maxRange * maxRange) return false;
        float perpendicularSquared = delta.sqrMagnitude - projected * projected;
        if (perpendicularSquared > radius * radius) return false;
        distance = Mathf.Max(0f, projected - Mathf.Sqrt(Mathf.Max(0f, radius * radius - perpendicularSquared)));
        return true;
    }

    private bool TryControllerRay(XRNode node, out Ray ray)
    {
        ray = default;
        if (trackingSpace == null) return false;
        var device = InputDevices.GetDeviceAtXRNode(node);
        bool tracked = device.isValid && (!device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool isTracked) || isTracked);
        if (tracked && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 position) &&
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rotation))
        {
            ray = new Ray(trackingSpace.TransformPoint(position), trackingSpace.TransformDirection(rotation * Vector3.forward));
            return true;
        }
#if ENABLE_INPUT_SYSTEM
        foreach (var input in InputSystem.devices)
        {
            bool matchingHand = false;
            foreach (var usage in input.usages)
                if (usage == (node == XRNode.LeftHand ? UnityEngine.InputSystem.CommonUsages.LeftHand : UnityEngine.InputSystem.CommonUsages.RightHand)) matchingHand = true;
            if (!matchingHand) continue;
            var trackedControl = input.TryGetChildControl<ButtonControl>("isTracked");
            if (trackedControl != null && !trackedControl.isPressed) continue;
            var pos = input.TryGetChildControl<Vector3Control>("devicePosition");
            var rot = input.TryGetChildControl<QuaternionControl>("deviceRotation");
            if (pos == null || rot == null) continue;
            ray = new Ray(trackingSpace.TransformPoint(pos.ReadValue()), trackingSpace.TransformDirection(rot.ReadValue() * Vector3.forward));
            return true;
        }
#endif
        return false;
    }

    private static LineRenderer MakeLine(string name, Transform parent, Material material, float width)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var line = obj.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.widthMultiplier = width;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCapVertices = 0;
        line.numCornerVertices = 0;
        return line;
    }

    private static Mesh BuildCharmMesh()
    {
        Vector3[] corners = { Vector3.up * 1.05f, Vector3.down * 1.05f, Vector3.left * .65f, Vector3.right * .65f, Vector3.forward * .65f, Vector3.back * .65f };
        int[] faces = {0,4,3, 0,3,5, 0,5,2, 0,2,4, 1,3,4, 1,5,3, 1,2,5, 1,4,2};
        var result = new Mesh { name = "Lingshan Eight Face Charm" };
        result.vertices = corners;
        result.triangles = faces;
        result.RecalculateNormals();
        result.RecalculateBounds();
        return result;
    }

    public void Dispose()
    {
        UnityEngine.Object.Destroy(root);
        UnityEngine.Object.Destroy(mesh);
    }
}
