#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

/// <summary>Explicit Editor-only integration test with temporary tracked controllers; never included in APK.</summary>
public static class LingshanPlayModeValidation
{
    private static bool testing;
    private static Action sampleInput;
    private static readonly FieldInfo Distance = typeof(RollerCoasterRide).GetField("distance", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo Acceleration = typeof(RollerCoasterRide).GetField("acceleration", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo Speed = typeof(RollerCoasterRide).GetField("speed", BindingFlags.NonPublic | BindingFlags.Instance);

    [MenuItem("Tools/Lingshan/Validate Play Mode Three Rounds")]
    public static async void Run()
    {
        if (testing || !EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first and run only one validation.");
        testing = true;
        Directory.CreateDirectory("Captures/LingshanOneDay");
        File.WriteAllText(Path.GetFullPath("Captures/LingshanOneDay/validation.txt"), "RUNNING " + DateTime.UtcNow.ToString("O"));
        Keyboard keyboard = null;
        UnityEngine.InputSystem.InputDevice left = null, right = null;
        Action updateAim = null;
        RollerCoasterLookController look = null;
        bool lookWasEnabled = false;
        RollerCoasterRide testedRide = null;
        float savedAcceleration = 0f;
        Quaternion lookRotation = Quaternion.identity;
        bool previousBackground = Application.runInBackground;
        bool hadBest = PlayerPrefs.HasKey(LingshanCollectChallenge.BestScorePlayerPrefsKey);
        int previousBest = PlayerPrefs.GetInt(LingshanCollectChallenge.BestScorePlayerPrefsKey, 0);
        try
        {
            Application.runInBackground = true;
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.RegisterLayout("{\"name\":\"LingshanValidationController\",\"extend\":\"XRController\",\"controls\":[{\"name\":\"trigger\",\"layout\":\"Axis\",\"format\":\"FLT\"}]}");
            left = InputSystem.AddDevice("LingshanValidationController");
            right = InputSystem.AddDevice("LingshanValidationController");
            InputSystem.SetDeviceUsage(left, UnityEngine.InputSystem.CommonUsages.LeftHand);
            InputSystem.SetDeviceUsage(right, UnityEngine.InputSystem.CommonUsages.RightHand);
            SetTracked(left);
            SetTracked(right);
            var ride = UnityEngine.Object.FindAnyObjectByType<RollerCoasterRide>();
            testedRide = ride;
            savedAcceleration = (float)Acceleration.GetValue(ride);
            Acceleration.SetValue(ride, 0f); // Hold each inspection pose exactly, including slow Editor frames.
            var challenge = LingshanCollectChallenge.Instance;
            bool aimLeft = false, aimRight = true, rightPressed = false;
            updateAim = () =>
            {
                if (ride != null && ride.IsRunning) Speed.SetValue(ride, 0f);
                if (challenge != null)
                {
                    bool hasTarget = challenge.TryGetActiveTargetPosition(out Vector3 target);
                    if (!hasTarget) target = Camera.main.transform.position + Camera.main.transform.forward * 15f;
                    PointController(left, target, hasTarget && aimLeft, -0.2f, false);
                    PointController(right, target, hasTarget && aimRight, 0.2f, rightPressed);
                }
            };
            InputSystem.onAfterUpdate += updateAim;
            sampleInput = updateAim;
            Debug.Log("LINGSHAN_TEST_RUN_STARTED playerLoopContext=" + (System.Threading.SynchronizationContext.Current != null));
            Check(ride != null && challenge != null, "Ride and challenge installed");
            await WaitUntil(() => FindButton("Start") != null, 15);
            await Delay(1.2);
            Check(Camera.main.cullingMask == (1 << 5) && Camera.main.clearFlags == CameraClearFlags.SolidColor && Camera.main.backgroundColor == Color.black, "Start menu hides the surrounding world in black");
            await Capture("01-start");
            FindButton("Start").onClick.Invoke();
            Check(ride.IsRunning, "Start must begin riding synchronously without confirmation");
            Check(Camera.main.cullingMask != (1 << 5), "Start immediately restores gameplay rendering");
            await WaitUntil(() => ride.IsRunning, 15);
            look = UnityEngine.Object.FindAnyObjectByType<RollerCoasterLookController>();
            if (look != null)
            {
                lookWasEnabled = look.enabled;
                lookRotation = look.transform.localRotation;
                look.enabled = false;
                look.transform.localRotation = Quaternion.identity;
            }
            var windows = LingshanCollectChallenge.CreateStandardWindows(ride.RouteLength);
            for (int run = 0; run < 3; run++)
            {
                Check(challenge.Score == 0 && challenge.CollectedCount == 0, "Round starts at zero");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                await Frames(3);
                for (int i = 0; i < windows.Length; i++)
                {
                    Distance.SetValue(ride, (windows[i].StartDistance + windows[i].EndDistance) * 0.5f);
                    Speed.SetValue(ride, 0f);
                    await Frames(3);
                    if (run == 0) await Capture("02-charm-" + (i + 1).ToString("00"));
                    if (i == 0)
                    {
                        aimLeft = true; aimRight = false;
                        await Frames(3);
                        rightPressed = true; updateAim();
                        await Frames(3);
                        Check(challenge.Score == 0, "Other hand trigger must not collect an aimed charm");
                        aimRight = true;
                        await Frames(3);
                        Check(challenge.Score == 0, "Holding trigger then aiming must not collect");
                        rightPressed = false; updateAim();
                        aimLeft = false;
                        await Frames(3);
                    }
                    var aiming = (LingshanAimTargets)typeof(LingshanCollectChallenge).GetField("aimTargets", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(challenge);
                    await WaitUntil(() => aiming.PressedHandIsAimed(false, true, false), 10);
                    rightPressed = true; updateAim();
                    await Frames(3);
                    Check(challenge.Score == (i + 1) * 100, "Trigger awards exactly one charm: run=" + run + " target=" + (i + 1) + " score=" + challenge.Score + " distance=" + ride.CurrentDistance + " paused=" + ride.IsPaused);
                    await Frames(3);
                    Check(challenge.Score == (i + 1) * 100, "Held trigger does not repeat");
                    rightPressed = false; updateAim();
                    await Frames(3);
                    if (run == 0 && i == 0)
                    {
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.P));
                        await WaitUntil(() => ride.IsPaused, 5);
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        await Frames(3);
                        float stopped = ride.CurrentDistance;
                        await Frames(5);
                        Check(Mathf.Abs(stopped - ride.CurrentDistance) < 0.001f, "Pause freezes vehicle");
                        await Capture("03-paused");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
                        await Frames(3);
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                        await WaitUntil(() => ride.IsRunning, 8);
                        Check(challenge.Score == 100, "Resume does not award a charm");
                    }
                }
                Check(challenge.Score == 1200 && challenge.CollectedCount == 12, "Perfect round bounded at 1200");
                Distance.SetValue(ride, ride.RouteLength);
                Speed.SetValue(ride, 1f);
                await WaitUntil(() => ride.IsFinished && FindButton("Again") != null, 15);
                await Delay(1.2);
                Check(challenge.Score == 1200 && challenge.BestScore == 1200, "Results and best retain final score");
                Check(UnityEngine.Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.name == "Run Score" && t.text.Contains("1200")), "Result UI displays score");
                Check(Camera.main.cullingMask == (1 << 5) && Camera.main.backgroundColor == Color.black, "Results hide the surrounding world in black");
                if (run == 0) await Capture("04-results");
                Debug.Log("LINGSHAN_PLAY_ROUND_PASS round=" + (run + 1));
                if (run < 2)
                {
                    FindButton("Again").onClick.Invoke();
                    Check(ride.IsRunning && Camera.main.cullingMask != (1 << 5), "Again immediately restores movement and gameplay rendering");
                    await WaitUntil(() => ride.IsRunning && !ride.IsFinished, 15);
                    if (look != null) { look.enabled = false; look.transform.localRotation = Quaternion.identity; }
                    Check(challenge.BestScore == 1200, "Again retains personal best");
                }
            }
            File.WriteAllText(Path.GetFullPath("Captures/LingshanOneDay/validation.txt"), "PASS rounds=3 activeCollection=36 blackMenus=true immediateStart=true immediateAgain=true " + DateTime.UtcNow.ToString("O"));
            Debug.Log("LINGSHAN_ACTIVE_COLLECTION_PLAYMODE_PASS rounds=3 input=synthetic-tracked-controllers deviceVerified=false");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.GetFullPath("Captures/LingshanOneDay/validation.txt"), "FAILED\n" + ex);
            Debug.LogError("LINGSHAN_PLAYMODE_VALIDATION_FAIL " + ex);
        }
        finally
        {
            if (look != null) { look.transform.localRotation = lookRotation; look.enabled = lookWasEnabled; }
            if (updateAim != null) InputSystem.onAfterUpdate -= updateAim;
            if (left != null && left.added) InputSystem.RemoveDevice(left);
            if (right != null && right.added) InputSystem.RemoveDevice(right);
            InputSystem.RemoveLayout("LingshanValidationController");
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            Application.runInBackground = previousBackground;
            if (hadBest) PlayerPrefs.SetInt(LingshanCollectChallenge.BestScorePlayerPrefsKey, previousBest);
            else PlayerPrefs.DeleteKey(LingshanCollectChallenge.BestScorePlayerPrefsKey);
            PlayerPrefs.Save();
            if (testedRide != null) Acceleration.SetValue(testedRide, savedAcceleration);
            sampleInput = null;
            testing = false;
        }
    }

