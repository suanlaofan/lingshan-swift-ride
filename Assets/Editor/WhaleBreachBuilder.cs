using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class WhaleBreachBuilder
{
    private const string RootName = "FantasyWhaleBreach";
    private const string RollerCoasterRootName = "RollerCoaster_Placeholder";
    private const string WhaleAssetPath = "Assets/Art/WhaleBreach/FantasyWhaleBone.glb";
    private const string GeneratedAssetFolder = "Assets/Art/WhaleBreach/Generated";
    private const string RiggedMeshAssetPath = GeneratedAssetFolder + "/FantasyWhaleBone_Rigged.asset";
    // These values were projected from the supplied Scene view reference: the whale
    // begins its breach shortly before the rider reaches the red-circled drop, then
    // clears the rails as the vehicle enters that point.
    private const float SequenceStartProgress = 0.235f;
    private const float TrackCrossingProgress = 0.2855f;
    private const float SequenceDuration = 5f;
    private const float WhaleScale = 32f;

    private static readonly Vector3[] SpinePositions =
    {
        new Vector3(0f, 0.34f, 0.36f),
        new Vector3(0f, 0.58f, 0.45f),
        new Vector3(0f, 0.72f, 0.40f),
        new Vector3(0f, 0.84f, 0.30f),
        new Vector3(0f, 0.91f, 0.17f),
        new Vector3(0f, 0.87f, 0.03f),
        new Vector3(0f, 0.82f, -0.11f),
        new Vector3(0f, 0.73f, -0.24f),
        new Vector3(0f, 0.59f, -0.36f),
        new Vector3(0f, 0.38f, -0.47f)
    };

    [MenuItem("Tools/Environment/Build Fantasy Whale Breach")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject rollerCoasterRoot = FindSceneObject(scene, RollerCoasterRootName);
        RollerCoasterRide ride = rollerCoasterRoot == null
            ? null
            : rollerCoasterRoot.GetComponent<RollerCoasterRide>();
        RollerCoasterPath path = rollerCoasterRoot == null
            ? null
            : rollerCoasterRoot.GetComponentInChildren<RollerCoasterPath>(true);
        if (ride == null || path == null || path.Length <= 0.01f)
        {
            Debug.LogError("The active scene does not contain the configured placeholder roller coaster.");
            return;
        }

        AssetDatabase.ImportAsset(WhaleAssetPath, ImportAssetOptions.ForceSynchronousImport);
        GameObject whaleAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WhaleAssetPath);
        if (whaleAsset == null)
        {
            Debug.LogError("FantasyWhaleBone.glb has not imported as a GameObject. Check the glTFast package in Package Manager.");
            return;
        }

        GameObject previousRoot = FindSceneObject(scene, RootName);
        if (previousRoot != null)
        {
            Object.DestroyImmediate(previousRoot);
        }

        path.EvaluateDistance(
            path.Length * TrackCrossingProgress,
            out Vector3 trackCrossing,
            out Vector3 trackTangent,
            out _);
        // Keep the breach's forward offset in the ground plane. Using the full
        // downhill rail tangent here would pull the "over track" waypoint down the
        // drop and prevent the whale from actually clearing the rails.
        Vector3 horizontalForward = Vector3.ProjectOnPlane(trackTangent, Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.001f)
        {
            horizontalForward = Vector3.forward;
        }
        horizontalForward.Normalize();

        Vector3 lateral = Vector3.Cross(Vector3.up, horizontalForward);
        if (lateral.sqrMagnitude < 0.001f)
        {
            lateral = Vector3.right;
        }
        lateral.Normalize();

        GameObject root = new GameObject(RootName);
        Transform waypointRoot = new GameObject("Route_Waypoints_Edit").transform;
        waypointRoot.SetParent(root.transform, false);
        List<Transform> waypoints = CreateWaypoints(waypointRoot, trackCrossing, horizontalForward, lateral);

        Transform animatedRoot = new GameObject("Whale_AnimatedRoot").transform;
        animatedRoot.SetParent(root.transform, false);
        GameObject whaleInstance = (GameObject)PrefabUtility.InstantiatePrefab(whaleAsset, scene);
        whaleInstance.name = "FantasyWhaleBone_Model";
        whaleInstance.transform.SetParent(animatedRoot, false);
        whaleInstance.transform.localPosition = new Vector3(0f, -0.485f * WhaleScale, 0f);
        whaleInstance.transform.localRotation = Quaternion.identity;
        whaleInstance.transform.localScale = Vector3.one * WhaleScale;
        if (PrefabUtility.IsPartOfPrefabInstance(whaleInstance))
        {
            PrefabUtility.UnpackPrefabInstance(
                whaleInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
        }

        WhaleProceduralRig proceduralRig = BuildProceduralRig(whaleInstance);
        if (proceduralRig == null)
        {
            Object.DestroyImmediate(root);
            return;
        }
        ConfigureRenderers(whaleInstance);

        WhaleBreachSequence sequence = root.AddComponent<WhaleBreachSequence>();
        sequence.Configure(
            ride,
            animatedRoot,
            proceduralRig,
            waypoints,
            SequenceStartProgress,
            SequenceDuration,
            new Vector3(0f, 90f, 0f));

        EditorUtility.SetDirty(sequence);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        Debug.Log(
            "Fantasy whale breach built. The whale starts below the clouds, crosses the track at ride progress "
            + $"{TrackCrossingProgress:P1}, then dives back below the clouds. Edit Route_Waypoints_Edit to tune the arc.");
    }

    [MenuItem("Tools/Environment/Select Fantasy Whale Route")]
    public static void SelectRoute()
    {
        GameObject root = FindSceneObject(SceneManager.GetActiveScene(), RootName);
        Transform waypointRoot = root == null ? null : root.transform.Find("Route_Waypoints_Edit");
        if (waypointRoot == null)
        {
            Debug.LogWarning("Build the fantasy whale breach first.");
            return;
        }

        Selection.objects = waypointRoot.Cast<Transform>().Select(item => item.gameObject).ToArray();
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    [MenuItem("Tools/Environment/Preview Fantasy Whale Over Track")]
    public static void PreviewOverTrack()
    {
        PreviewAt(0.56f);
    }

    [MenuItem("Tools/Environment/Preview Fantasy Whale Emergence")]
    public static void PreviewEmergence()
    {
        PreviewAt(0.24f);
    }

    [MenuItem("Tools/Environment/Preview Fantasy Whale Dive")]
    public static void PreviewDive()
    {
        PreviewAt(0.8f);
    }

    private static void PreviewAt(float normalizedTime)
    {
        WhaleBreachSequence sequence = FindSequence();
        if (sequence == null)
        {
            Debug.LogWarning("Build the fantasy whale breach first.");
            return;
        }

        sequence.PreviewAt(normalizedTime);
        Selection.activeGameObject = sequence.gameObject;
        SceneView.RepaintAll();
    }

    [MenuItem("Tools/Environment/Hide Fantasy Whale Preview")]
    public static void HidePreview()
    {
        WhaleBreachSequence sequence = FindSequence();
        if (sequence == null)
        {
            return;
        }

        sequence.HidePreview();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        SceneView.RepaintAll();
    }

    private static List<Transform> CreateWaypoints(
        Transform parent,
        Vector3 crossing,
        Vector3 horizontalForward,
        Vector3 lateral)
    {
        Vector3[] positions =
        {
            crossing + lateral * 46f - Vector3.up * 46f + horizontalForward * 2f,
            crossing + lateral * 34f - Vector3.up * 10f + horizontalForward * 9f,
            crossing + lateral * 20f + Vector3.up * 14f + horizontalForward * 18f,
            crossing - lateral * 1f + Vector3.up * 22f + horizontalForward * 28f,
            crossing - lateral * 26f + Vector3.up * 5f + horizontalForward * 39f,
            crossing - lateral * 48f - Vector3.up * 46f + horizontalForward * 54f
        };
        string[] names =
        {
            "WP_00_Start_BelowCloud",
            "WP_01_BreakCloud",
            "WP_02_Rise",
            "WP_03_OverTrack",
            "WP_04_Dive",
            "WP_05_End_BelowCloud"
        };

        var result = new List<Transform>(positions.Length);
        for (int index = 0; index < positions.Length; index++)
        {
            GameObject point = new GameObject(names[index]);
            point.transform.SetParent(parent, false);
            point.transform.position = positions[index];
            Texture2D icon = EditorGUIUtility.IconContent(index == 3 ? "sv_label_1" : "sv_label_6").image as Texture2D;
            EditorGUIUtility.SetIconForObject(point, icon);
            result.Add(point.transform);
        }
        return result;
    }

    private static WhaleProceduralRig BuildProceduralRig(GameObject whaleInstance)
    {
        MeshFilter[] meshFilters = whaleInstance
            .GetComponentsInChildren<MeshFilter>(true)
            .Where(filter => filter.sharedMesh != null)
            .ToArray();
        if (meshFilters.Length != 1)
        {
            Debug.LogError(
                $"Expected one whale mesh to rig, but found {meshFilters.Length}. "
                + "The source GLB hierarchy may have changed.");
            return null;
        }

        MeshFilter sourceFilter = meshFilters[0];
        MeshRenderer sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
        if (sourceRenderer == null)
        {
            Debug.LogError("The whale mesh does not have a MeshRenderer to convert to a SkinnedMeshRenderer.");
            return null;
        }

        Mesh sourceMesh = sourceFilter.sharedMesh;
        if (!sourceMesh.isReadable)
        {
            Debug.LogError("The imported whale mesh is not readable, so editor-time skin weights cannot be generated.");
            return null;
        }

        Transform[] bones = CreateSpine(sourceFilter.transform);
        Mesh riggedMesh = CreateRiggedMesh(sourceMesh, sourceFilter.transform, bones);
        if (riggedMesh == null)
        {
            return null;
        }

        Material[] materials = sourceRenderer.sharedMaterials;
        ShadowCastingMode shadowCastingMode = sourceRenderer.shadowCastingMode;
        bool receiveShadows = sourceRenderer.receiveShadows;
        MotionVectorGenerationMode motionVectors = sourceRenderer.motionVectorGenerationMode;
        LightProbeUsage lightProbeUsage = sourceRenderer.lightProbeUsage;
        ReflectionProbeUsage reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        Transform probeAnchor = sourceRenderer.probeAnchor;
        int sortingLayerId = sourceRenderer.sortingLayerID;
        int sortingOrder = sourceRenderer.sortingOrder;
        GameObject meshObject = sourceFilter.gameObject;

        Object.DestroyImmediate(sourceRenderer);
        Object.DestroyImmediate(sourceFilter);

        SkinnedMeshRenderer skinnedRenderer = meshObject.AddComponent<SkinnedMeshRenderer>();
        skinnedRenderer.sharedMesh = riggedMesh;
        skinnedRenderer.sharedMaterials = materials;
        skinnedRenderer.rootBone = bones[0];
        skinnedRenderer.bones = bones;
        skinnedRenderer.quality = SkinQuality.Bone1;
        skinnedRenderer.updateWhenOffscreen = true;
        skinnedRenderer.forceMatrixRecalculationPerRender = true;
        skinnedRenderer.allowOcclusionWhenDynamic = false;
        skinnedRenderer.shadowCastingMode = shadowCastingMode;
        skinnedRenderer.receiveShadows = receiveShadows;
        skinnedRenderer.motionVectorGenerationMode = motionVectors;
        skinnedRenderer.lightProbeUsage = lightProbeUsage;
        skinnedRenderer.reflectionProbeUsage = reflectionProbeUsage;
        skinnedRenderer.probeAnchor = probeAnchor;
        skinnedRenderer.sortingLayerID = sortingLayerId;
        skinnedRenderer.sortingOrder = sortingOrder;

        Bounds expandedBounds = riggedMesh.bounds;
        expandedBounds.Expand(new Vector3(0.8f, 0.8f, 0.8f));
        skinnedRenderer.localBounds = expandedBounds;

        WhaleProceduralRig rig = whaleInstance.AddComponent<WhaleProceduralRig>();
        rig.Configure(bones);
        EditorUtility.SetDirty(rig);
        return rig;
    }

    private static Transform[] CreateSpine(Transform rendererTransform)
    {
        Transform[] bones = new Transform[SpinePositions.Length];
        Transform parent = rendererTransform;
        for (int index = 0; index < SpinePositions.Length; index++)
        {
            GameObject boneObject = new GameObject($"WhaleSpine_{index:00}");
            Transform bone = boneObject.transform;
            bone.SetParent(parent, false);
            bone.localPosition = index == 0
                ? SpinePositions[index]
                : SpinePositions[index] - SpinePositions[index - 1];
            bone.localRotation = Quaternion.identity;
            bone.localScale = Vector3.one;
            bones[index] = bone;
            parent = bone;
        }

        return bones;
    }

    private static Mesh CreateRiggedMesh(
        Mesh sourceMesh,
        Transform rendererTransform,
        IReadOnlyList<Transform> bones)
    {
        Vector3[] vertices = sourceMesh.vertices;
        int[] triangles = sourceMesh.triangles;
        if (vertices.Length == 0 || triangles.Length == 0)
        {
            Debug.LogError("The whale mesh has no readable geometry to rig.");
            return null;
        }

        int[] componentParents = new int[vertices.Length];
        for (int index = 0; index < componentParents.Length; index++)
        {
            componentParents[index] = index;
        }

        for (int index = 0; index + 2 < triangles.Length; index += 3)
        {
            Union(componentParents, triangles[index], triangles[index + 1]);
            Union(componentParents, triangles[index + 1], triangles[index + 2]);
        }

        Vector3[] componentSums = new Vector3[vertices.Length];
        int[] componentVertexCounts = new int[vertices.Length];
        for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            int component = FindComponent(componentParents, vertexIndex);
            componentSums[component] += vertices[vertexIndex];
            componentVertexCounts[component]++;
        }

        int[] componentBoneIndices = new int[vertices.Length];
        for (int component = 0; component < componentVertexCounts.Length; component++)
        {
            if (componentVertexCounts[component] == 0)
            {
                continue;
            }

            Vector3 center = componentSums[component] / componentVertexCounts[component];
            float closestDistance = float.PositiveInfinity;
            int closestBone = 0;
            for (int boneIndex = 0; boneIndex < SpinePositions.Length; boneIndex++)
            {
                float distance = (center - SpinePositions[boneIndex]).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestBone = boneIndex;
                }
            }
            componentBoneIndices[component] = closestBone;
        }

        BoneWeight[] weights = new BoneWeight[vertices.Length];
        for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            int component = FindComponent(componentParents, vertexIndex);
            weights[vertexIndex] = new BoneWeight
            {
                boneIndex0 = componentBoneIndices[component],
                weight0 = 1f
            };
        }

        Matrix4x4[] bindPoses = new Matrix4x4[bones.Count];
        for (int boneIndex = 0; boneIndex < bones.Count; boneIndex++)
        {
            bindPoses[boneIndex] = bones[boneIndex].worldToLocalMatrix
                * rendererTransform.localToWorldMatrix;
        }

        Mesh generatedMesh = Object.Instantiate(sourceMesh);
        generatedMesh.name = "FantasyWhaleBone_Rigged";
        generatedMesh.boneWeights = weights;
        generatedMesh.bindposes = bindPoses;
        generatedMesh.RecalculateBounds();

        EnsureGeneratedAssetFolder();
        Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(RiggedMeshAssetPath);
        if (existingMesh == null)
        {
            AssetDatabase.CreateAsset(generatedMesh, RiggedMeshAssetPath);
            return generatedMesh;
        }

        EditorUtility.CopySerialized(generatedMesh, existingMesh);
        existingMesh.name = "FantasyWhaleBone_Rigged";
        EditorUtility.SetDirty(existingMesh);
        Object.DestroyImmediate(generatedMesh);
        return existingMesh;
    }

    private static int FindComponent(int[] parents, int vertex)
    {
        int root = vertex;
        while (parents[root] != root)
        {
            root = parents[root];
        }

        while (parents[vertex] != vertex)
        {
            int next = parents[vertex];
            parents[vertex] = root;
            vertex = next;
        }
        return root;
    }

    private static void Union(int[] parents, int first, int second)
    {
        int firstRoot = FindComponent(parents, first);
        int secondRoot = FindComponent(parents, second);
        if (firstRoot != secondRoot)
        {
            parents[secondRoot] = firstRoot;
        }
    }

    private static void EnsureGeneratedAssetFolder()
    {
        if (!AssetDatabase.IsValidFolder(GeneratedAssetFolder))
        {
            AssetDatabase.CreateFolder("Assets/Art/WhaleBreach", "Generated");
        }
    }

    private static void ConfigureRenderers(GameObject instance)
    {
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
        }
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform match = FindRecursive(root.transform, objectName);
            if (match != null)
            {
                return match.gameObject;
            }
        }
        return null;
    }

    private static WhaleBreachSequence FindSequence()
    {
        GameObject root = FindSceneObject(SceneManager.GetActiveScene(), RootName);
        return root == null ? null : root.GetComponent<WhaleBreachSequence>();
    }

    private static Transform FindRecursive(Transform current, string objectName)
    {
        if (current.name == objectName)
        {
            return current;
        }

        for (int index = 0; index < current.childCount; index++)
        {
            Transform match = FindRecursive(current.GetChild(index), objectName);
            if (match != null)
            {
                return match;
            }
        }
        return null;
    }
}
