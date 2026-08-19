using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WhaleBreachSequence : MonoBehaviour
{
    private const int ArcLengthSamples = 128;

    [Header("Trigger")]
    [SerializeField] private RollerCoasterRide ride;
    [SerializeField, Range(0f, 1f)] private float sequenceStartProgress = 0.235f;

    [Header("Whale")]
    [SerializeField] private Transform animatedRoot;
    [SerializeField] private WhaleProceduralRig proceduralRig;
    [SerializeField] private Vector3 modelRotationOffset = new Vector3(0f, 90f, 0f);
    [SerializeField] private float bankAngle = 18f;

    [Header("Route")]
    [SerializeField] private List<Transform> routePoints = new List<Transform>();
    [SerializeField, Min(0.1f)] private float duration = 4.8f;
    [SerializeField] private AnimationCurve motionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private readonly float[] arcLengths = new float[ArcLengthSamples + 1];
    private float elapsed;
    private float previousRideProgress;
    private float totalArcLength;
    private bool hasPlayed;
    private bool isPlaying;

    public bool IsPlaying => isPlaying;

    public void PreviewAt(float normalizedDistance)
    {
        if (animatedRoot == null || !HasValidRoute())
        {
            return;
        }

        float previewTime = Mathf.Clamp01(normalizedDistance);
        RebuildArcLengthTable();
        animatedRoot.gameObject.SetActive(true);
        SetPose(NormalizedDistanceToCurveParameter(previewTime));
        proceduralRig?.ApplyPose(previewTime);
    }

    public void HidePreview()
    {
        if (!Application.isPlaying && animatedRoot != null)
        {
            proceduralRig?.ResetPose();
            animatedRoot.gameObject.SetActive(false);
        }
    }

    public void Configure(
        RollerCoasterRide newRide,
        Transform newAnimatedRoot,
        WhaleProceduralRig newProceduralRig,
        IReadOnlyList<Transform> newRoutePoints,
        float newSequenceStartProgress,
        float newDuration,
        Vector3 newModelRotationOffset)
    {
        ride = newRide;
        animatedRoot = newAnimatedRoot;
        proceduralRig = newProceduralRig;
        sequenceStartProgress = Mathf.Clamp01(newSequenceStartProgress);
        duration = Mathf.Max(0.1f, newDuration);
        modelRotationOffset = newModelRotationOffset;
        routePoints.Clear();
        for (int index = 0; index < newRoutePoints.Count; index++)
        {
            routePoints.Add(newRoutePoints[index]);
        }

        motionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        RebuildArcLengthTable();
        ResetSequence();
    }

    private void Awake()
    {
        RebuildArcLengthTable();
        ResetSequence();
    }

    private void OnEnable()
    {
        previousRideProgress = ride == null ? 0f : ride.NormalizedProgress;
    }

    private void Update()
    {
        if (ride == null || animatedRoot == null || !HasValidRoute())
        {
            return;
        }

        float rideProgress = ride.NormalizedProgress;
        if (rideProgress + 0.01f < previousRideProgress)
        {
            ResetSequence();
        }

        if (!hasPlayed
            && !isPlaying
            && ride.IsRunning
            && previousRideProgress < sequenceStartProgress
            && rideProgress >= sequenceStartProgress)
        {
            StartSequence();
        }

        if (isPlaying && ride.IsRunning)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float normalizedDistance = Mathf.Clamp01(motionCurve.Evaluate(normalizedTime));
            SetPose(NormalizedDistanceToCurveParameter(normalizedDistance));
            proceduralRig?.ApplyPose(normalizedTime);

            if (normalizedTime >= 1f)
            {
                isPlaying = false;
                proceduralRig?.ResetPose();
                animatedRoot.gameObject.SetActive(false);
            }
        }

        previousRideProgress = rideProgress;
    }

    private void StartSequence()
    {
        RebuildArcLengthTable();
        elapsed = 0f;
        hasPlayed = true;
        isPlaying = true;
        animatedRoot.gameObject.SetActive(true);
        SetPose(0f);
        proceduralRig?.ApplyPose(0f);
    }

    private void ResetSequence()
    {
        elapsed = 0f;
        hasPlayed = false;
        isPlaying = false;
        previousRideProgress = ride == null ? 0f : ride.NormalizedProgress;

        if (animatedRoot != null)
        {
            SetPose(0f);
            proceduralRig?.ResetPose();
            animatedRoot.gameObject.SetActive(false);
        }
    }

    private void SetPose(float curveParameter)
    {
        Vector3 position = EvaluateRoute(curveParameter);
        float tangentStep = 1f / ArcLengthSamples;
        Vector3 before = EvaluateRoute(Mathf.Max(0f, curveParameter - tangentStep));
        Vector3 after = EvaluateRoute(Mathf.Min(1f, curveParameter + tangentStep));
        Vector3 direction = after - before;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.forward;
        }

        float bank = Mathf.Sin(curveParameter * Mathf.PI) * bankAngle;
        Quaternion routeRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        animatedRoot.SetPositionAndRotation(
            position,
            routeRotation * Quaternion.Euler(0f, 0f, bank) * Quaternion.Euler(modelRotationOffset));
    }

    private Vector3 EvaluateRoute(float normalizedParameter)
    {
        int count = routePoints.Count;
        if (count == 0)
        {
            return transform.position;
        }
        if (count == 1)
        {
            return routePoints[0].position;
        }

        float scaled = Mathf.Clamp01(normalizedParameter) * (count - 1);
        int segment = Mathf.Min(Mathf.FloorToInt(scaled), count - 2);
        float t = scaled - segment;
        Vector3 p0 = routePoints[Mathf.Max(0, segment - 1)].position;
        Vector3 p1 = routePoints[segment].position;
        Vector3 p2 = routePoints[segment + 1].position;
        Vector3 p3 = routePoints[Mathf.Min(count - 1, segment + 2)].position;
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            2f * p1
            + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private void RebuildArcLengthTable()
    {
        Array.Clear(arcLengths, 0, arcLengths.Length);
        totalArcLength = 0f;
        if (!HasValidRoute())
        {
            return;
        }

        Vector3 previous = EvaluateRoute(0f);
        for (int index = 1; index <= ArcLengthSamples; index++)
        {
            Vector3 current = EvaluateRoute(index / (float)ArcLengthSamples);
            totalArcLength += Vector3.Distance(previous, current);
            arcLengths[index] = totalArcLength;
            previous = current;
        }
    }

    private float NormalizedDistanceToCurveParameter(float normalizedDistance)
    {
        if (totalArcLength <= 0.0001f)
        {
            return normalizedDistance;
        }

        float targetLength = Mathf.Clamp01(normalizedDistance) * totalArcLength;
        int upper = Array.BinarySearch(arcLengths, targetLength);
        if (upper < 0)
        {
            upper = ~upper;
        }

        upper = Mathf.Clamp(upper, 1, ArcLengthSamples);
        int lower = upper - 1;
        float segmentLength = arcLengths[upper] - arcLengths[lower];
        float segmentT = segmentLength > 0.0001f
            ? (targetLength - arcLengths[lower]) / segmentLength
            : 0f;
        return (lower + segmentT) / ArcLengthSamples;
    }

    private bool HasValidRoute()
    {
        if (routePoints == null || routePoints.Count < 2)
        {
            return false;
        }

        for (int index = 0; index < routePoints.Count; index++)
        {
            if (routePoints[index] == null)
            {
                return false;
            }
        }

        return true;
    }

    private void OnDrawGizmos()
    {
        if (!HasValidRoute())
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.74f, 0.08f, 1f);
        Vector3 previous = EvaluateRoute(0f);
        for (int index = 1; index <= 64; index++)
        {
            Vector3 current = EvaluateRoute(index / 64f);
            Gizmos.DrawLine(previous, current);
            previous = current;
        }

        Gizmos.color = new Color(0.1f, 0.9f, 0.95f, 1f);
        for (int index = 0; index < routePoints.Count; index++)
        {
            if (routePoints[index] != null)
            {
                Gizmos.DrawWireSphere(routePoints[index].position, 1.4f);
            }
        }
    }
}
