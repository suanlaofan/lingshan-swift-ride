using System;

/// <summary>Distance windows deliberately do not award targets skipped between frames.</summary>
public readonly struct LingshanTargetWindow
{
    public readonly float StartDistance;
    public readonly float EndDistance;

    public LingshanTargetWindow(float startDistance, float endDistance)
    {
        StartDistance = startDistance;
        EndDistance = endDistance;
    }
}

/// <summary>A single gate sees both hands, and requires a release after menus or pauses.</summary>
public struct LingshanPressGate
{
    private bool armed;
    private bool wasPressed;

    public void RequireRelease()
    {
        armed = false;
        wasPressed = true;
    }

    public bool Sample(bool pressed)
    {
        if (!armed)
        {
            if (!pressed)
            {
                armed = true;
                wasPressed = false;
            }
            return false;
        }

        bool risingEdge = pressed && !wasPressed;
        wasPressed = pressed;
        return risingEdge;
    }
}

/// <summary>Pure round state: no Unity objects, frame-time dependence, or steady-state allocation.</summary>
public sealed class LingshanChallengeRound
{
    public const int PointsPerTarget = 100;
    private readonly LingshanTargetWindow[] windows;
    private LingshanPressGate triggerGate;
    private int nextTarget;

    public int CollectedCount { get; private set; }
    public int Score => CollectedCount * PointsPerTarget;
    public int TargetCount => windows.Length;
    public int NextTargetIndex => nextTarget;
    public bool IsComplete => nextTarget >= windows.Length;

    public LingshanChallengeRound(LingshanTargetWindow[] targetWindows)
    {
        if (targetWindows == null || targetWindows.Length == 0)
        {
            throw new ArgumentException("At least one target window is required.", nameof(targetWindows));
        }

        windows = (LingshanTargetWindow[])targetWindows.Clone();
        for (int index = 0; index < windows.Length; index++)
        {
            LingshanTargetWindow window = windows[index];
            if (float.IsNaN(window.StartDistance) || float.IsNaN(window.EndDistance) ||
                float.IsInfinity(window.StartDistance) || float.IsInfinity(window.EndDistance) ||
                window.StartDistance < 0f || window.EndDistance <= window.StartDistance ||
                (index > 0 && window.StartDistance <= windows[index - 1].EndDistance))
            {
                throw new ArgumentException("Target windows must be finite, positive-length, ordered and disjoint.", nameof(targetWindows));
            }
        }
        Reset();
    }

    public void Reset()
    {
        nextTarget = 0;
        CollectedCount = 0;
        triggerGate.RequireRelease();
    }

    public void RequireTriggerRelease()
    {
        triggerGate.RequireRelease();
    }

    public void Advance(float distance)
    {
        while (nextTarget < windows.Length && distance > windows[nextTarget].EndDistance)
        {
            nextTarget++;
        }
    }

    public bool TryGetActiveWindow(float distance, out LingshanTargetWindow window)
    {
        if (nextTarget < windows.Length)
        {
            window = windows[nextTarget];
            return distance >= window.StartDistance && distance <= window.EndDistance;
        }
        window = default;
        return false;
    }

    public bool Tick(float distance, bool eitherTriggerPressed, bool isPlaying, bool pressedHandAimedAtTarget)
    {
        if (!isPlaying)
        {
            triggerGate.RequireRelease();
            return false;
        }

        Advance(distance);
        bool edge = triggerGate.Sample(eitherTriggerPressed);
        if (!edge || !pressedHandAimedAtTarget || !TryGetActiveWindow(distance, out _))
        {
            return false;
        }

        CollectedCount++;
        nextTarget++;
        return true;
    }
}
