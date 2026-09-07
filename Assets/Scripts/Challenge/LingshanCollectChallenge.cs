using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

/// <summary>
/// Twelve world-space charms require a tracked hand to aim and press its trigger.
/// Desktop camera aim is available for Editor validation only on XR platforms.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class LingshanCollectChallenge : MonoBehaviour
{
    // Increment the layout version if target windows, scoring or ride conditions change.
    public const string BestScorePlayerPrefsKey = "lingshan.collect.aim2.12x100.standard.best";
    public const int StandardTargetCount = 12;
    private const float ResumePreparationSeconds = 1.5f;
    private static readonly float[] WindowStarts =
    {
        0.04f, 0.10f, 0.16f, 0.31f, 0.37f, 0.43f,
        0.49f, 0.63f, 0.69f, 0.75f, 0.81f, 0.88f
    };

    public static LingshanCollectChallenge Instance { get; private set; }
    public int Score => round == null ? 0 : round.Score;
    public int CollectedCount => round == null ? 0 : round.CollectedCount;
    public int BestScore { get; private set; }
    public int TargetCount => round == null ? StandardTargetCount : round.TargetCount;
    public event Action ScoreChanged;

    private RollerCoasterRide ride;
    private LingshanChallengeRound round;
    private LingshanAimTargets aimTargets;
    private bool desktopTriggerHeld;
    public bool TryGetActiveTargetPosition(out Vector3 position)
    {
        position = Vector3.zero;
        return aimTargets != null && aimTargets.TryGetActivePosition(out position);
    }
    private LingshanPressGate pauseGate;
    private LingshanPressGate resumeGate;
    private bool focused = true;
    private bool systemPaused;
    private bool leftTriggerHeld;
    private bool rightTriggerHeld;
    private bool bestDirty;
    private float resumePreparation = -1f;
    private float feedbackUntil;
    private int lastPresentation = -1;
    private int lastCountdown = -1;
    private Canvas hudCanvas;
    private GameObject hudObject;
    private GameObject charmIcon;
    private GameObject windowBar;
    private RectTransform windowFill;
    private Text promptLabel;
    private Text hintLabel;
    private Text scoreLabel;

#if ENABLE_INPUT_SYSTEM
    private struct TriggerBinding
    {
        public AxisControl Axis;
        public ButtonControl Button;
        public bool Held;
        public bool Left;
        public bool Right;
    }
    private readonly List<TriggerBinding> triggerBindings = new List<TriggerBinding>(4);
    private readonly List<ButtonControl> pauseBindings = new List<ButtonControl>(2);
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterSceneLoad()
    {
        RollerCoasterRide activeRide = FindAnyObjectByType<RollerCoasterRide>();
        if (activeRide != null)
        {
            Install(activeRide);
        }
    }

    public static LingshanCollectChallenge Install(RollerCoasterRide activeRide)
    {
        if (activeRide == null)
        {
            return null;
        }

        LingshanCollectChallenge existing = Instance != null
            ? Instance : FindAnyObjectByType<LingshanCollectChallenge>();
        if (existing == null)
        {
            GameObject owner = new GameObject("Lingshan Collect Challenge");
            owner.transform.SetParent(activeRide.transform, false);
            existing = owner.AddComponent<LingshanCollectChallenge>();
        }
        existing.Bind(activeRide);
        return existing;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BestScore = Mathf.Clamp(PlayerPrefs.GetInt(BestScorePlayerPrefsKey, 0),
            0, StandardTargetCount * LingshanChallengeRound.PointsPerTarget);
        pauseGate.RequireRelease();
        resumeGate.RequireRelease();
    }

    private void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        RebuildInputBindings();
        InputSystem.onDeviceChange += HandleInputDeviceChange;
#endif
    }

    private void Start()
    {
        if (ride == null)
        {
            Bind(FindAnyObjectByType<RollerCoasterRide>());
        }
    }

    public void Bind(RollerCoasterRide activeRide)
    {
        if (activeRide == null || (ride == activeRide && round != null))
        {
            return;
        }
        UnsubscribeFromRide();
        ride = activeRide;
        ride.RunStarted += HandleRunStarted;
        ride.FinishedAtEnd += HandleFinished;
        ride.PauseChanged += HandlePauseChanged;

        if (ride.RouteLength <= 0.01f || ride.VehicleRoot == null)
        {
            Debug.LogError("Lingshan collect challenge requires a configured ride path and vehicle.", this);
            return;
        }

        var windows = CreateStandardWindows(ride.RouteLength);
        round = new LingshanChallengeRound(windows);
        aimTargets?.Dispose();
        aimTargets = new LingshanAimTargets(ride, windows);
        BuildHud();
        UpdateScoreLabel();
        if (ride.IsRunning)
        {
            HandleRunStarted(ride);
        }
    }

    public static LingshanTargetWindow[] CreateStandardWindows(float routeLength)
    {
        if (float.IsNaN(routeLength) || float.IsInfinity(routeLength) || routeLength <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(routeLength));
        }

        var windows = new LingshanTargetWindow[StandardTargetCount];
        // On the current approximately 1088 m route, each window spans 54.4 m:
        // at the configured maximum speed of 27 m/s this allows at least 2 seconds.
        // The 0.21-0.31 and 0.54-0.63 sections remain free of collection prompts.
        float width = routeLength * 0.05f;
        for (int index = 0; index < windows.Length; index++)
        {
            float start = WindowStarts[index] * routeLength;
            windows[index] = new LingshanTargetWindow(start, start + width);
        }
        return windows;
    }

    private void Update()
    {
        if (ride == null || round == null)
        {
            return;
        }

        ReadInputs(out bool triggerHeld, out bool pauseHeld);
        if (ride.IsRunning) round.Advance(ride.CurrentDistance);
        aimTargets.Update(ride.CurrentDistance, round.NextTargetIndex, ride.IsRunning || ride.IsPaused, ride.IsRunning);
        bool wearerPresent = IsWearerPresent();
        // Tracking is required to aim and collect, not to move the ride.
        // A controller arriving after Start must never trap the player at the station.
        bool canResume = focused && !systemPaused && wearerPresent;
        if (!canResume && ride.IsRunning)
        {
            ride.SetPaused(true);
        }

        bool pausePressed = pauseGate.Sample(pauseHeld);
        if (ride.IsRunning && pausePressed)
        {
            ride.SetPaused(true);
        }

        if (ride.IsPaused)
        {
            round.RequireTriggerRelease();
            SetHudVisible(true);
            if (!canResume)
            {
                resumeGate.RequireRelease();
                resumePreparation = -1f;
                ShowPresentation(3);
                return;
            }

            if (resumePreparation >= 0f)
            {
                ShowPresentation(4);
                resumePreparation -= Time.unscaledDeltaTime;
                int countdown = Mathf.Max(1, Mathf.CeilToInt(resumePreparation));
                if (countdown != lastCountdown)
                {
                    lastCountdown = countdown;
                    hintLabel.text = countdown == 2 ? "2" : "1";
                }
                if (resumePreparation <= 0f)
                {
                    ride.SetPaused(false);
                    round.RequireTriggerRelease();
                    resumeGate.RequireRelease();
                    resumePreparation = -1f;
                    lastPresentation = -1;
                }
            }
            else
            {
                ShowPresentation(3);
                if (resumeGate.Sample(triggerHeld))
                {
                    resumePreparation = ResumePreparationSeconds;
                    lastCountdown = -1;
                }
            }
            return;
        }

        if (!ride.IsRunning)
        {
            round.RequireTriggerRelease();
            pauseGate.RequireRelease();
            resumeGate.RequireRelease();
            SetHudVisible(false);
            return;
        }

        SetHudVisible(true);
        int candidateIndex = round.NextTargetIndex;
        if (round.Tick(ride.CurrentDistance, triggerHeld, true,
            aimTargets.PressedHandIsAimed(leftTriggerHeld, rightTriggerHeld, desktopTriggerHeld)))
        {
            feedbackUntil = Time.unscaledTime + 0.55f;
            UpdateBestScore();
            UpdateScoreLabel();
            ScoreChanged?.Invoke();
            SendCollectHaptics();
            Debug.Log("LINGSHAN_CHARM_COLLECTED target=" + (candidateIndex + 1) + " score=" + Score + " distance=" + ride.CurrentDistance);
            aimTargets.Update(ride.CurrentDistance, round.NextTargetIndex, true, true);
        }

        if (Time.unscaledTime < feedbackUntil)
        {
            ShowPresentation(2);
        }
        else if (round.TryGetActiveWindow(ride.CurrentDistance, out LingshanTargetWindow activeWindow))
        {
            ShowPresentation(aimTargets.IsAimed ? 6 : 1);
            float remaining = Mathf.Clamp01((activeWindow.EndDistance - ride.CurrentDistance) /
                (activeWindow.EndDistance - activeWindow.StartDistance));
            windowFill.localScale = new Vector3(remaining, 1f, 1f);
        }
        else
        {
            ShowPresentation(round.IsComplete ? 5 : 0);
        }
    }

    private void HandleRunStarted(RollerCoasterRide source)
    {
        if (source != ride || round == null)
        {
            return;
        }
        round.Reset();
        pauseGate.RequireRelease();
        resumeGate.RequireRelease();
        resumePreparation = -1f;
        feedbackUntil = 0f;
        lastPresentation = -1;
        UpdateScoreLabel();
        ScoreChanged?.Invoke();
    }

    private void HandleFinished(RollerCoasterRide source)
    {
        if (source != ride || round == null)
        {
            return;
        }
        round.Advance(ride.RouteLength + 1f);
        round.RequireTriggerRelease();
        UpdateBestScore();
        FlushBestScore();
        SetHudVisible(false);
        ScoreChanged?.Invoke();
    }

    private void HandlePauseChanged(RollerCoasterRide source)
    {
        round?.RequireTriggerRelease();
        resumeGate.RequireRelease();
        pauseGate.RequireRelease();
        resumePreparation = -1f;
        lastPresentation = -1;
        if (source.IsPaused)
        {
            FlushBestScore();
        }
    }

    private void UpdateBestScore()
    {
        if (Score > BestScore)
        {
            BestScore = Score;
            PlayerPrefs.SetInt(BestScorePlayerPrefsKey, BestScore);
            bestDirty = true;
        }
    }

    private void FlushBestScore()
    {
        if (bestDirty)
        {
            PlayerPrefs.Save();
            bestDirty = false;
        }
    }

    private static readonly bool IsPicoEmulator = SystemInfo.deviceModel.Replace('_', ' ').IndexOf("SDK Spaceos", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsWearerPresent()
    {
        // PICO Emulator 0.13 exposes userPresence=false although its XR session is focused.
        // It has no physical wear sensor. Keep the physical-headset check on actual devices.
        if (Application.isEditor || IsPicoEmulator) return true;
        UnityEngine.XR.InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        return !head.isValid ||
            !head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out bool present) || present;
    }

    private static bool ReadTrigger(UnityEngine.XR.InputDevice device, ref bool held)
    {
        if (!device.isValid)
        {
            held = false;
            return false;
        }
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float value))
        {
            if (value >= 0.65f) held = true;
            else if (value <= 0.2f) held = false;
        }
        else
        {
            held = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool pressed) && pressed;
        }
        return held;
    }

    private void ReadInputs(out bool triggerHeld, out bool pauseHeld)
    {
        UnityEngine.XR.InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        UnityEngine.XR.InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        bool leftHeld = ReadTrigger(left, ref leftTriggerHeld);
        bool rightHeld = ReadTrigger(right, ref rightTriggerHeld);
        desktopTriggerHeld = false;
        triggerHeld = leftHeld || rightHeld;
        pauseHeld = (left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool leftPause) && leftPause) ||
            (right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool rightPause) && rightPause);

