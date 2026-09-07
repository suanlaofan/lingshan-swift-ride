#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class LingshanChallengeValidation
{
    [MenuItem("Tools/Lingshan/Validate Collect Challenge")]
    public static void Validate()
    {
        int passed = 0;
        VerifyMenuReleaseAndLongHold(); passed++;
        VerifyDoubleHandGate(); passed++;
        VerifySkippedWindows(); passed++;
        VerifyPauseAndResumeRelease(); passed++;
        VerifyWindowBoundaries(); passed++;
        VerifyReset(); passed++;
        VerifyFullRound(); passed++;
        VerifyInvalidLayouts(); passed++;
        VerifyActiveAimRequired(); passed++;
        VerifyAimGeometry(); passed++;
        VerifyRideLifecycle(); passed++;
        Debug.Log("LINGSHAN_CHALLENGE_VALIDATION_PASS cases=" + passed +
            " targets=12 maximumScore=1200 bestKey=" + LingshanCollectChallenge.BestScorePlayerPrefsKey);
    }

    private static LingshanChallengeRound ThreeWindows()
    {
        return new LingshanChallengeRound(new[]
        {
            new LingshanTargetWindow(10f, 20f),
            new LingshanTargetWindow(30f, 40f),
            new LingshanTargetWindow(50f, 60f)
        });
    }

    private static void VerifyMenuReleaseAndLongHold()
    {
        LingshanChallengeRound round = ThreeWindows();
        Check(!round.Tick(10f, true, true, true), "Held menu confirmation must not collect.");
        Check(!round.Tick(12f, true, true, true), "Continued menu hold must remain blocked.");
        Check(!round.Tick(13f, false, true, true), "Release must not collect.");
        Check(round.Tick(14f, true, true, true), "Fresh press within the window must collect.");
        Check(!round.Tick(15f, true, true, true), "A target must not score twice.");
        Check(!round.Tick(31f, true, true, true), "Holding into the next window must not collect.");
        round.Tick(32f, false, true, true);
        Check(round.Tick(33f, true, true, true), "Second target requires a new press.");
        Check(round.Score == 200 && round.CollectedCount == 2, "Two distinct collections must total 200.");
    }

    private static void VerifyDoubleHandGate()
    {
        var gate = new LingshanPressGate();
        gate.RequireRelease();
        gate.Sample(false);
        bool left = true;
        bool right = true;
        Check(gate.Sample(left || right), "Simultaneous hands must generate one edge.");
        left = false;
        Check(!gate.Sample(left || right), "Releasing only one hand must not re-arm.");
        left = true;
        Check(!gate.Sample(left || right), "Pressing the other hand while one is held must not generate another edge.");
        left = right = false;
        Check(!gate.Sample(left || right), "Releasing both hands must not generate an edge.");
        right = true;
        Check(gate.Sample(left || right), "A new press after both release must generate one edge.");
    }

    private static void VerifySkippedWindows()
    {
        LingshanChallengeRound round = ThreeWindows();
        round.Tick(0f, false, true, true);
        Check(!round.Tick(45f, true, true, true), "A low-frame-rate jump must not award windows already missed.");
        Check(round.NextTargetIndex == 2 && round.Score == 0, "All crossed windows must advance exactly once.");
        round.Tick(51f, false, true, true);
        Check(round.Tick(52f, true, true, true), "The remaining active window must still be collectable.");
        Check(round.Score == 100 && round.IsComplete, "Skipped windows must remain unscored.");
    }

    private static void VerifyPauseAndResumeRelease()
    {
        LingshanChallengeRound round = ThreeWindows();
        round.Tick(12f, false, true, true);
        Check(!round.Tick(999f, true, false, true), "Paused frames cannot collect.");
        Check(round.NextTargetIndex == 0, "Paused frames cannot expire windows.");
        Check(!round.Tick(13f, true, true, true), "Held resume trigger must not collect.");
        round.Tick(14f, false, true, true);
        Check(round.Tick(15f, true, true, true), "A fresh press after resume release must collect.");
    }

    private static void VerifyWindowBoundaries()
    {
        LingshanChallengeRound atStart = ThreeWindows();
        atStart.Tick(0f, false, true, true);
        Check(atStart.Tick(10f, true, true, true), "Start boundary must be inclusive.");

        LingshanChallengeRound atEnd = ThreeWindows();
        atEnd.Tick(0f, false, true, true);
        Check(atEnd.Tick(20f, true, true, true), "End boundary must be inclusive.");

        LingshanChallengeRound afterEnd = ThreeWindows();
        afterEnd.Tick(0f, false, true, true);
        Check(!afterEnd.Tick(20.001f, true, true, true), "After the end boundary must miss.");

        LingshanChallengeRound earlyHold = ThreeWindows();
        earlyHold.Tick(0f, false, true, true);
        Check(!earlyHold.Tick(9f, true, true, true), "An early press must not collect.");
        Check(!earlyHold.Tick(10f, true, true, true), "Holding an early press into the window must not collect.");
    }

    private static void VerifyReset()
    {
        LingshanChallengeRound round = ThreeWindows();
        round.Tick(0f, false, true, true);
        round.Tick(12f, true, true, true);
        round.Advance(100f);
        round.Reset();
        Check(round.Score == 0 && round.CollectedCount == 0 && round.NextTargetIndex == 0,
            "AGAIN reset must clear all round state.");
        Check(!round.Tick(12f, true, true, true), "AGAIN confirmation must require release.");
        round.Tick(13f, false, true, true);
        Check(round.Tick(14f, true, true, true), "Targets must become collectable on a new round.");
    }

    private static void VerifyFullRound()
    {
        LingshanTargetWindow[] windows = LingshanCollectChallenge.CreateStandardWindows(1088.26f);
        var round = new LingshanChallengeRound(windows);
        Check(windows.Length == 12, "Standard layout must contain exactly 12 targets.");
        for (int index = 0; index < windows.Length; index++)
        {
            LingshanTargetWindow window = windows[index];
            Check((window.EndDistance - window.StartDistance) / 27f >= 2f,
                "Current route windows must allow at least two seconds at maximum configured speed.");
            round.Tick(window.StartDistance - 0.01f, false, true, true);
            Check(round.Tick(window.StartDistance, true, true, true), "Every standard target must be reachable once.");
        }
        Check(round.Score == 1200 && round.CollectedCount == 12 && round.IsComplete,
            "A complete perfect collection run must total 1200.");
        round.Tick(2000f, false, true, true);
        Check(!round.Tick(2000f, true, true, true) && round.Score == 1200,
            "Score cannot grow beyond 1200.");
        Check(LingshanCollectChallenge.BestScorePlayerPrefsKey.Contains("aim2.12x100"),
            "Best-score storage must identify the scoring/layout version.");
    }

    private static void VerifyInvalidLayouts()
    {
        bool rejectedOverlap = false;
        try
        {
            new LingshanChallengeRound(new[]
            {
                new LingshanTargetWindow(1f, 3f),
                new LingshanTargetWindow(2f, 4f)
            });
        }
        catch (ArgumentException)
        {
            rejectedOverlap = true;
        }
        Check(rejectedOverlap, "Overlapping targets would make scoring ambiguous and must be rejected.");
    }

    private static void VerifyActiveAimRequired()
    {
        var round = ThreeWindows();
        round.Tick(10f, false, true, false);
        Check(!round.Tick(12f, true, true, false), "An unaimed trigger press must never collect.");
        Check(!round.Tick(13f, true, true, true), "Holding trigger then sweeping onto a target must not collect.");
        round.Tick(14f, false, true, true);
        Check(round.Tick(15f, true, true, true), "An aimed fresh trigger press must collect.");
        round.Reset();
        for (int distance = 0; distance < 100; distance++) round.Tick(distance, false, true, true);
        Check(round.Score == 0, "Riding through all targets without a press must score zero.");
    }

    private static void VerifyAimGeometry()
    {
        var ray = new Ray(Vector3.zero, Vector3.forward);
        Check(LingshanAimTargets.RayHitsTarget(ray, Vector3.forward * 20f, 1.35f, 55f, out _), "Forward target should be aimable.");
        Check(!LingshanAimTargets.RayHitsTarget(ray, new Vector3(3f, 0f, 20f), 1.35f, 55f, out _), "Off-target aim must miss.");
        Check(!LingshanAimTargets.RayHitsTarget(ray, Vector3.back * 20f, 1.35f, 55f, out _), "Targets behind the controller must miss.");
        Check(!LingshanAimTargets.RayHitsTarget(ray, Vector3.forward * 60f, 1.35f, 55f, out _), "Targets beyond collection range must miss.");
    }

    private static void VerifyRideLifecycle()
    {
        GameObject root = new GameObject("Lingshan Validation Temporary Ride")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        try
        {
            var path = root.AddComponent<RollerCoasterPath>();
            path.SetWorldPoints(new[] { Vector3.zero, Vector3.forward * 100f });
            GameObject vehicle = new GameObject("Validation Vehicle");
            vehicle.transform.SetParent(root.transform, false);
            var ride = root.AddComponent<RollerCoasterRide>();
            ride.Configure(path, vehicle.transform);
            int started = 0;
            ride.RunStarted += _ => started++;
            ride.StartRideFromMenu();
            Check(started == 1 && ride.IsRunning && !ride.IsPaused, "Starting must emit one new-run event.");
            ride.SetPaused(true);
            Check(ride.IsPaused && !ride.IsRunning, "Pause must stop ride progression.");
            ride.SetPaused(false);
            Check(started == 1 && ride.IsRunning && !ride.IsPaused,
                "Resume must not reset the score by emitting a new-run event.");
            ride.StartRideFromMenu();
            Check(started == 2 && ride.CurrentDistance == 0f, "AGAIN must start exactly one new round.");
            ride.HoldForStartMenu();
            ride.SetPaused(false);
            Check(!ride.IsRunning && ride.IsHeldForStartMenu, "Resume cannot bypass the start menu.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Lingshan collect validation failed: " + message);
        }
    }
}
#endif
