using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class RollerCoasterBuilder
{
    private const string RootName = "RollerCoaster_Placeholder";
    private const string ControlPointRootName = "Route_ControlPoints_Edit_Then_Rebuild";
    private const string GeneratedTrackRootName = "Track_Placeholder_ReplaceMe";
    private const string GeneratedFolder = "Assets/Art/RollerCoaster/Generated";
    private const string RailMeshPath = GeneratedFolder + "/PlaceholderRails.asset";
    private const string TieMeshPath = GeneratedFolder + "/PlaceholderTies.asset";
    private const string SupportMeshPath = GeneratedFolder + "/PlaceholderSupports.asset";
    private const string TrackMaterialPath = GeneratedFolder + "/TrackMetal.mat";
    private const string TieMaterialPath = GeneratedFolder + "/TieWood.mat";
    private const string SupportMaterialPath = GeneratedFolder + "/SupportMetal.mat";
    private const string CartMaterialPath = GeneratedFolder + "/CartRed.mat";
    private const string AccentMaterialPath = GeneratedFolder + "/CartAccent.mat";
    private const string StoneGroundNamePrefix = "tripo_convert_e30cd860-dbb0-49b0-9aae-90b25ea39573";
    private const float TrackGroundClearance = 1.12f;
    private const float TrackFootprintRadius = 1.55f;
    private const float TrackModelClearance = 0.5f;
    private const float MaximumAutomaticModelLift = 2f;
    private const float ModelAvoidanceMargin = 2.15f;
    private const float ModelAvoidanceTransition = 12f;
    private const float MaximumAvoidedModelSize = 64f;
    private const float MinimumCrossingClearance = 5.2f;
    private const float CrossingTransitionLength = 22f;

    private readonly struct RouteAnchor
    {
        public RouteAnchor(float x, float z, float elevation, string label)
        {
            X = x;
            Z = z;
            Elevation = elevation;
            Label = label;
        }

        public float X { get; }
        public float Z { get; }
        public float Elevation { get; }
        public string Label { get; }
    }

    private sealed class RouteFrames
    {
        public Vector3[] Tangents;
        public Vector3[] Rights;
        public Vector3[] Ups;
        public float[] Distances;
    }

    [MenuItem("Tools/Environment/Build Placeholder Roller Coaster")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        Terrain terrain = FindTerrain(scene);
        if (terrain == null)
        {
            Debug.LogError("Build the roller coaster from EnvironmentScene, where Wilderness Terrain is present.");
            return;
        }

        EnsureFolder(GeneratedFolder);
        DestroySceneObject(scene, RootName);
        List<Terrain> sceneTerrains = GetSceneTerrains(scene);

        GameObject root = new GameObject(RootName);
        Transform controls = new GameObject(ControlPointRootName).transform;
        controls.SetParent(root.transform, false);

        List<Transform> roadPieces = GetStoneGroundPieces(scene);
        Vector3 start = FindRoadPoint(roadPieces, new Vector2(31f, 9f), terrain);
        Vector3 end = FindRoadPoint(roadPieces, new Vector2(30f, -9f), terrain);
        List<RouteAnchor> anchors = CreateDefaultAnchors(start, end, sceneTerrains);
        for (int index = 0; index < anchors.Count; index++)
        {
            RouteAnchor anchor = anchors[index];
            Vector3 position;
            if (index == 0)
            {
                position = start;
            }
            else if (index == anchors.Count - 1)
            {
                position = end;
            }
            else
            {
                float terrainHeight = SampleHighestTerrainHeight(sceneTerrains, anchor.X, anchor.Z, terrain);
                position = new Vector3(anchor.X, terrainHeight + TrackGroundClearance + anchor.Elevation, anchor.Z);
            }

            GameObject point = new GameObject($"CP_{index:00}_{anchor.Label}");
            point.transform.SetParent(controls, false);
            point.transform.position = position;
            Texture2D icon = EditorGUIUtility.IconContent(index == 0 || index == anchors.Count - 1
                ? "sv_label_1"
                : "sv_label_6").image as Texture2D;
            EditorGUIUtility.SetIconForObject(point, icon);
        }

        BuildFromControlPoints(root, sceneTerrains, terrain);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("Placeholder roller coaster built. Enter Play mode for the first-person ride. Space pauses; R restarts.");
    }

    [MenuItem("Tools/Environment/Rebuild Roller Coaster From Control Points")]
    public static void RebuildFromControlPoints()
    {
        Scene scene = SceneManager.GetActiveScene();
        Terrain terrain = FindTerrain(scene);
        GameObject root = FindSceneObject(scene, RootName);
        if (terrain == null || root == null)
        {
            Debug.LogError("The terrain or existing roller coaster root could not be found.");
            return;
        }

        BuildFromControlPoints(root, GetSceneTerrains(scene), terrain);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("Roller coaster rebuilt from the edited control points.");
    }

    [MenuItem("Tools/Environment/Select Roller Coaster Control Points")]
    public static void SelectControlPoints()
    {
        GameObject root = FindSceneObject(SceneManager.GetActiveScene(), RootName);
        Transform controls = root == null ? null : root.transform.Find(ControlPointRootName);
        if (controls == null)
        {
            Debug.LogWarning("Build the placeholder roller coaster first.");
            return;
        }

        Selection.objects = controls.Cast<Transform>().Select(item => item.gameObject).ToArray();
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    private static void BuildFromControlPoints(GameObject root, List<Terrain> terrains, Terrain fallbackTerrain)
    {
        Transform controlRoot = root.transform.Find(ControlPointRootName);
        if (controlRoot == null || controlRoot.childCount < 4)
        {
            Debug.LogError("At least four route control points are required.");
            return;
        }

        List<Vector3> controlPoints = controlRoot.Cast<Transform>()
            .OrderBy(point => point.GetSiblingIndex())
            .Select(point => point.position)
            .ToList();
        List<Vector3> route = BuildSmoothRoute(controlPoints, terrains, fallbackTerrain, 14);

        Transform previousTrack = root.transform.Find(GeneratedTrackRootName);
        if (previousTrack != null)
        {
            UnityEngine.Object.DestroyImmediate(previousTrack.gameObject);
        }

        Transform previousVehicle = root.transform.Find("RideVehicle");
        if (previousVehicle != null)
        {
            UnityEngine.Object.DestroyImmediate(previousVehicle.gameObject);
        }

        Transform previousStations = root.transform.Find("StationMarkers");
        if (previousStations != null)
        {
            UnityEngine.Object.DestroyImmediate(previousStations.gameObject);
        }

        RollerCoasterPath path = root.GetComponentInChildren<RollerCoasterPath>();
        if (path == null)
        {
            GameObject pathObject = new GameObject("RollerCoaster_Path_KeepWhenReplacingModels");
            pathObject.transform.SetParent(root.transform, false);
            path = pathObject.AddComponent<RollerCoasterPath>();
        }
        path.SetWorldPoints(route);

        Material trackMaterial = CreateOrUpdateMaterial(
            TrackMaterialPath,
            "Track Metal",
            new Color(0.13f, 0.16f, 0.18f, 1f),
            0.72f,
            0.48f);
        Material tieMaterial = CreateOrUpdateMaterial(
            TieMaterialPath,
            "Tie Wood",
            new Color(0.19f, 0.09f, 0.045f, 1f),
            0f,
            0.2f);
        Material supportMaterial = CreateOrUpdateMaterial(
            SupportMaterialPath,
            "Support Metal",
            new Color(0.075f, 0.09f, 0.10f, 1f),
            0.65f,
            0.38f);
        Material cartMaterial = CreateOrUpdateMaterial(
            CartMaterialPath,
            "Cart Red",
            new Color(0.55f, 0.045f, 0.035f, 1f),
            0.25f,
            0.42f);
        Material accentMaterial = CreateOrUpdateMaterial(
            AccentMaterialPath,
            "Cart Accent",
            new Color(0.88f, 0.62f, 0.12f, 1f),
            0.55f,
            0.5f);

        CreateTrackVisuals(root.transform, route, terrains, fallbackTerrain, trackMaterial, tieMaterial, supportMaterial);
        Transform vehicle = CreatePlaceholderVehicle(root.transform, cartMaterial, trackMaterial, accentMaterial);
        CreateStationMarkers(root.transform, route, trackMaterial, accentMaterial);

        RollerCoasterRide ride = root.GetComponent<RollerCoasterRide>();
        if (ride == null)
        {
            ride = root.AddComponent<RollerCoasterRide>();
        }
        ride.Configure(path, vehicle);
        RollerCoasterAudioInstaller.ConfigureForBuilder(root, ride, vehicle);
        EditorUtility.SetDirty(path);
        EditorUtility.SetDirty(ride);
    }

    private static List<RouteAnchor> CreateDefaultAnchors(
        Vector3 start,
        Vector3 end,
        List<Terrain> terrains)
    {
        Terrain mountainTerrain = terrains
            .Where(candidate => candidate.name != "Wilderness Terrain")
            .OrderByDescending(candidate => candidate.terrainData.size.y)
            .FirstOrDefault();
        Vector3 peakWorld = mountainTerrain == null
            ? new Vector3(-150f, 30f, -20f)
            : FindTerrainPeak(mountainTerrain);
        Vector2 peak = new Vector2(peakWorld.x, peakWorld.z);
        Vector2 town = new Vector2(8f, 28f);
        Vector2 towardPeak = peak - town;
        if (towardPeak.sqrMagnitude < 1f)
        {
            towardPeak = Vector2.left;
        }
        towardPeak.Normalize();
        Vector2 climbSide = new Vector2(-towardPeak.y, towardPeak.x);
        Vector2 approach = new Vector2(-10f, 50f);
        Vector2 baseOne = Vector2.Lerp(approach, peak, 0.26f) + climbSide * 24f;
        Vector2 baseTwo = Vector2.Lerp(town, peak, 0.43f) + climbSide * 38f;
        Vector2 liftOne = Vector2.Lerp(town, peak, 0.59f) + climbSide * 44f;
        Vector2 liftTwo = Vector2.Lerp(town, peak, 0.74f) + climbSide * 34f;
        Vector2 summitApproach = Vector2.Lerp(town, peak, 0.9f) + climbSide * 18f;
        Vector2 summitExit = peak - climbSide * 20f;
        Vector2 dropOne = Vector2.Lerp(peak, new Vector2(end.x, end.z), 0.23f) - climbSide * 42f;
        Vector2 dropTwo = Vector2.Lerp(peak, new Vector2(end.x, end.z), 0.46f) - climbSide * 31f;
        Vector2 dropThree = Vector2.Lerp(peak, new Vector2(end.x, end.z), 0.68f) - climbSide * 19f;
        Vector2 returnRun = Vector2.Lerp(peak, new Vector2(end.x, end.z), 0.84f) - climbSide * 8f;

        return new List<RouteAnchor>
        {
            new RouteAnchor(start.x, start.z, 0f, "Start_StoneRoad"),
            new RouteAnchor(43f, 14f, 0.15f, "Leave_Town"),
            new RouteAnchor(57f, 21f, 0.35f, "Outer_Rim_01"),
            new RouteAnchor(68f, 34f, 0.55f, "Outer_Rim_02"),
            new RouteAnchor(69f, 49f, 0.8f, "Outer_Rim_03"),
            new RouteAnchor(60f, 60f, 1.2f, "Outer_Rim_04"),
            new RouteAnchor(45f, 65f, 1.1f, "Upper_Ridge_01"),
            new RouteAnchor(27f, 62f, 0.7f, "Upper_Ridge_02"),
            new RouteAnchor(8f, 56f, 0.45f, "Upper_Ridge_03"),
            new RouteAnchor(-10f, 50f, 0.3f, "Mountain_Approach"),
            new RouteAnchor(baseOne.x, baseOne.y, 0.45f, "Mountain_Base_01"),
            new RouteAnchor(baseTwo.x, baseTwo.y, 0.65f, "Mountain_Base_02"),
            new RouteAnchor(liftOne.x, liftOne.y, 0.9f, "Lift_Hill_01"),
            new RouteAnchor(liftTwo.x, liftTwo.y, 1.25f, "Lift_Hill_02"),
            new RouteAnchor(summitApproach.x, summitApproach.y, 1.8f, "Mountain_Summit_Approach"),
            new RouteAnchor(peak.x, peak.y, 2.4f, "Mountain_Summit"),
            new RouteAnchor(summitExit.x, summitExit.y, 1.5f, "Drop_Entry"),
            new RouteAnchor(dropOne.x, dropOne.y, 0.9f, "Main_Drop_01"),
            new RouteAnchor(dropTwo.x, dropTwo.y, 0.55f, "Main_Drop_02"),
            new RouteAnchor(dropThree.x, dropThree.y, 0.35f, "Main_Drop_03"),
            new RouteAnchor(returnRun.x, returnRun.y, 0.2f, "Return_To_Town"),
            new RouteAnchor(17f, -13f, 0.1f, "Brake_Run"),
            new RouteAnchor(end.x, end.z, 0f, "End_StoneRoad")
        };
    }

    private static List<Vector3> BuildSmoothRoute(
        IReadOnlyList<Vector3> controls,
        List<Terrain> terrains,
        Terrain fallbackTerrain,
        int samplesPerSegment)
    {
        List<Renderer> surfaceRenderers = GetTrackSurfaceRenderers(fallbackTerrain.gameObject.scene);
        var result = new List<Vector3>((controls.Count - 1) * samplesPerSegment + 1);
        for (int segment = 0; segment < controls.Count - 1; segment++)
        {
            Vector3 p0 = controls[Mathf.Max(0, segment - 1)];
            Vector3 p1 = controls[segment];
            Vector3 p2 = controls[segment + 1];
            Vector3 p3 = controls[Mathf.Min(controls.Count - 1, segment + 2)];
            for (int sample = 0; sample < samplesPerSegment; sample++)
            {
                float t = sample / (float)samplesPerSegment;
                Vector3 point = CatmullRom(p0, p1, p2, p3, t);
                point.y = Mathf.Max(
                    point.y,
                    SampleTrackClearanceHeight(point, terrains, fallbackTerrain, surfaceRenderers));
                result.Add(point);
            }
        }
        result.Add(controls[controls.Count - 1]);

        for (int pass = 0; pass < 2; pass++)
        {
            float[] heights = result.Select(point => point.y).ToArray();
            for (int index = 1; index < result.Count - 1; index++)
            {
                Vector3 point = result[index];
                point.y = (heights[index - 1] + heights[index] * 2f + heights[index + 1]) * 0.25f;
                point.y = Mathf.Max(
                    point.y,
                    SampleTrackClearanceHeight(point, terrains, fallbackTerrain, surfaceRenderers));
                result[index] = point;
            }
        }

        ResolveHorizontalModelIntersections(result, surfaceRenderers);
        ResolveTrackSelfIntersections(result);

        // Recheck the full footprint after smoothing so neighboring high ground cannot
        // push the outer rail or the ends of a cross tie into the surface.
        for (int index = 0; index < result.Count; index++)
        {
            Vector3 point = result[index];
            point.y = Mathf.Max(
                point.y,
                SampleTrackClearanceHeight(point, terrains, fallbackTerrain, surfaceRenderers));
            result[index] = point;
        }

        return result;
    }

    private static void ResolveTrackSelfIntersections(List<Vector3> points)
    {
        if (points.Count < 20)
        {
            return;
        }

        float[] distances = new float[points.Count];
        float[] lifts = new float[points.Count];
        for (int index = 1; index < points.Count; index++)
        {
            distances[index] = distances[index - 1] + Vector3.Distance(points[index - 1], points[index]);
        }

        var resolvedLocations = new List<Vector2>();
        int crossingCount = 0;
        for (int first = 1; first < points.Count - 2; first++)
        {
            for (int second = first + 8; second < points.Count - 2; second++)
            {
                if (!TryIntersectSegmentsXZ(
                    points[first],
                    points[first + 1],
                    points[second],
                    points[second + 1],
                    out float firstT,
                    out float secondT,
                    out Vector2 intersection))
                {
                    continue;
                }

                if (resolvedLocations.Any(location => Vector2.SqrMagnitude(location - intersection) < 16f))
                {
                    continue;
                }

                float firstHeight = Mathf.Lerp(points[first].y, points[first + 1].y, firstT);
                float secondHeight = Mathf.Lerp(points[second].y, points[second + 1].y, secondT);
                float heightDifference = Mathf.Abs(firstHeight - secondHeight);
                if (heightDifference >= MinimumCrossingClearance)
                {
                    continue;
                }

                int raisedSegment = firstHeight >= secondHeight ? first : second;
                float raisedHeight = Mathf.Max(firstHeight, secondHeight);
                float lowerHeight = Mathf.Min(firstHeight, secondHeight);
                if (IsNearStation(raisedSegment, points.Count))
                {
                    raisedSegment = raisedSegment == first ? second : first;
                    raisedHeight = raisedSegment == first ? firstHeight : secondHeight;
                    lowerHeight = raisedSegment == first ? secondHeight : firstHeight;
                }
                if (IsNearStation(raisedSegment, points.Count))
                {
                    continue;
                }

                float requiredLift = lowerHeight + MinimumCrossingClearance - raisedHeight;
                if (requiredLift <= 0f)
                {
                    requiredLift = heightDifference + MinimumCrossingClearance;
                }

                float segmentT = raisedSegment == first ? firstT : secondT;
                float centerDistance = Mathf.Lerp(
                    distances[raisedSegment],
                    distances[raisedSegment + 1],
                    segmentT);
                for (int index = 2; index < points.Count - 2; index++)
                {
                    float distanceFromCrossing = Mathf.Abs(distances[index] - centerDistance);
                    if (distanceFromCrossing >= CrossingTransitionLength)
                    {
                        continue;
                    }

                    float t = distanceFromCrossing / CrossingTransitionLength;
                    float weight = 0.5f + 0.5f * Mathf.Cos(t * Mathf.PI);
                    lifts[index] = Mathf.Max(lifts[index], requiredLift * weight);
                }

                resolvedLocations.Add(intersection);
                crossingCount++;
            }
        }

        for (int index = 0; index < points.Count; index++)
        {
            if (lifts[index] <= 0f)
            {
                continue;
            }

            Vector3 point = points[index];
            point.y += lifts[index];
            points[index] = point;
        }

        if (crossingCount > 0)
        {
            Debug.Log($"Raised {crossingCount} roller coaster crossing(s) to maintain track clearance.");
        }
    }

    private static bool IsNearStation(int segment, int pointCount)
    {
        return segment < 10 || segment > pointCount - 12;
    }

    private static bool TryIntersectSegmentsXZ(
        Vector3 firstStart,
        Vector3 firstEnd,
        Vector3 secondStart,
        Vector3 secondEnd,
        out float firstT,
        out float secondT,
        out Vector2 intersection)
    {
        Vector2 p = new Vector2(firstStart.x, firstStart.z);
        Vector2 r = new Vector2(firstEnd.x - firstStart.x, firstEnd.z - firstStart.z);
        Vector2 q = new Vector2(secondStart.x, secondStart.z);
        Vector2 s = new Vector2(secondEnd.x - secondStart.x, secondEnd.z - secondStart.z);
        float denominator = Cross2D(r, s);
        if (Mathf.Abs(denominator) < 0.0001f)
        {
            firstT = 0f;
            secondT = 0f;
            intersection = Vector2.zero;
            return false;
        }

        Vector2 offset = q - p;
        firstT = Cross2D(offset, s) / denominator;
        secondT = Cross2D(offset, r) / denominator;
        if (firstT <= 0.02f || firstT >= 0.98f || secondT <= 0.02f || secondT >= 0.98f)
        {
            intersection = Vector2.zero;
            return false;
        }

        intersection = p + r * firstT;
        return true;
    }

    private static float Cross2D(Vector2 first, Vector2 second)
    {
        return first.x * second.y - first.y * second.x;
    }

    private static void ResolveHorizontalModelIntersections(
        List<Vector3> points,
        IReadOnlyList<Renderer> surfaceRenderers)
    {
        if (points.Count < 7)
        {
            return;
        }

        float[] distances = new float[points.Count];
        for (int index = 1; index < points.Count; index++)
        {
            distances[index] = distances[index - 1] + Vector3.Distance(points[index - 1], points[index]);
        }

        List<Renderer> obstacles = surfaceRenderers
            .Where(IsModelObstacle)
            .OrderBy(renderer => renderer.name, StringComparer.Ordinal)
            .ThenBy(renderer => renderer.transform.GetSiblingIndex())
            .ToList();

        for (int pass = 0; pass < 2; pass++)
        {
            for (int obstacleIndex = 0; obstacleIndex < obstacles.Count; obstacleIndex++)
            {
                Bounds bounds = obstacles[obstacleIndex].bounds;
                int closestIndex = FindClosestIntersectingPoint(points, bounds);
                if (closestIndex < 0)
                {
                    continue;
                }

                Vector2 tangent = new Vector2(
                    points[closestIndex + 1].x - points[closestIndex - 1].x,
                    points[closestIndex + 1].z - points[closestIndex - 1].z).normalized;
                if (tangent.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                Vector2 right = new Vector2(tangent.y, -tangent.x);
                Vector2 pathPosition = new Vector2(points[closestIndex].x, points[closestIndex].z);
                Vector2 obstacleCenter = new Vector2(bounds.center.x, bounds.center.z);
                float obstacleSide = Vector2.Dot(obstacleCenter - pathPosition, right);
                float sideSign = Mathf.Abs(obstacleSide) < 0.05f
                    ? ((obstacleIndex & 1) == 0 ? 1f : -1f)
                    : -Mathf.Sign(obstacleSide);
                float projectedHalfWidth = Mathf.Abs(right.x) * bounds.extents.x
                    + Mathf.Abs(right.y) * bounds.extents.z;
                float shift = projectedHalfWidth + ModelAvoidanceMargin - Mathf.Abs(obstacleSide);
                if (shift <= 0f)
                {
                    continue;
                }

                float projectedHalfLength = Mathf.Abs(tangent.x) * bounds.extents.x
                    + Mathf.Abs(tangent.y) * bounds.extents.z;
                float influence = projectedHalfLength + ModelAvoidanceTransition + shift * 0.75f;
                float centerDistance = distances[closestIndex];
                for (int index = 2; index < points.Count - 2; index++)
                {
                    float distanceFromCenter = Mathf.Abs(distances[index] - centerDistance);
                    if (distanceFromCenter >= influence)
                    {
                        continue;
                    }

                    float t = distanceFromCenter / influence;
                    float weight = 0.5f + 0.5f * Mathf.Cos(t * Mathf.PI);
                    Vector3 point = points[index];
                    point.x += right.x * sideSign * shift * weight;
                    point.z += right.y * sideSign * shift * weight;
                    points[index] = point;
                }
            }
        }
    }

    private static int FindClosestIntersectingPoint(IReadOnlyList<Vector3> points, Bounds bounds)
    {
        int closestIndex = -1;
        float closestDistance = float.PositiveInfinity;
        for (int index = 2; index < points.Count - 2; index++)
        {
            Vector3 point = points[index];
            if (bounds.max.y < point.y - 0.35f || bounds.min.y > point.y + 1.8f)
            {
                continue;
            }

            float deltaX = Mathf.Max(bounds.min.x - point.x, 0f, point.x - bounds.max.x);
            float deltaZ = Mathf.Max(bounds.min.z - point.z, 0f, point.z - bounds.max.z);
            float distance = Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            if (distance < ModelAvoidanceMargin && distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = index;
            }
        }

        return closestIndex;
    }

    private static bool IsModelObstacle(Renderer renderer)
    {
        Bounds bounds = renderer.bounds;
        if (bounds.size.y < 1f || Mathf.Max(bounds.size.x, bounds.size.z) > MaximumAvoidedModelSize)
        {
            return false;
        }

        for (Transform current = renderer.transform; current != null; current = current.parent)
        {
            string name = current.name;
            if (name.StartsWith(StoneGroundNamePrefix, StringComparison.Ordinal)
                || name.IndexOf("terrain", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("cliff", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("mountain", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static float SampleTrackClearanceHeight(
        Vector3 point,
        List<Terrain> terrains,
        Terrain fallbackTerrain,
        IReadOnlyList<Renderer> surfaceRenderers)
    {
        float highest = SampleHighestTerrainHeight(terrains, point.x, point.z, fallbackTerrain);
        const int samples = 8;
        for (int index = 0; index < samples; index++)
        {
            float angle = index / (float)samples * Mathf.PI * 2f;
            float sampleX = point.x + Mathf.Cos(angle) * TrackFootprintRadius;
            float sampleZ = point.z + Mathf.Sin(angle) * TrackFootprintRadius;
            highest = Mathf.Max(
                highest,
                SampleHighestTerrainHeight(terrains, sampleX, sampleZ, fallbackTerrain));
        }

        float requiredHeight = highest + TrackGroundClearance;
        for (int index = 0; index < surfaceRenderers.Count; index++)
        {
            Bounds bounds = surfaceRenderers[index].bounds;
            if (point.x < bounds.min.x - TrackFootprintRadius
                || point.x > bounds.max.x + TrackFootprintRadius
                || point.z < bounds.min.z - TrackFootprintRadius
                || point.z > bounds.max.z + TrackFootprintRadius)
            {
                continue;
            }

            float surfaceTop = bounds.max.y;
            if (surfaceTop >= point.y - TrackModelClearance
                && surfaceTop <= point.y + MaximumAutomaticModelLift)
            {
                requiredHeight = Mathf.Max(requiredHeight, surfaceTop + TrackModelClearance);
            }
        }

        return requiredHeight;
    }

    private static List<Renderer> GetTrackSurfaceRenderers(Scene scene)
    {
        return Resources.FindObjectsOfTypeAll<Renderer>()
            .Where(renderer => renderer.gameObject.scene == scene
                && renderer.enabled
                && renderer.gameObject.activeInHierarchy
                && !IsBelowNamedRoot(renderer.transform, RootName))
            .ToList();
    }

    private static bool IsBelowNamedRoot(Transform transform, string rootName)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (current.name == rootName)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * ((2f * p1)
            + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private static void CreateTrackVisuals(
        Transform parent,
        IReadOnlyList<Vector3> points,
        List<Terrain> terrains,
        Terrain fallbackTerrain,
        Material trackMaterial,
        Material tieMaterial,
        Material supportMaterial)
    {
        GameObject trackRoot = new GameObject(GeneratedTrackRootName);
        trackRoot.transform.SetParent(parent, false);
        RouteFrames frames = BuildFrames(points);

        Mesh leftRail = BuildTubeMesh(points, frames, -0.73f, 0.115f, 8);
        Mesh rightRail = BuildTubeMesh(points, frames, 0.73f, 0.115f, 8);
        Mesh railsCombined = CombineMeshes(leftRail, rightRail, "Placeholder Rails");
        UnityEngine.Object.DestroyImmediate(leftRail);
        UnityEngine.Object.DestroyImmediate(rightRail);
        Mesh railAsset = SaveOrReplaceMesh(railsCombined, RailMeshPath);
        CreateMeshObject("Twin Rails", trackRoot.transform, railAsset, trackMaterial);

        Mesh ties = BuildTieMesh(points, frames, 1.65f);
        Mesh tieAsset = SaveOrReplaceMesh(ties, TieMeshPath);
        CreateMeshObject("Cross Ties", trackRoot.transform, tieAsset, tieMaterial);

        Mesh supports = BuildSupportMesh(points, frames, terrains, fallbackTerrain, 9f);
        Mesh supportAsset = SaveOrReplaceMesh(supports, SupportMeshPath);
        CreateMeshObject("Support Columns", trackRoot.transform, supportAsset, supportMaterial);
    }

    private static RouteFrames BuildFrames(IReadOnlyList<Vector3> points)
    {
        int count = points.Count;
        var frames = new RouteFrames
        {
            Tangents = new Vector3[count],
            Rights = new Vector3[count],
            Ups = new Vector3[count],
            Distances = new float[count]
        };

        for (int index = 1; index < count; index++)
        {
            frames.Distances[index] = frames.Distances[index - 1] + Vector3.Distance(points[index - 1], points[index]);
        }
        for (int index = 0; index < count; index++)
        {
            int previous = Mathf.Max(0, index - 1);
            int next = Mathf.Min(count - 1, index + 1);
            frames.Tangents[index] = (points[next] - points[previous]).normalized;
        }

        for (int index = 0; index < count; index++)
        {
            Vector3 right = Vector3.Cross(Vector3.up, frames.Tangents[index]);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = index > 0
                    ? Vector3.ProjectOnPlane(frames.Rights[index - 1], frames.Tangents[index])
                    : Vector3.ProjectOnPlane(Vector3.right, frames.Tangents[index]);
            }
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.forward;
            }

            right.Normalize();
            Vector3 up = Vector3.Cross(frames.Tangents[index], right).normalized;
            if (Vector3.Dot(up, Vector3.up) < 0f)
            {
                right = -right;
                up = -up;
            }

            frames.Rights[index] = right;
            frames.Ups[index] = up;
        }

        for (int index = 1; index < count - 1; index++)
        {
            float signedTurn = Vector3.SignedAngle(frames.Tangents[index - 1], frames.Tangents[index + 1], frames.Ups[index]);
            float bank = Mathf.Clamp(-signedTurn * 0.55f, -14f, 14f);
            frames.Ups[index] = (Quaternion.AngleAxis(bank, frames.Tangents[index]) * frames.Ups[index]).normalized;
        }
        for (int index = 0; index < count; index++)
        {
            frames.Rights[index] = Vector3.Cross(frames.Ups[index], frames.Tangents[index]).normalized;
        }
        return frames;
    }

    private static Mesh BuildTubeMesh(
        IReadOnlyList<Vector3> points,
        RouteFrames frames,
        float lateralOffset,
        float radius,
        int sides)
    {
        var vertices = new List<Vector3>(points.Count * sides);
        var triangles = new List<int>((points.Count - 1) * sides * 6);
        var uvs = new List<Vector2>(vertices.Capacity);

        for (int index = 0; index < points.Count; index++)
        {
            Vector3 center = points[index] + frames.Rights[index] * lateralOffset + frames.Ups[index] * 0.08f;
            for (int side = 0; side < sides; side++)
            {
                float angle = side / (float)sides * Mathf.PI * 2f;
                Vector3 radial = frames.Rights[index] * Mathf.Cos(angle) + frames.Ups[index] * Mathf.Sin(angle);
                vertices.Add(center + radial * radius);
                uvs.Add(new Vector2(side / (float)sides, frames.Distances[index] * 0.24f));
            }
        }

        for (int ring = 0; ring < points.Count - 1; ring++)
        {
            for (int side = 0; side < sides; side++)
            {
                int nextSide = (side + 1) % sides;
                int a = ring * sides + side;
                int b = ring * sides + nextSide;
                int c = (ring + 1) * sides + side;
                int d = (ring + 1) * sides + nextSide;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
        }

        Mesh mesh = new Mesh { name = "Rail" };
        mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildTieMesh(IReadOnlyList<Vector3> points, RouteFrames frames, float spacing)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        float length = frames.Distances[frames.Distances.Length - 1];
        for (float distance = 0f; distance <= length; distance += spacing)
        {
            SampleRoute(points, frames, distance, out Vector3 position, out Vector3 tangent, out Vector3 right, out Vector3 up);
            AppendBox(vertices, triangles, position - up * 0.12f, right, up, tangent, new Vector3(1.08f, 0.09f, 0.16f));
        }

        return CreateMesh("Placeholder Ties", vertices, triangles);
    }

    private static Mesh BuildSupportMesh(
        IReadOnlyList<Vector3> points,
        RouteFrames frames,
        List<Terrain> terrains,
        Terrain fallbackTerrain,
        float spacing)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        float length = frames.Distances[frames.Distances.Length - 1];
        for (float distance = spacing; distance < length - spacing; distance += spacing)
        {
            SampleRoute(points, frames, distance, out Vector3 position, out Vector3 tangent, out Vector3 right, out Vector3 up);
            float ground = SampleHighestTerrainHeight(terrains, position.x, position.z, fallbackTerrain);
            float top = position.y - 0.18f;
            float height = top - ground;
            if (height < 0.65f)
            {
                continue;
            }

            AppendBox(vertices, triangles, position - up * 0.24f, right, up, tangent, new Vector3(1.03f, 0.095f, 0.12f));
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 columnTop = position + right * (0.77f * side) - up * 0.28f;
                Vector3 center = new Vector3(columnTop.x, ground + height * 0.5f, columnTop.z);
                AppendBox(vertices, triangles, center, Vector3.right, Vector3.up, Vector3.forward, new Vector3(0.095f, height * 0.5f, 0.095f));
            }
        }

        return CreateMesh("Placeholder Supports", vertices, triangles);
    }

    private static void SampleRoute(
        IReadOnlyList<Vector3> points,
        RouteFrames frames,
        float distance,
        out Vector3 position,
        out Vector3 tangent,
        out Vector3 right,
        out Vector3 up)
    {
        int upper = Array.BinarySearch(frames.Distances, distance);
        if (upper < 0)
        {
            upper = ~upper;
        }
        upper = Mathf.Clamp(upper, 1, points.Count - 1);
        int lower = upper - 1;
        float segmentLength = frames.Distances[upper] - frames.Distances[lower];
        float t = segmentLength > 0.0001f ? (distance - frames.Distances[lower]) / segmentLength : 0f;
        position = Vector3.Lerp(points[lower], points[upper], t);
        tangent = Vector3.Slerp(frames.Tangents[lower], frames.Tangents[upper], t).normalized;
        up = Vector3.Slerp(frames.Ups[lower], frames.Ups[upper], t).normalized;
        right = Vector3.Cross(up, tangent).normalized;
    }

    private static void AppendBox(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 center,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        Vector3 halfExtents)
    {
        right.Normalize();
        up.Normalize();
        forward.Normalize();
        int start = vertices.Count;
        for (int y = -1; y <= 1; y += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                for (int x = -1; x <= 1; x += 2)
                {
                    vertices.Add(center
                        + right * (halfExtents.x * x)
                        + up * (halfExtents.y * y)
                        + forward * (halfExtents.z * z));
                }
            }
        }

        int[] boxTriangles =
        {
            0, 4, 6, 0, 6, 2,
            1, 3, 7, 1, 7, 5,
            0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3,
            0, 2, 3, 0, 3, 1,
            4, 5, 7, 4, 7, 6
        };
        for (int index = 0; index < boxTriangles.Length; index++)
        {
            triangles.Add(start + boxTriangles[index]);
        }
    }

    private static Transform CreatePlaceholderVehicle(
        Transform parent,
        Material cartMaterial,
        Material trackMaterial,
        Material accentMaterial)
    {
        GameObject vehicle = new GameObject("RideVehicle");
        vehicle.transform.SetParent(parent, false);
        Transform visual = new GameObject("CartVisual_Placeholder_ReplaceMe").transform;
        visual.SetParent(vehicle.transform, false);

        CreatePrimitivePart("Cart Body", PrimitiveType.Cube, visual, new Vector3(0f, 0.3f, 0f), Vector3.zero, new Vector3(1.65f, 0.38f, 2.45f), cartMaterial);
        CreatePrimitivePart("Cart Nose", PrimitiveType.Cube, visual, new Vector3(0f, 0.49f, 0.78f), new Vector3(11f, 0f, 0f), new Vector3(1.58f, 0.34f, 0.72f), cartMaterial);
        CreatePrimitivePart("Seat Back", PrimitiveType.Cube, visual, new Vector3(0f, 0.86f, -0.48f), new Vector3(-8f, 0f, 0f), new Vector3(1.28f, 0.82f, 0.18f), trackMaterial);
        CreatePrimitivePart("Safety Bar", PrimitiveType.Cube, visual, new Vector3(0f, 0.95f, 0.52f), Vector3.zero, new Vector3(1.18f, 0.10f, 0.10f), accentMaterial);

        for (int side = -1; side <= 1; side += 2)
        {
            for (int end = -1; end <= 1; end += 2)
            {
                CreatePrimitivePart(
                    $"Wheel_{side}_{end}",
                    PrimitiveType.Cylinder,
                    visual,
                    new Vector3(0.86f * side, -0.02f, 0.72f * end),
                    new Vector3(0f, 0f, 90f),
                    new Vector3(0.27f, 0.10f, 0.27f),
                    trackMaterial);
            }
        }

        Transform cameraMount = new GameObject("CameraMount_ReplaceWithXR").transform;
        cameraMount.SetParent(vehicle.transform, false);
        cameraMount.localPosition = new Vector3(0f, 1.23f, 0.34f);
        cameraMount.gameObject.AddComponent<RollerCoasterLookController>();
        GameObject cameraObject = new GameObject("RollerCoaster First Person Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(cameraMount, false);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 78f;
        camera.nearClipPlane = 0.04f;
        camera.farClipPlane = 1000f;
        camera.allowHDR = true;
        cameraObject.AddComponent<AudioListener>();

        foreach (Camera otherCamera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (otherCamera != camera && otherCamera.gameObject.scene == parent.gameObject.scene)
            {
                otherCamera.enabled = false;
            }
        }
        foreach (AudioListener listener in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (listener.gameObject != cameraObject && listener.gameObject.scene == parent.gameObject.scene)
            {
                listener.enabled = false;
            }
        }
        return vehicle.transform;
    }

    private static void CreateStationMarkers(
        Transform parent,
        IReadOnlyList<Vector3> route,
        Material trackMaterial,
        Material accentMaterial)
    {
        Transform stations = new GameObject("StationMarkers").transform;
        stations.SetParent(parent, false);
        CreateStationMarker("Start Station - Stone Road", stations, route[0], route[1] - route[0], accentMaterial);
        CreateStationMarker("End Station - Stone Road", stations, route[route.Count - 1], route[route.Count - 1] - route[route.Count - 2], trackMaterial);
    }

    private static void CreateStationMarker(string name, Transform parent, Vector3 position, Vector3 direction, Material material)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.forward;
        }
        GameObject marker = CreatePrimitivePart(
            name,
            PrimitiveType.Cube,
            parent,
            position + Vector3.down * 0.28f,
            Quaternion.LookRotation(direction.normalized, Vector3.up).eulerAngles,
            new Vector3(2.7f, 0.12f, 3.4f),
            material);
        marker.transform.position = position + Vector3.down * 0.28f;
    }

    private static GameObject CreatePrimitivePart(
        string name,
        PrimitiveType primitive,
        Transform parent,
        Vector3 localPosition,
        Vector3 localEulerAngles,
        Vector3 localScale,
        Material material)
    {
        GameObject part = GameObject.CreatePrimitive(primitive);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localEulerAngles = localEulerAngles;
        part.transform.localScale = localScale;
        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            UnityEngine.Object.DestroyImmediate(collider);
        }
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part;
    }

    private static Mesh CombineMeshes(Mesh first, Mesh second, string name)
    {
        var combined = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        combined.CombineMeshes(new[]
        {
            new CombineInstance { mesh = first, transform = Matrix4x4.identity },
            new CombineInstance { mesh = second, transform = Matrix4x4.identity }
        }, true, false, false);
        combined.RecalculateBounds();
        return combined;
    }

    private static Mesh CreateMesh(string name, List<Vector3> vertices, List<int> triangles)
    {
        Mesh mesh = new Mesh { name = name };
        mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void CreateMeshObject(string name, Transform parent, Mesh mesh, Material material)
    {
        GameObject gameObject = new GameObject(name);
        gameObject.transform.SetParent(parent, false);
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
    }

    private static Mesh SaveOrReplaceMesh(Mesh generated, string path)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }

        existing.Clear();
        existing.name = generated.name;
        existing.indexFormat = generated.indexFormat;
        existing.vertices = generated.vertices;
        existing.normals = generated.normals;
        existing.tangents = generated.tangents;
        existing.uv = generated.uv;
        existing.colors = generated.colors;
        existing.subMeshCount = generated.subMeshCount;
        for (int subMesh = 0; subMesh < generated.subMeshCount; subMesh++)
        {
            existing.SetIndices(
                generated.GetIndices(subMesh),
                generated.GetTopology(subMesh),
                subMesh,
                false);
        }
        existing.bounds = generated.bounds;
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssetIfDirty(existing);
        UnityEngine.Object.DestroyImmediate(generated);
        return existing;
    }

    private static Material CreateOrUpdateMaterial(
        string path,
        string name,
        Color color,
        float metallic,
        float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
            material.name = name;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Terrain FindTerrain(Scene scene)
    {
        return Resources.FindObjectsOfTypeAll<Terrain>()
            .FirstOrDefault(candidate => candidate.gameObject.scene == scene && candidate.name == "Wilderness Terrain");
    }

    private static List<Terrain> GetSceneTerrains(Scene scene)
    {
        return Resources.FindObjectsOfTypeAll<Terrain>()
            .Where(candidate => candidate.gameObject.scene == scene && candidate.enabled)
            .ToList();
    }

    private static Vector3 FindTerrainPeak(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        Vector3 peak = terrain.transform.position;
        float peakHeight = float.NegativeInfinity;
        const int grid = 48;
        for (int z = 2; z < grid - 2; z++)
        {
            float v = z / (grid - 1f);
            for (int x = 2; x < grid - 2; x++)
            {
                float u = x / (grid - 1f);
                float localHeight = data.GetInterpolatedHeight(u, v);
                Vector3 world = terrain.transform.position + new Vector3(
                    u * data.size.x,
                    localHeight,
                    v * data.size.z);
                if (world.y > peakHeight)
                {
                    peakHeight = world.y;
                    peak = world;
                }
            }
        }
        return peak;
    }

    private static List<Transform> GetStoneGroundPieces(Scene scene)
    {
        return Resources.FindObjectsOfTypeAll<Transform>()
            .Where(candidate => candidate.gameObject.scene == scene
                && candidate.name.StartsWith(StoneGroundNamePrefix, StringComparison.Ordinal))
            .ToList();
    }

    private static Vector3 FindRoadPoint(List<Transform> roadPieces, Vector2 target, Terrain terrain)
    {
        Transform nearest = roadPieces
            .OrderBy(piece => Vector2.SqrMagnitude(new Vector2(piece.position.x, piece.position.z) - target))
            .FirstOrDefault();
        if (nearest == null)
        {
            float height = SampleTerrainHeight(terrain, target.x, target.y);
            return new Vector3(target.x, height + 1.05f, target.y);
        }

        float terrainHeight = SampleTerrainHeight(terrain, nearest.position.x, nearest.position.z);
        Renderer renderer = nearest.GetComponentInChildren<Renderer>();
        float roadTop = renderer == null ? nearest.position.y : renderer.bounds.max.y;
        return new Vector3(
            nearest.position.x,
            Mathf.Max(terrainHeight + 0.9f, roadTop + 0.58f),
            nearest.position.z);
    }

    private static float SampleTerrainHeight(Terrain terrain, float worldX, float worldZ)
    {
        if (TrySampleTerrainHeight(terrain, worldX, worldZ, out float height))
        {
            return height;
        }

        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        float u = Mathf.Clamp01((worldX - origin.x) / data.size.x);
        float v = Mathf.Clamp01((worldZ - origin.z) / data.size.z);
        float localHeight = data.GetInterpolatedHeight(u, v);
        return origin.y + localHeight;
    }

    private static float SampleHighestTerrainHeight(
        List<Terrain> terrains,
        float worldX,
        float worldZ,
        Terrain fallbackTerrain)
    {
        float highest = float.NegativeInfinity;
        for (int index = 0; index < terrains.Count; index++)
        {
            if (TrySampleTerrainHeight(terrains[index], worldX, worldZ, out float height))
            {
                highest = Mathf.Max(highest, height);
            }
        }

        return float.IsNegativeInfinity(highest)
            ? SampleTerrainHeight(fallbackTerrain, worldX, worldZ)
            : highest;
    }

    private static bool TrySampleTerrainHeight(Terrain terrain, float worldX, float worldZ, out float height)
    {
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        float localX = worldX - origin.x;
        float localZ = worldZ - origin.z;
        if (localX < 0f || localZ < 0f || localX > data.size.x || localZ > data.size.z)
        {
            height = 0f;
            return false;
        }

        float u = localX / data.size.x;
        float v = localZ / data.size.z;
        float localHeight = data.GetInterpolatedHeight(u, v);
        height = origin.y + localHeight;
        return true;
    }

    private static void DestroySceneObject(Scene scene, string objectName)
    {
        GameObject existing = FindSceneObject(scene, objectName);
        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(existing);
        }
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        return Resources.FindObjectsOfTypeAll<GameObject>()
            .FirstOrDefault(candidate => candidate.scene == scene && candidate.name == objectName);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        int separator = path.LastIndexOf('/');
        string parent = path.Substring(0, separator);
        string name = path.Substring(separator + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