#if ENABLE_INPUT_SYSTEM
        // Controls are discovered only when devices change, not by scanning paths every frame.
        for (int index = 0; index < triggerBindings.Count; index++)
        {
            TriggerBinding binding = triggerBindings[index];
            if (binding.Axis != null)
            {
                float value = binding.Axis.ReadValue();
                if (value >= 0.65f) binding.Held = true;
                else if (value <= 0.2f) binding.Held = false;
            }
            else
            {
                binding.Held = binding.Button != null && binding.Button.isPressed;
            }
            triggerBindings[index] = binding;
            triggerHeld |= binding.Held;
            if (binding.Left) leftHeld |= binding.Held;
            if (binding.Right) rightHeld |= binding.Held;
        }
        for (int index = 0; index < pauseBindings.Count; index++)
        {
            pauseHeld |= pauseBindings[index].isPressed;
        }
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            desktopTriggerHeld = keyboard.eKey.isPressed;
            pauseHeld |= keyboard.pKey.isPressed;
        }
        if (aimTargets != null && aimTargets.UsesDesktopAim && Mouse.current != null)
            desktopTriggerHeld |= Mouse.current.leftButton.isPressed;
        triggerHeld |= desktopTriggerHeld;
#endif
        leftTriggerHeld = leftHeld;
        rightTriggerHeld = rightHeld;
    }

