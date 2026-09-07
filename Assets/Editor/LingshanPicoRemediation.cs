#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Explicit editor operations; importing this file never changes project settings.</summary>
public static class LingshanPicoRemediation
{
    public const string ScenePath = "Assets/Scenes/EnvironmentScene.unity";
    public const string ProfilePath = "Assets/Settings/PICO/Lingshan_PICO_RPAsset.asset";
    public const string RendererPath = "Assets/Settings/PICO/Lingshan_PICO_Renderer.asset";
    public const string QualityName = "Lingshan PICO";
    public const string PackageName = "com.newgamestudio.picoenvironmentstarter";
    private const string BaselinePath = "Assets/Settings/PICO/LingshanProfileBaseline.json";
    private const string BaselineQualityName = "Lingshan PICO Baseline";

    [MenuItem("Tools/Lingshan/PICO/Audit JSON")]
    public static void AuditJson()
    {
        WithScene(false, scene => Debug.Log("LINGSHAN_PICO_AUDIT " + JsonUtility.ToJson(CreateAudit(scene), true)));
    }

    [MenuItem("Tools/Lingshan/PICO/Apply PICO One Day Profile")]
    public static void ApplyPicoProfile()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before applying the PICO profile.");
        if (PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android) != PackageName)
            throw new InvalidOperationException("Unexpected Android package identity; existing package must be preserved.");

        EnsureFolder("Assets/Settings/PICO");
        EnsureChallengeMaterials();
        WithScene(true, scene =>
        {
            SaveBaselineOnce(scene);
            UniversalRenderPipelineAsset profile = CopyIfMissing<UniversalRenderPipelineAsset>(
                "Assets/Settings/PC_RPAsset.asset", ProfilePath);
            UniversalRendererData renderer = CopyIfMissing<UniversalRendererData>(
                "Assets/Settings/PC_Renderer.asset", RendererPath);

            // Keep the existing light coverage/rendering path and depth/opaque dependencies.
            // CopyAsset duplicates renderer subassets so disabling SSAO does not edit the PC original.
            foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
            {
                if (feature != null && feature.GetType().Name.IndexOf("AmbientOcclusion", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (AssetDatabase.GetAssetPath(feature) != RendererPath)
                        throw new InvalidOperationException("SSAO feature is shared with another renderer; refusing to modify it.");
                    feature.SetActive(false);
                    EditorUtility.SetDirty(feature);
                }
            }
            var serializedProfile = new SerializedObject(profile);
            SerializedProperty renderers = Require(serializedProfile, "m_RendererDataList");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            Require(serializedProfile, "m_DefaultRendererIndex").intValue = 0;
            // URP 17.5 exposes internal-only setters for these flags.
            Require(serializedProfile, "m_MainLightShadowsSupported").boolValue = true;
            Require(serializedProfile, "m_AdditionalLightShadowsSupported").boolValue = false;
            Require(serializedProfile, "m_AnyShadowsSupported").boolValue = true;
            Require(serializedProfile, "m_SoftShadowsSupported").boolValue = false;
            serializedProfile.ApplyModifiedPropertiesWithoutUndo();
            profile.supportsHDR = false;
            profile.msaaSampleCount = 2;
            profile.renderScale = 1f;
            profile.mainLightShadowmapResolution = 1024;
            profile.shadowCascadeCount = 2;
            profile.shadowDistance = 25f;
            profile.useSRPBatcher = true;
            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(renderer);

            foreach (Light light in Components<Light>(scene))
            {
                if (light.type != LightType.Point || light.shadows == LightShadows.None) continue;
                Undo.RecordObject(light, "Lingshan PICO point light shadows");
                light.shadows = LightShadows.None;
                PrefabUtility.RecordPrefabInstancePropertyModifications(light);
                EditorUtility.SetDirty(light);
            }
            BindAndroidQuality(QualityName, profile);
            GraphicsSettings.defaultRenderPipeline = profile;
            PlayerSettings.productName = "灵山飞驰";
            if (!Version.TryParse(PlayerSettings.bundleVersion, out Version existing) || existing < new Version(0, 2, 0))
                PlayerSettings.bundleVersion = "0.2.0";
            PlayerSettings.Android.bundleVersionCode = Math.Max(2, PlayerSettings.Android.bundleVersionCode);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the PICO scene.");
            AssetDatabase.SaveAssets();
            Debug.Log("LINGSHAN_PICO_PROFILE_APPLIED " + JsonUtility.ToJson(CreateAudit(scene)));
        });
    }

    [MenuItem("Tools/Lingshan/PICO/Use Baseline Render Profile")]
    public static void UseBaselineRenderProfile()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before switching profiles.");
        if (!File.Exists(BaselinePath)) throw new FileNotFoundException("Apply PICO profile once to capture the baseline.", BaselinePath);
        Baseline baseline = JsonUtility.FromJson<Baseline>(File.ReadAllText(BaselinePath));
        UniversalRenderPipelineAsset profile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(baseline.pipeline);
        if (profile == null) throw new InvalidOperationException("The captured baseline pipeline is missing.");
        WithScene(true, scene =>
        {
            var lights = new Dictionary<string, Light>();
            foreach (Light light in Components<Light>(scene))
                lights[GlobalObjectId.GetGlobalObjectIdSlow(light).ToString()] = light;
            int restored = 0;
            foreach (LightBaseline saved in baseline.pointLights)
            {
                if (!lights.TryGetValue(saved.objectId, out Light light)) continue;
                Undo.RecordObject(light, "Restore Lingshan baseline point light shadows");
                light.shadows = (LightShadows)saved.shadows;
                PrefabUtility.RecordPrefabInstancePropertyModifications(light);
                EditorUtility.SetDirty(light);
                restored++;
            }
            BindAndroidQuality(BaselineQualityName, profile);
            GraphicsSettings.defaultRenderPipeline = profile;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the baseline scene.");
            AssetDatabase.SaveAssets();
            Debug.Log("LINGSHAN_BASELINE_PROFILE_APPLIED restoredPointLights=" + restored
                + "; product identity/version are retained; this switches rendering for A/B comparison only.");
        });
    }

    public static Audit CreateAudit(Scene scene)
    {
        UniversalRenderPipelineAsset pipeline = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
        var audit = new Audit
        {
            unityVersion = Application.unityVersion,
            scene = scene.path,
            productName = PlayerSettings.productName,
            packageName = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android),
            version = PlayerSettings.bundleVersion,
            versionCode = PlayerSettings.Android.bundleVersionCode,
            buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
            quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
            androidDefaultQuality = AndroidDefaultQuality(),
            androidEnabledQualities = QualitySettings.GetActiveQualityLevelsForPlatform("Android"),
            pipeline = AssetDatabase.GetAssetPath(pipeline),
            hdr = pipeline != null && pipeline.supportsHDR,
            msaa = pipeline == null ? 0 : pipeline.msaaSampleCount,
            renderScale = pipeline == null ? 0 : pipeline.renderScale,
            depthTexture = pipeline != null && pipeline.supportsCameraDepthTexture,
            opaqueTexture = pipeline != null && pipeline.supportsCameraOpaqueTexture,
            mainShadowResolution = pipeline == null ? 0 : pipeline.mainLightShadowmapResolution,
            shadowCascades = pipeline == null ? 0 : pipeline.shadowCascadeCount,
            shadowDistance = pipeline == null ? 0 : pipeline.shadowDistance,
            additionalLightShadows = pipeline != null && pipeline.supportsAdditionalLightShadows,
            xrSettings = XrSettingsSummary(),
            devicePerformanceVerified = false
        };
        foreach (Light light in Components<Light>(scene))
        {
            audit.totalLights++;
            if (light.enabled && light.gameObject.activeInHierarchy) audit.activeLights++;
            if (light.type != LightType.Point) continue;
            audit.pointLights++;
            if (light.shadows != LightShadows.None) audit.pointLightsWithShadows++;
        }
        foreach (Renderer renderer in Components<Renderer>(scene))
        {
            audit.renderers++;
            if (renderer.enabled && renderer.gameObject.activeInHierarchy) audit.activeRenderers++;
            Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            audit.instancedMeshVertices += mesh.vertexCount;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                if (mesh.GetTopology(submesh) == MeshTopology.Triangles) audit.instancedMeshTriangles += mesh.GetIndexCount(submesh) / 3;
        }
        audit.lodGroups = Components<LODGroup>(scene).Count;
        audit.reflectionProbes = Components<ReflectionProbe>(scene).Count;
        foreach (Terrain terrain in Components<Terrain>(scene))
        {
            TerrainData data = terrain.terrainData;
            audit.terrains.Add(new TerrainAudit
            {
                name = Hierarchy(terrain.transform),
                active = terrain.enabled && terrain.gameObject.activeInHierarchy,
                data = AssetDatabase.GetAssetPath(data),
                material = AssetDatabase.GetAssetPath(terrain.materialTemplate),
                instanced = terrain.drawInstanced,
                pixelError = terrain.heightmapPixelError,
                basemapDistance = terrain.basemapDistance,
                treeDistance = terrain.treeDistance,
                detailDistance = terrain.detailObjectDistance,
                heightmapResolution = data == null ? 0 : data.heightmapResolution,
                treeInstances = data == null ? 0 : data.treeInstanceCount,
                terrainLayers = data == null ? 0 : data.alphamapLayers
            });
        }
        if (pipeline != null)
            foreach (ScriptableRendererData renderer in pipeline.rendererDataList)
                if (renderer != null)
                    foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
                        if (feature != null) audit.rendererFeatures.Add(feature.GetType().Name + ":" + feature.isActive);
        return audit;
    }

    public static void ValidateForBuild()
    {
        if (Resources.Load<Material>("LingshanChallenge/CharmGold") == null ||
            Resources.Load<Material>("LingshanChallenge/CharmLocked") == null ||
            Resources.Load<Material>("LingshanChallenge/ControllerRay") == null)
            throw new InvalidOperationException("Active-collection materials are missing; apply the PICO profile.");
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Wait for compilation and leave Play Mode before building.");
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new InvalidOperationException("Switch the active Unity build target to Android before building.");
        if (PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android) != PackageName)
            throw new InvalidOperationException("Android application identifier changed; preserving install identity is required.");
        if (PlayerSettings.productName != "灵山飞驰" || PlayerSettings.Android.bundleVersionCode < 2
            || !Version.TryParse(PlayerSettings.bundleVersion, out Version version) || version < new Version(0, 2, 0))
            throw new InvalidOperationException("Apply the PICO product name and version profile before building.");
        UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(ProfilePath);
        int quality = Array.IndexOf(QualitySettings.names, QualityName);
        if (pipeline == null || quality < 0 || QualitySettings.GetQualityLevel() != quality
            || AndroidDefaultQuality() != quality || QualitySettings.GetRenderPipelineAssetAt(quality) != pipeline
            || GraphicsSettings.defaultRenderPipeline != pipeline
            || Array.IndexOf(QualitySettings.GetActiveQualityLevelsForPlatform("Android"), quality) < 0)
            throw new InvalidOperationException("PICO profile is not the explicit active/default Android quality.");
        if (pipeline.supportsHDR || pipeline.msaaSampleCount != 2 || pipeline.mainLightShadowmapResolution != 1024
            || pipeline.shadowCascadeCount != 2 || Math.Abs(pipeline.shadowDistance - 25f) > 0.01f
            || pipeline.supportsAdditionalLightShadows)
            throw new InvalidOperationException("PICO render settings differ from the reviewed one-day profile.");
        foreach (ScriptableRendererData renderer in pipeline.rendererDataList)
        {
            if (renderer == null) throw new InvalidOperationException("PICO renderer reference is missing.");
            foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
                if (feature != null && feature.isActive && feature.GetType().Name.IndexOf("AmbientOcclusion", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException("SSAO is still active on the PICO renderer.");
        }
        ValidateXr();
        WithScene(false, scene =>
        {
            if (Components<Camera>(scene).Find(camera => camera.enabled && camera.gameObject.activeInHierarchy) == null
                || Components<RollerCoasterRide>(scene).Count == 0)
                throw new InvalidOperationException("Build scene must contain an active camera and RollerCoasterRide.");
            foreach (Light light in Components<Light>(scene))
                if (light.type == LightType.Point && light.shadows != LightShadows.None)
                    throw new InvalidOperationException("Point light shadows remain on " + Hierarchy(light.transform));
            if (scene.isDirty) throw new InvalidOperationException("Save the reviewed scene before building.");
            Debug.Log("LINGSHAN_PICO_BUILD_CONFIGURATION " + JsonUtility.ToJson(CreateAudit(scene)));
        });
    }

    private static void BindAndroidQuality(string name, UniversalRenderPipelineAsset pipeline)
    {
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        SerializedProperty qualities = Require(settings, "m_QualitySettings");
        int index = -1;
        for (int i = 0; i < qualities.arraySize; i++)
            if (qualities.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name) index = i;
        if (index < 0)
        {
            if (qualities.arraySize == 0) throw new InvalidOperationException("No quality profile is available to clone.");
            index = qualities.arraySize;
            qualities.InsertArrayElementAtIndex(index);
        }
        SerializedProperty quality = qualities.GetArrayElementAtIndex(index);
        quality.FindPropertyRelative("name").stringValue = name;
        quality.FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipeline;
        SerializedProperty excluded = quality.FindPropertyRelative("excludedTargetPlatforms");
        for (int i = excluded.arraySize - 1; i >= 0; i--)
            if (excluded.GetArrayElementAtIndex(i).stringValue == "Android") excluded.DeleteArrayElementAtIndex(i);
        SerializedProperty defaults = Require(settings, "m_PerPlatformDefaultQuality");
        int slot = -1;
        for (int i = 0; i < defaults.arraySize; i++)
            if (defaults.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue == "Android") slot = i;
        if (slot < 0) { slot = defaults.arraySize; defaults.InsertArrayElementAtIndex(slot); }
        defaults.GetArrayElementAtIndex(slot).FindPropertyRelative("first").stringValue = "Android";
        defaults.GetArrayElementAtIndex(slot).FindPropertyRelative("second").intValue = index;
        settings.ApplyModifiedPropertiesWithoutUndo();
        QualitySettings.SetQualityLevel(index, true);
    }

    private static void EnsureChallengeMaterials()
    {
        EnsureFolder("Assets/Resources/LingshanChallenge");
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) throw new InvalidOperationException("URP Unlit shader is unavailable.");
        string[] names = { "CharmGold", "CharmLocked", "ControllerRay" };
        Color[] colors = { new Color(1f, 0.65f, 0.06f), new Color(0.25f, 1f, 0.58f), new Color(0.3f, 0.8f, 1f) };
        for (int i = 0; i < names.Length; i++)
        {
            string path = "Assets/Resources/LingshanChallenge/" + names[i] + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = names[i], enableInstancing = true };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", colors[i]);
            EditorUtility.SetDirty(material);
        }
        AssetDatabase.SaveAssets();
    }

    private static int AndroidDefaultQuality()
    {
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        SerializedProperty defaults = Require(settings, "m_PerPlatformDefaultQuality");
        for (int i = 0; i < defaults.arraySize; i++)
        {
            SerializedProperty pair = defaults.GetArrayElementAtIndex(i);
            if (pair.FindPropertyRelative("first").stringValue == "Android") return pair.FindPropertyRelative("second").intValue;
        }
        return -1;
    }

    private static void ValidateXr()
    {
        UnityEngine.Object settingsAsset = AssetDatabase.LoadMainAssetAtPath("Assets/XR/Settings/PXR_Settings.asset");
        if (settingsAsset == null) throw new InvalidOperationException("PICO XR settings are missing.");
        var settings = new SerializedObject(settingsAsset);
        if (Require(settings, "appMode").intValue != 0 || Require(settings, "stereoRenderingModeAndroid").intValue != 1)
            throw new InvalidOperationException("PICO must retain XR mode and Multiview rendering.");
        var general = UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        if (general == null || !general.InitManagerOnStart || general.AssignedSettings == null)
            throw new InvalidOperationException("Android XR startup/manager settings are missing.");
        bool loaderFound = false;
        foreach (UnityEngine.XR.Management.XRLoader loader in general.AssignedSettings.activeLoaders)
            if (loader != null && loader.GetType().Name == "PXR_Loader") loaderFound = true;
        if (!loaderFound) throw new InvalidOperationException("Android PICO XR loader is not configured.");
    }

    private static string XrSettingsSummary()
    {
        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath("Assets/XR/Settings/PXR_Settings.asset");
        if (asset == null) return "missing";
        var settings = new SerializedObject(asset);
        return "appMode=" + Require(settings, "appMode").intValue
            + ",stereoAndroid=" + Require(settings, "stereoRenderingModeAndroid").intValue
            + ",displayFrequency=" + Require(settings, "systemDisplayFrequency").intValue;
    }

    private static void SaveBaselineOnce(Scene scene)
    {
        if (File.Exists(BaselinePath)) return;
        var baseline = new Baseline
        {
            pipeline = AssetDatabase.GetAssetPath(QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline),
            capturedUtc = DateTime.UtcNow.ToString("O")
        };
        foreach (Light light in Components<Light>(scene))
            if (light.type == LightType.Point)
                baseline.pointLights.Add(new LightBaseline
                {
                    objectId = GlobalObjectId.GetGlobalObjectIdSlow(light).ToString(),
                    hierarchy = Hierarchy(light.transform), shadows = (int)light.shadows
                });
        File.WriteAllText(BaselinePath, JsonUtility.ToJson(baseline, true));
        AssetDatabase.ImportAsset(BaselinePath);
    }

    private static T CopyIfMissing<T>(string source, string destination) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(destination);
        if (asset != null) return asset;
        if (!AssetDatabase.CopyAsset(source, destination)) throw new IOException("Could not copy " + source);
        AssetDatabase.ImportAsset(destination);
        asset = AssetDatabase.LoadAssetAtPath<T>(destination);
        return asset != null ? asset : throw new InvalidOperationException("Copied asset has the wrong type: " + destination);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }

    private static SerializedProperty Require(SerializedObject value, string field)
    {
        return value.FindProperty(field) ?? throw new InvalidOperationException("Unity serialized field not found: " + field);
    }

    private static void WithScene(bool edit, Action<Scene> action)
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool alreadyOpen = scene.IsValid() && scene.isLoaded;
        bool preview = !alreadyOpen && !edit;
        if (!alreadyOpen) scene = preview ? EditorSceneManager.OpenPreviewScene(ScenePath) : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try { action(scene); }
        finally
        {
            if (preview) EditorSceneManager.ClosePreviewScene(scene);
            else if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static List<T> Components<T>(Scene scene) where T : Component
    {
        var components = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects()) components.AddRange(root.GetComponentsInChildren<T>(true));
        return components;
    }

    private static string Hierarchy(Transform transform)
    {
        string name = transform.name;
        while (transform.parent != null) { transform = transform.parent; name = transform.name + "/" + name; }
        return name;
    }

    [Serializable] private sealed class Baseline
    {
        public string pipeline, capturedUtc;
        public List<LightBaseline> pointLights = new List<LightBaseline>();
    }
    [Serializable] private sealed class LightBaseline { public string objectId, hierarchy; public int shadows; }
    [Serializable] public sealed class Audit
    {
        public string unityVersion, scene, productName, packageName, version, buildTarget, quality, pipeline, xrSettings;
        public int versionCode, androidDefaultQuality, msaa, mainShadowResolution, shadowCascades;
        public int[] androidEnabledQualities;
        public float renderScale, shadowDistance;
        public bool hdr, depthTexture, opaqueTexture, additionalLightShadows, devicePerformanceVerified;
        public int totalLights, activeLights, pointLights, pointLightsWithShadows, renderers, activeRenderers, lodGroups, reflectionProbes;
        public long instancedMeshVertices, instancedMeshTriangles;
        public List<string> rendererFeatures = new List<string>();
        public List<TerrainAudit> terrains = new List<TerrainAudit>();
    }
    [Serializable] public sealed class TerrainAudit
    {
        public string name, data, material;
        public bool active, instanced;
        public float pixelError, basemapDistance, treeDistance, detailDistance;
        public int heightmapResolution, treeInstances, terrainLayers;
    }
}
#endif