    private static void PointController(UnityEngine.InputSystem.InputDevice controller, Vector3 target, bool aimed, float side, bool pressed)
    {
        var camera = Camera.main;
        Transform tracking = camera.transform.parent;
        Vector3 localPosition = camera.transform.localPosition + new Vector3(side, -0.1f, 0.12f);
        Vector3 worldPosition = tracking.TransformPoint(localPosition);
        Quaternion rotation = Quaternion.Inverse(tracking.rotation) * Quaternion.LookRotation(aimed ? target - worldPosition : camera.transform.up);
        using (StateEvent.From(controller, out var eventPtr))
        {
            controller.GetChildControl<ButtonControl>("isTracked").WriteValueIntoEvent(1f, eventPtr);
            controller.GetChildControl<Vector3Control>("devicePosition").WriteValueIntoEvent(localPosition, eventPtr);
            controller.GetChildControl<QuaternionControl>("deviceRotation").WriteValueIntoEvent(rotation, eventPtr);
            controller.GetChildControl<AxisControl>("trigger").WriteValueIntoEvent(pressed ? 1f : 0f, eventPtr);
            InputState.Change(controller, eventPtr, InputUpdateType.Dynamic);
        }
    }

    private static void SetTracked(UnityEngine.InputSystem.InputDevice device)
    {
        using (StateEvent.From(device, out var eventPtr))
        {
            device.GetChildControl<ButtonControl>("isTracked").WriteValueIntoEvent(1f, eventPtr);
            InputSystem.QueueEvent(eventPtr);
        }
    }

    private static Button FindButton(string name) => UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == name + " Option" && b.gameObject.activeInHierarchy);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Frames(int count)
    {
        int frame = Time.frameCount;
        await WaitUntil(() => Time.frameCount - frame >= count, 20);
    }
    private static async Task Delay(double seconds)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        await WaitUntil(() => EditorApplication.timeSinceStartup >= end, seconds + 20);
    }
    private static async Task WaitUntil(Func<bool> condition, double timeout)
    {
        double end = EditorApplication.timeSinceStartup + timeout;
        while (!condition())
        {
            sampleInput?.Invoke();
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > end)
                throw new TimeoutException("Play Mode integration wait timed out.");
            await Task.Delay(20);
        }
    }
    private static async Task Capture(string name)
    {
        string directory = Path.GetFullPath("Captures/LingshanOneDay");
        Directory.CreateDirectory(directory);
        ScreenCapture.CaptureScreenshot(Path.Combine(directory, name + ".png"));
        await Frames(2);
    }
}
#endif
