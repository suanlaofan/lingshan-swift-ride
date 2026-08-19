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

    public float NormalizedProgress => path == null || path.Length <= 0f ? 0f : distance / path.Length;
    public float CurrentDistance => distance;
    public float CurrentSpeed => speed;
    public float CurrentSlope => currentSlope;
    public bool IsRunning => running;
    public bool IsFinished => finished;
    public bool IsHeldForStartMenu => heldForStartMenu;
    public Transform VehicleRoot => vehicleRoot;

    public event System.Action<RollerCoasterRide> FinishedAtEnd;

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
        HandleKeyboard();
        if (path == null || vehicleRoot == null || path.Length <= 0.01f)
        {
            return;
        }

        if (heldForStartMenu)
        {
            return;
        }

        if (!running && !finished && autoStart)
        {
            countdown -= Time.deltaTime;
            if (countdown <= 0f)
            {
                running = true;
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
                    FinishedAtEnd?.Invoke(this);
                }
                else
                {
                    distance = 0f;
                    countdown = startDelay;
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
        running = true;
    }

    public void HoldAtFinishedEnd()
    {
        heldForStartMenu = true;
        autoStart = false;
        countdown = 0f;
        speed = 0f;
        running = false;
        finished = true;
    }

    public void TogglePause()
    {
        if (heldForStartMenu)
        {
            return;
        }

        if (finished)
        {
            ResetRide();
        }
        running = !running;
        autoStart = false;
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