#if ENABLE_INPUT_SYSTEM
    private void HandleInputDeviceChange(UnityEngine.InputSystem.InputDevice device, InputDeviceChange change)
    {
        RebuildInputBindings();
        round?.RequireTriggerRelease();
        pauseGate.RequireRelease();
        resumeGate.RequireRelease();
    }

    private void RebuildInputBindings()
    {
        triggerBindings.Clear();
        pauseBindings.Clear();
        foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
        {
            AxisControl axis = device.TryGetChildControl<AxisControl>("trigger");
            ButtonControl button = device.TryGetChildControl<ButtonControl>("triggerPressed") ??
                device.TryGetChildControl<ButtonControl>("triggerButton");
            if (axis != null || button != null)
            {
                bool isLeft = false, isRight = false;
                foreach (var usage in device.usages)
                {
                    isLeft |= usage == UnityEngine.InputSystem.CommonUsages.LeftHand;
                    isRight |= usage == UnityEngine.InputSystem.CommonUsages.RightHand;
                }
                triggerBindings.Add(new TriggerBinding { Axis = axis, Button = button, Left = isLeft, Right = isRight });
            }
            ButtonControl pause = device.TryGetChildControl<ButtonControl>("secondaryButton");
            if (pause != null)
            {
                pauseBindings.Add(pause);
            }
        }
    }
