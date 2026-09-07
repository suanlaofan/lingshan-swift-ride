using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

/// <summary>Read-only runtime evidence. Does not choose refresh rate, quality or XR settings.</summary>
public sealed class LingshanRuntimeDiagnostics : MonoBehaviour
{
    private static LingshanRuntimeDiagnostics instance;
    private readonly float[] frameTimes = new float[1800];
    private readonly float[] sortedFrameTimes = new float[1800];
    private readonly FrameTiming[] timings = new FrameTiming[1];
    private int samples;
    private double elapsed;
    private bool configurationLogged;
    private RollerCoasterRide ride;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (instance != null) return;
        var host = new GameObject("Lingshan Runtime Diagnostics");
        instance = host.AddComponent<LingshanRuntimeDiagnostics>();
        DontDestroyOnLoad(host);
    }

    private void Update()
    {
        if (!configurationLogged)
        {
            LogConfiguration();
            ride = FindAnyObjectByType<RollerCoasterRide>();
            configurationLogged = true;
        }
        float seconds = Time.unscaledDeltaTime;
        if (seconds <= 0f) return;
        frameTimes[samples++] = seconds * 1000f;
        elapsed += seconds;
        bool timingAvailable = FrameTimingManager.IsFeatureEnabled();
        if (timingAvailable) FrameTimingManager.CaptureFrameTimings();
        if (elapsed < 5.0 && samples < frameTimes.Length) return;

        Array.Copy(frameTimes, sortedFrameTimes, samples);
        Array.Sort(sortedFrameTimes, 0, samples);
        bool hasLatest = timingAvailable && FrameTimingManager.GetLatestTimings(1, timings) > 0;
        var report = new FrameReport
        {
            realtimeSeconds = Time.realtimeSinceStartupAsDouble,
            samples = samples,
            applicationFocused = Application.isFocused,
            rideRunning = ride != null && ride.IsRunning,
            ridePaused = ride != null && ride.IsPaused,
            routeProgress = ride == null ? -1f : ride.NormalizedProgress,
            wearerPresence = DescribePresence(),
            leftController = DescribeController(XRNode.LeftHand),
            rightController = DescribeController(XRNode.RightHand),
            intervalSeconds = elapsed,
            averageFps = samples / elapsed,
            averageFrameMs = elapsed * 1000.0 / samples,
            p95FrameMs = sortedFrameTimes[Mathf.Clamp(Mathf.CeilToInt(samples * 0.95f) - 1, 0, samples - 1)],
            maximumFrameMs = sortedFrameTimes[samples - 1],
            latestCpuFrameMs = hasLatest && timings[0].cpuFrameTime > 0 ? timings[0].cpuFrameTime : -1,
            latestGpuFrameMs = hasLatest && timings[0].gpuFrameTime > 0 ? timings[0].gpuFrameTime : -1,
            xrActive = XRSettings.isDeviceActive,
            eyeWidth = XRSettings.eyeTextureWidth,
            eyeHeight = XRSettings.eyeTextureHeight
        };
        Debug.Log("LINGSHAN_FRAME_SAMPLE " + JsonUtility.ToJson(report));
        samples = 0;
        elapsed = 0;
    }

    private static string DescribeInputDevices()
    {
#if ENABLE_INPUT_SYSTEM
        var result = new System.Text.StringBuilder();
        foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
        {
            result.Append(device.name).Append("[");
            foreach (var usage in device.usages) result.Append(usage).Append(",");
            var tracked = device.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("isTracked");
            result.Append("] tracked=").Append(tracked == null ? "unknown" : tracked.isPressed.ToString()).Append("; ");
        }
        return result.ToString();
#else
        return "disabled";
#endif
    }

    private static string DescribePresence()
    {
        var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out bool present) ? present.ToString() : "unknown";
    }

    private void OnApplicationFocus(bool focused)
    {
        Debug.Log("LINGSHAN_APPLICATION_FOCUS " + focused);
    }

    private static string DescribeController(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        bool pose = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 position);
        bool rotation = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion orientation);
        bool hasTracked = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked);
        return device.name + " valid=" + device.isValid + " tracked=" + (hasTracked ? tracked.ToString() : "unknown") + " position=" + pose + " rotation=" + rotation;
    }

    private static void LogConfiguration()
    {
        var pipeline = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
        int quality = QualitySettings.GetQualityLevel();
        var report = new Configuration
        {
            productName = Application.productName,
            version = Application.version,
            packageName = Application.identifier,
            unityVersion = Application.unityVersion,
            deviceModel = SystemInfo.deviceModel,
            graphicsDevice = SystemInfo.graphicsDeviceName,
            graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            operatingSystem = SystemInfo.operatingSystem,
            developmentBuild = Debug.isDebugBuild,
            quality = QualitySettings.names[quality],
            pipeline = pipeline == null ? "none" : pipeline.name,
            hdr = pipeline != null && pipeline.supportsHDR,
            msaa = pipeline == null ? 0 : pipeline.msaaSampleCount,
            renderScale = pipeline == null ? 0 : pipeline.renderScale,
            xrActive = XRSettings.isDeviceActive,
            eyeWidth = XRSettings.eyeTextureWidth,
            eyeHeight = XRSettings.eyeTextureHeight,
            frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(),
            inputDevices = DescribeInputDevices(),
            targetFrameRate = Application.targetFrameRate
        };
        Debug.Log("LINGSHAN_RUNTIME_CONFIGURATION " + JsonUtility.ToJson(report));
    }

    private void OnApplicationPause(bool paused)
    {
        // Exclude a background/resume gap from the next interval.
        samples = 0;
        elapsed = 0;
        if (!paused) configurationLogged = false;
    }

    [Serializable] private sealed class Configuration
    {
        public string productName, version, packageName, unityVersion, deviceModel, graphicsDevice, graphicsApi, operatingSystem, quality, pipeline, inputDevices;
        public bool developmentBuild, hdr, xrActive, frameTimingEnabled;
        public int msaa, eyeWidth, eyeHeight, targetFrameRate;
        public float renderScale;
    }

    [Serializable] private sealed class FrameReport
    {
        public double realtimeSeconds, intervalSeconds, averageFps, averageFrameMs, latestCpuFrameMs, latestGpuFrameMs;
        public float p95FrameMs, maximumFrameMs;
        public int samples, eyeWidth, eyeHeight;
        public bool xrActive, applicationFocused, rideRunning, ridePaused;
        public float routeProgress;
        public string leftController, rightController, wearerPresence;
        public string measurement = "Application Update intervals; CPU/GPU are latest available samples, -1 means unavailable; not headset presentation FPS.";
    }
}
