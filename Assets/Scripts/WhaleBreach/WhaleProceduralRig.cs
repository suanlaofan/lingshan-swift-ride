using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WhaleProceduralRig : MonoBehaviour
{
    [SerializeField] private Transform[] spineBones = Array.Empty<Transform>();
    [SerializeField] private Quaternion[] neutralLocalRotations = Array.Empty<Quaternion>();

    public int BoneCount => spineBones == null ? 0 : spineBones.Length;

    public void Configure(IReadOnlyList<Transform> newSpineBones)
    {
        if (newSpineBones == null)
        {
            spineBones = Array.Empty<Transform>();
            neutralLocalRotations = Array.Empty<Quaternion>();
            return;
        }

        spineBones = new Transform[newSpineBones.Count];
        neutralLocalRotations = new Quaternion[newSpineBones.Count];
        for (int index = 0; index < newSpineBones.Count; index++)
        {
            spineBones[index] = newSpineBones[index];
            neutralLocalRotations[index] = newSpineBones[index] == null
                ? Quaternion.identity
                : newSpineBones[index].localRotation;
        }
    }

    public void ApplyPose(float normalizedTime)
    {
        if (!HasValidRig())
        {
            return;
        }

        float time = Mathf.Clamp01(normalizedTime);
        float activity = Smooth01(Mathf.InverseLerp(0f, 0.12f, time))
            * (1f - Smooth01(Mathf.InverseLerp(0.88f, 1f, time)));
        float breachArch = Pulse(time, 0.02f, 0.48f, 0.72f);
        float diveArch = Pulse(time, 0.58f, 0.78f, 1f);
        float tailKick = Pulse(time, 0.54f, 0.74f, 0.94f);

        int lastBone = Mathf.Max(1, spineBones.Length - 1);
        for (int index = 0; index < spineBones.Length; index++)
        {
            float spinePosition = index / (float)lastBone;
            float bodyProfile = Mathf.Sin(spinePosition * Mathf.PI);
            float archAngle = (breachArch * 4.8f - diveArch * 5.6f) * bodyProfile;

            // The phase travels from the head toward the tail while the tail receives
            // more amplitude, producing visible follow-through without softening a bone piece.
            float wavePhase = (time * 1.55f - spinePosition * 0.82f) * Mathf.PI * 2f;
            float waveAmplitude = Mathf.Lerp(1.2f, 6.8f, spinePosition) * activity;
            float waveAngle = Mathf.Sin(wavePhase) * waveAmplitude;
            float tailAngle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 1f, spinePosition))
                * tailKick
                * 5.5f;

            spineBones[index].localRotation = neutralLocalRotations[index]
                * Quaternion.AngleAxis(archAngle + waveAngle + tailAngle, Vector3.right);
        }
    }

    public void ResetPose()
    {
        if (!HasValidRig())
        {
            return;
        }

        for (int index = 0; index < spineBones.Length; index++)
        {
            spineBones[index].localRotation = neutralLocalRotations[index];
        }
    }

    private bool HasValidRig()
    {
        if (spineBones == null
            || neutralLocalRotations == null
            || spineBones.Length == 0
            || spineBones.Length != neutralLocalRotations.Length)
        {
            return false;
        }

        for (int index = 0; index < spineBones.Length; index++)
        {
            if (spineBones[index] == null)
            {
                return false;
            }
        }

        return true;
    }

    private static float Pulse(float value, float start, float peak, float end)
    {
        if (value <= start || value >= end)
        {
            return 0f;
        }

        if (value < peak)
        {
            return Smooth01(Mathf.InverseLerp(start, peak, value));
        }

        return 1f - Smooth01(Mathf.InverseLerp(peak, end, value));
    }

    private static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }
}
