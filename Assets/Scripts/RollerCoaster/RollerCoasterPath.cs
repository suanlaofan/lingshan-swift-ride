using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RollerCoasterPath : MonoBehaviour
{
    [SerializeField] private List<Vector3> localPoints = new List<Vector3>();
    [SerializeField, Range(0f, 30f)] private float maximumBankAngle = 14f;

    private float[] cumulativeDistances = Array.Empty<float>();
    private Vector3[] localTangents = Array.Empty<Vector3>();
    private Vector3[] localUps = Array.Empty<Vector3>();

    public float Length
    {
        get
        {
            EnsureCache();
            return cumulativeDistances.Length == 0 ? 0f : cumulativeDistances[cumulativeDistances.Length - 1];
        }
    }

    public int PointCount => localPoints.Count;

    public void SetWorldPoints(IReadOnlyList<Vector3> worldPoints)
    {
        localPoints.Clear();
        for (int index = 0; index < worldPoints.Count; index++)
        {
            localPoints.Add(transform.InverseTransformPoint(worldPoints[index]));
        }

        RebuildCache();
    }

    public Vector3 GetWorldPoint(int index)
    {
        return transform.TransformPoint(localPoints[index]);
    }

    public void EvaluateDistance(float distance, out Vector3 position, out Vector3 tangent, out Vector3 up)
    {
        EnsureCache();
        if (localPoints.Count == 0)
        {
            position = transform.position;
            tangent = transform.forward;
            up = transform.up;
            return;
        }

        if (localPoints.Count == 1 || cumulativeDistances.Length < 2)
        {
            position = transform.TransformPoint(localPoints[0]);
            tangent = transform.forward;
            up = transform.up;
            return;
        }

        float clampedDistance = Mathf.Clamp(distance, 0f, cumulativeDistances[cumulativeDistances.Length - 1]);
        int upper = Array.BinarySearch(cumulativeDistances, clampedDistance);
        if (upper < 0)
        {
            upper = ~upper;
        }

        upper = Mathf.Clamp(upper, 1, localPoints.Count - 1);
        int lower = upper - 1;
        float segmentLength = cumulativeDistances[upper] - cumulativeDistances[lower];
        float t = segmentLength > 0.0001f
            ? (clampedDistance - cumulativeDistances[lower]) / segmentLength
            : 0f;

        Vector3 localPosition = Vector3.Lerp(localPoints[lower], localPoints[upper], t);
        Vector3 localTangent = Vector3.Slerp(localTangents[lower], localTangents[upper], t).normalized;
        Vector3 localUp = Vector3.Slerp(localUps[lower], localUps[upper], t).normalized;
        position = transform.TransformPoint(localPosition);
        tangent = transform.TransformDirection(localTangent).normalized;
        up = transform.TransformDirection(localUp).normalized;
    }

    private void OnEnable()
    {
        RebuildCache();
    }

    private void OnValidate()
    {
        RebuildCache();
    }

    private void EnsureCache()
    {
        if (cumulativeDistances.Length != localPoints.Count)
        {
            RebuildCache();
        }
    }

    private void RebuildCache()
    {
        int count = localPoints.Count;
        cumulativeDistances = new float[count];
        localTangents = new Vector3[count];
        localUps = new Vector3[count];
        if (count == 0)
        {
            return;
        }

        for (int index = 1; index < count; index++)
        {
            cumulativeDistances[index] = cumulativeDistances[index - 1]
                + Vector3.Distance(localPoints[index - 1], localPoints[index]);
        }

        for (int index = 0; index < count; index++)
        {
            int previous = Mathf.Max(0, index - 1);
            int next = Mathf.Min(count - 1, index + 1);
            Vector3 direction = localPoints[next] - localPoints[previous];
            localTangents[index] = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.forward;
        }

        Vector3 previousRight = Vector3.right;
        for (int index = 0; index < count; index++)
        {
            Vector3 right = Vector3.Cross(Vector3.up, localTangents[index]);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.ProjectOnPlane(previousRight, localTangents[index]);
            }
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.forward;
            }

            right.Normalize();
            Vector3 up = Vector3.Cross(localTangents[index], right).normalized;
            if (Vector3.Dot(up, Vector3.up) < 0f)
            {
                right = -right;
                up = -up;
            }

            previousRight = right;
            localUps[index] = up;
        }

        for (int index = 1; index < count - 1; index++)
        {
            float signedTurn = Vector3.SignedAngle(localTangents[index - 1], localTangents[index + 1], localUps[index]);
            float bank = Mathf.Clamp(-signedTurn * 0.55f, -maximumBankAngle, maximumBankAngle);
            localUps[index] = (Quaternion.AngleAxis(bank, localTangents[index]) * localUps[index]).normalized;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (localPoints.Count < 2)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.72f, 0.08f, 1f);
        Vector3 previous = transform.TransformPoint(localPoints[0]);
        for (int index = 1; index < localPoints.Count; index++)
        {
            Vector3 current = transform.TransformPoint(localPoints[index]);
            Gizmos.DrawLine(previous, current);
            previous = current;
        }
    }
}
