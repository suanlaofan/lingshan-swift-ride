using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class RollerCoasterRide : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RollerCoasterPath path;
    [SerializeField] private Transform vehicleRoot;

    [Header("Ride")]
    [SerializeField] private bool autoStart = true;
    [SerializeField] private float startDelay = 1.5f;
    [SerializeField] private float liftSpeed = 6.5f;
    [SerializeField] private float cruiseSpeed = 13f;
    [SerializeField] private float maximumSpeed = 27f;
    [SerializeField] private float acceleration = 7f;
    [SerializeField] private float vehicleHeight = 0.62f;
    [SerializeField] private bool stopAtEnd = true;

    private float distance;
    private float speed;
    private float currentSlope;
    private float countdown;
    private bool running;
    private bool finished;
    private bool heldForStartMenu;
    private bool paused;
    private bool hasStarted;
    private bool applicationFocused = true;
    private bool applicationPaused;

    public float NormalizedProgress => path == null || path.Length <= 0f ? 0f : distance / path.Length;
    public float CurrentDistance => distance;
    public float CurrentSpeed => speed;
    public float CurrentSlope => currentSlope;
    public bool IsRunning => running;
    public bool IsFinished => finished;
    public bool IsHeldForStartMenu => heldForStartMenu;
    public bool IsPaused => paused;
    public float RouteLength => path == null ? 0f : path.Length;
    public Transform VehicleRoot => vehicleRoot;

    public void EvaluateRouteDistance(float routeDistance, out Vector3 position, out Vector3 tangent, out Vector3 up)
    {
        path.EvaluateDistance(routeDistance, out position, out tangent, out up);
    }

    public event System.Action<RollerCoasterRide> FinishedAtEnd;
    public event System.Action<RollerCoasterRide> RunStarted;
    public event System.Action<RollerCoasterRide> PauseChanged;

    public void Configure(RollerCoasterPath newPath, Transform newVehicleRoot)
    {
        path = newPath;
        vehicleRoot = newVehicleRoot;
        ResetRide();
    }

    private void Awake()
    {
        ResetRide();
    }

    private void Update()
    {
        if (!applicationFocused || applicationPaused)
        {
            return;
        }
        HandleKeyboard();
        if (path == null || vehicleRoot == null || path.Length <= 0.01f)
        {
            return;
        }

        if (heldForStartMenu || paused)
        {
            return;
        }

        if (!running && !finished && autoStart)
        {
            countdown -= Time.deltaTime;
            if (countdown <= 0f)
            {
                BeginRun();
            }
        }

        path.EvaluateDistance(distance, out Vector3 position, out Vector3 tangent, out Vector3 up);
        if (running)
        {
            float downhill = Mathf.Clamp01(-tangent.y * 2.2f);
            float uphill = Mathf.Clamp01(tangent.y * 2.6f);
            float targetSpeed = Mathf.Lerp(cruiseSpeed, maximumSpeed, downhill);
            targetSpeed = Mathf.Lerp(targetSpeed, liftSpeed, uphill);
            speed = Mathf.MoveTowards(speed, targetSpeed, acceleration * Time.deltaTime);
            distance += speed * Time.deltaTime;

            if (distance >= path.Length)
            {
                distance = path.Length;
                speed = 0f;
                running = false;
                if (stopAtEnd)
                {
                    finished = true;
                    paused = false;
                    FinishedAtEnd?.Invoke(this);
                }
                else
                {
                    distance = 0f;
                    countdown = startDelay;
                    hasStarted = false;
                }
            }

            path.EvaluateDistance(distance, out position, out tangent, out up);
        }

        currentSlope = tangent.y;
        Quaternion targetRotation = Quaternion.LookRotation(tangent, up);
        vehicleRoot.SetPositionAndRotation(position + up * vehicleHeight, targetRotation);
    }

    public void ResetRide()
    {
        distance = 0f;
        speed = 0f;
        currentSlope = 0f;
        countdown = startDelay;
        running = false;
        finished = false;
        paused = false;
        hasStarted = false;

        if (path != null && vehicleRoot != null && path.Length > 0.01f)
        {
            path.EvaluateDistance(0f, out Vector3 position, out Vector3 tangent, out Vector3 up);
            currentSlope = tangent.y;
            vehicleRoot.SetPositionAndRotation(
                position + up * vehicleHeight,
                Quaternion.LookRotation(tangent, up));
        }
    }

    public void RestartRide()
    {
        heldForStartMenu = false;
        autoStart = true;
        ResetRide();
    }

    public void HoldForStartMenu()
    {
        heldForStartMenu = true;
        autoStart = false;
        ResetRide();
    }

    public void StartRideFromMenu()
    {
        ResetRide();
        heldForStartMenu = false;
        autoStart = false;
        countdown = 0f;
        BeginRun();
    }

    public void HoldAtFinishedEnd()
    {
        heldForStartMenu = true;
        autoStart = false;
        countdown = 0f;
        speed = 0f;
        running = false;
        finished = true;
        paused = false;
    }

    public void TogglePause()
    {
        if (heldForStartMenu)
        {
            return;
        }

        if (finished || !hasStarted)
        {
            ResetRide();
            autoStart = false;
            BeginRun();
            return;
        }
        SetPaused(!paused);
    }

    public void SetPaused(bool shouldPause)
    {
        if (heldForStartMenu || finished || !hasStarted || paused == shouldPause)
        {
            return;
        }
        if (!shouldPause && (!applicationFocused || applicationPaused))
        {
            return;
        }

        paused = shouldPause;
        running = !shouldPause;
        autoStart = false;
        PauseChanged?.Invoke(this);
    }

    private void BeginRun()
    {
        paused = false;
        running = true;
        if (!hasStarted)
        {
            hasStarted = true;
            RunStarted?.Invoke(this);
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        applicationFocused = hasFocus;
        if (!hasFocus)
        {
            SetPaused(true);
        }
    }

    private void OnApplicationPause(bool isApplicationPaused)
    {
        applicationPaused = isApplicationPaused;
        if (isApplicationPaused)
        {
            SetPaused(true);
        }
    }

    private void HandleKeyboard()
    {
#if ENABLE_INPUT_SYSTEM
        if (heldForStartMenu)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.rKey.wasPressedThisFrame)
        {
            RestartRide();
        }
        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            TogglePause();
        }
#endif
    }
}