#endif

    private void SendCollectHaptics()
    {
        if (leftTriggerHeld)
        {
            InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).SendHapticImpulse(0, 0.2f, 0.06f);
        }
        if (rightTriggerHeld)
        {
            InputDevices.GetDeviceAtXRNode(XRNode.RightHand).SendHapticImpulse(0, 0.2f, 0.06f);
        }
    }

    private void BuildHud()
    {
        if (hudObject != null)
        {
            Destroy(hudObject);
        }
        Font font = Resources.Load<Font>("StartMenu/Fonts/RobotoMono");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        RectTransform root = MakeRect("Collect Prompt", ride.VehicleRoot, Vector2.zero, new Vector2(640f, 300f));
        hudObject = root.gameObject;
        root.localPosition = new Vector3(0f, 0.70f, 2.8f);
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one * 0.0016f;
        hudCanvas = hudObject.AddComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.WorldSpace;
        hudCanvas.worldCamera = Camera.main;
        hudCanvas.sortingOrder = 20;
        Image background = root.gameObject.AddComponent<Image>();
        background.color = new Color(0.015f, 0.025f, 0.04f, 0.88f);
        background.raycastTarget = false;

        RectTransform icon = MakeRect("Gold Charm", root, new Vector2(0f, 101f), new Vector2(30f, 30f));
        icon.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Image iconImage = icon.gameObject.AddComponent<Image>();
        iconImage.color = new Color(1f, 0.73f, 0.16f, 1f);
        iconImage.raycastTarget = false;
        charmIcon = icon.gameObject;

        promptLabel = MakeLabel("Collect State", root, font, 42, new Vector2(0f, 46f), new Vector2(610f, 65f));
        hintLabel = MakeLabel("Collect Hint", root, font, 25, new Vector2(0f, -16f), new Vector2(610f, 70f));
        scoreLabel = MakeLabel("Collect Score", root, font, 29, new Vector2(0f, -113f), new Vector2(610f, 48f));
        scoreLabel.color = new Color(0.8f, 0.86f, 0.92f, 1f);

        RectTransform bar = MakeRect("Window Remaining", root, new Vector2(0f, -68f), new Vector2(380f, 8f));
        Image barBackground = bar.gameObject.AddComponent<Image>();
        barBackground.color = new Color(0.24f, 0.28f, 0.31f, 1f);
        barBackground.raycastTarget = false;
        windowBar = bar.gameObject;
        windowFill = MakeRect("Remaining Fill", bar, new Vector2(-190f, 0f), new Vector2(380f, 8f));
        windowFill.pivot = new Vector2(0f, 0.5f);
        Image fill = windowFill.gameObject.AddComponent<Image>();
        fill.color = new Color(1f, 0.73f, 0.16f, 1f);
        fill.raycastTarget = false;
        // A passive canvas has no raycaster or EventSystem and cannot steal menu input.
        SetHudVisible(false);
    }

    private static RectTransform MakeRect(string objectName, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private static Text MakeLabel(string objectName, Transform parent, Font font, int size, Vector2 position, Vector2 dimensions)
    {
        Text label = MakeRect(objectName, parent, position, dimensions).gameObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = size;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        label.supportRichText = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
    }

    private void UpdateScoreLabel()
    {
        if (scoreLabel != null)
        {
            scoreLabel.text = "SCORE " + Score + "   " + CollectedCount + "/" + TargetCount + "   BEST " + BestScore;
        }
    }

    private void SetHudVisible(bool visible)
    {
        if (hudCanvas != null && hudCanvas.enabled != visible)
        {
            hudCanvas.enabled = visible;
        }
    }

    private void ShowPresentation(int state)
    {
        if (state == lastPresentation || promptLabel == null)
        {
            return;
        }
        lastPresentation = state;
        charmIcon.SetActive(state == 1 || state == 6);
        windowBar.SetActive(state == 1 || state == 6);
        promptLabel.color = state == 1 ? new Color(1f, 0.78f, 0.25f, 1f) : Color.white;
        switch (state)
        {
            case 1:
                promptLabel.text = "AIM AT THE CHARM";
                hintLabel.text = "POINT YOUR CONTROLLER\nTHEN PULL TRIGGER";
                break;
            case 6:
                promptLabel.text = "TARGET LOCKED";
                promptLabel.color = new Color(0.45f, 1f, 0.65f, 1f);
                hintLabel.text = "PULL THIS HAND'S TRIGGER\nTO COLLECT";
                break;
            case 2:
                promptLabel.text = "+100";
                promptLabel.color = new Color(0.45f, 1f, 0.65f, 1f);
                hintLabel.text = "CHARM COLLECTED";
                break;
            case 3:
                promptLabel.text = "PAUSED";
                hintLabel.text = "RELEASE, THEN PULL TRIGGER\nTO CONTINUE";
                break;
            case 4:
                promptLabel.text = "GET READY";
                hintLabel.text = "2";
                break;
            case 5:
                promptLabel.text = "ENJOY THE VIEW";
                hintLabel.text = "RESULTS AT THE STATION";
                break;
            default:
                promptLabel.text = "LOOK FOR GOLD CHARMS";
                hintLabel.text = "AIM, THEN PULL TRIGGER\nB / Y TO PAUSE";
                break;
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        focused = hasFocus;
        if (!hasFocus)
        {
            ride?.SetPaused(true);
            FlushBestScore();
        }
    }

    private void OnApplicationPause(bool paused)
    {
        systemPaused = paused;
        if (paused)
        {
            ride?.SetPaused(true);
            FlushBestScore();
        }
    }

    private void UnsubscribeFromRide()
    {
        if (ride != null)
        {
            ride.RunStarted -= HandleRunStarted;
            ride.FinishedAtEnd -= HandleFinished;
            ride.PauseChanged -= HandlePauseChanged;
        }
    }

    private void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        InputSystem.onDeviceChange -= HandleInputDeviceChange;
#endif
        FlushBestScore();
    }

    private void OnDestroy()
    {
        aimTargets?.Dispose();
        UnsubscribeFromRide();
        FlushBestScore();
        if (hudObject != null)
        {
            Destroy(hudObject);
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
