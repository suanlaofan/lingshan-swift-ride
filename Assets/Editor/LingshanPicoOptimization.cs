#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Explicit second-pass optimization. Source FBX and original textures remain intact.</summary>
public static class LingshanPicoOptimization
{
    public const string MeshFolder = "Assets/Art/PICOOptimized";
    public const string IconPath = "Assets/UI/Branding/LingshanIcon.png";
    private const string SourceFragment = "tripo_convert_a8fc79f3-b0b5-45bd-a817-50b48b1bcc64.fbx";

    public static void ImportMeshes(string directory)
    {
        Directory.CreateDirectory(MeshFolder);
        AssetDatabase.Refresh();
        for (int lod = 0; lod < 3; lod++)
        {
            using (var reader = new BinaryReader(File.OpenRead(Path.Combine(directory, "LOD" + lod + ".bin"))))
            {
                int count = reader.ReadInt32(), indexCount = reader.ReadInt32();
                if (count < 3 || count > 500000 || indexCount % 3 != 0) throw new InvalidDataException("Invalid optimized mesh");
                var vertices = new Vector3[count]; var normals = new Vector3[count]; var uv = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                }
                var indices = new int[indexCount];
                for (int i = 0; i < indexCount; i++) { indices[i] = reader.ReadInt32(); if (indices[i] < 0 || indices[i] >= count) throw new InvalidDataException("Invalid mesh index"); }
                string path = MeshFolder + "/LingshanArchitecture_LOD" + lod + ".asset";
                var mesh = new Mesh { name = "LingshanArchitecture_LOD" + lod, indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = indices;
                mesh.RecalculateBounds(); mesh.RecalculateTangents(); mesh.Optimize();
                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null) AssetDatabase.CreateAsset(mesh, path);
                else { EditorUtility.CopySerialized(mesh, existing); EditorUtility.SetDirty(existing); UnityEngine.Object.DestroyImmediate(mesh); }
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("LINGSHAN_OPTIMIZED_MESHES_IMPORTED");
    }

    [MenuItem("Tools/Lingshan/PICO/Apply Geometry and Icon Optimization")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != LingshanPicoRemediation.ScenePath) throw new InvalidOperationException("Wrong scene");
        var meshes = new Mesh[3];
        for (int i = 0; i < 3; i++)
        {
            meshes[i] = AssetDatabase.LoadAssetAtPath<Mesh>(MeshFolder + "/LingshanArchitecture_LOD" + i + ".asset");
            if (meshes[i] == null) throw new InvalidOperationException("LOD mesh missing");
        }
        int count = 0;
        foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            if (filter.gameObject.scene != scene || filter.sharedMesh == null) continue;
            bool source = AssetDatabase.GetAssetPath(filter.sharedMesh).EndsWith(SourceFragment, StringComparison.Ordinal);
            bool existing = filter.sharedMesh == meshes[0];
            if (!source && !existing) continue;
            Mesh original = filter.sharedMesh;
            if (source && (original.bounds.center - meshes[0].bounds.center).magnitude > original.bounds.size.magnitude * 0.02f)
                throw new InvalidOperationException("Optimized mesh local-space bounds differ from source");
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null) throw new InvalidOperationException("Expected a static mesh renderer");
            Undo.RecordObject(filter, "Lingshan optimized geometry"); filter.sharedMesh = meshes[0];
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            foreach (var collider in filter.GetComponents<MeshCollider>())
                if (collider.sharedMesh == original) { Undo.RecordObject(collider, "Lingshan optimized collider"); collider.sharedMesh = meshes[1]; PrefabUtility.RecordPrefabInstancePropertyModifications(collider); }
            var lods = new LOD[3]; lods[0] = new LOD(0.25f, new Renderer[] { renderer });
            for (int i = 1; i < 3; i++)
            {
                var child = filter.transform.Find("PICO LOD" + i);
                if (child == null) { var go = new GameObject("PICO LOD" + i); Undo.RegisterCreatedObjectUndo(go, "Lingshan LOD"); child = go.transform; child.SetParent(filter.transform, false); }
                var mf = child.GetComponent<MeshFilter>(); if (mf == null) mf = child.gameObject.AddComponent<MeshFilter>();
                var mr = child.GetComponent<MeshRenderer>(); if (mr == null) mr = child.gameObject.AddComponent<MeshRenderer>();
                mf.sharedMesh = meshes[i]; mr.sharedMaterials = renderer.sharedMaterials; mr.shadowCastingMode = renderer.shadowCastingMode; mr.receiveShadows = renderer.receiveShadows;
                child.gameObject.layer = filter.gameObject.layer;
                lods[i] = new LOD(i == 1 ? 0.06f : 0f, new Renderer[] { mr });
            }
            var group = filter.GetComponent<LODGroup>(); if (group == null) group = Undo.AddComponent<LODGroup>(filter.gameObject);
            group.fadeMode = LODFadeMode.None; group.SetLODs(lods); group.RecalculateBounds();
            count++;
        }
        if (count != 2) throw new InvalidOperationException("Expected exactly two architecture instances, got " + count);
        foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            if (terrain.gameObject.scene != scene) continue;
            Undo.RecordObject(terrain, "Lingshan terrain detail budget");
            terrain.heightmapPixelError = Mathf.Max(8f, terrain.heightmapPixelError);
            terrain.basemapDistance = Mathf.Min(250f, terrain.basemapDistance);
            terrain.detailObjectDistance = Mathf.Min(60f, terrain.detailObjectDistance);
            if (terrain.materialTemplate != null && terrain.materialTemplate.shader.name == "Universal Render Pipeline/Terrain/Lit") terrain.drawInstanced = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(terrain);
        }
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(LingshanPicoRemediation.ProfilePath);
        pipeline.supportsCameraDepthTexture = false; pipeline.supportsCameraOpaqueTexture = false;
        var settings = new SerializedObject(pipeline);
        foreach (string flag in new[] { "m_ReflectionProbeBlending", "m_ReflectionProbeBoxProjection", "m_ReflectionProbeAtlas" })
        { var field = settings.FindProperty(flag); if (field != null) field.boolValue = false; }
        settings.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(pipeline);
        PlayerSettings.bundleVersion = "0.2.1"; PlayerSettings.Android.bundleVersionCode = Math.Max(3, PlayerSettings.Android.bundleVersionCode);
        PlayerSettings.enableFrameTimingStats = true;
        ApplyIcons();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Debug.Log("LINGSHAN_SECOND_OPTIMIZATION_APPLIED instances=" + count + " lod0Triangles=" + meshes[0].triangles.Length / 3);
    }

    [MenuItem("Tools/Lingshan/PICO/Apply Game Icon")]
    public static void ApplyIcons()
    {
        Texture2D artwork = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (artwork == null) throw new BuildFailedException("Lingshan game icon missing: " + IconPath);
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { artwork }, IconKind.Any);
        foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
        {
            PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            foreach (PlatformIcon icon in icons)
            {
                // The supplied finished artwork is opaque. Both adaptive layers use it,
                // so the foreground never exposes Unity's default icon underneath.
                var layers = new Texture2D[Math.Max(icon.minLayerCount, icon.layerCount)];
                for (int i = 0; i < layers.Length; i++) layers[i] = artwork;
                icon.SetTextures(layers);
            }
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
        }
        AssetDatabase.SaveAssets();
    }

    public static void Validate()
    {
        int groups = 0;
        foreach (var group in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
        {
            if (group.transform.Find("PICO LOD1") == null) continue;
            var lods = group.GetLODs(); if (lods.Length != 3) throw new BuildFailedException("Architecture LOD levels missing");
            foreach (var lod in lods) foreach (var r in lod.renderers)
            { var mesh = r.GetComponent<MeshFilter>().sharedMesh; if (mesh == null || mesh.triangles.Length / 3 > 70000) throw new BuildFailedException("Architecture exceeds triangle budget"); }
            groups++;
        }
        if (groups != 2) throw new BuildFailedException("Expected both optimized architecture instances");
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            foreach (var slot in PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind))
                if (slot.GetTexture(0) != icon) throw new BuildFailedException("Game icon is not bound to every Android slot");
        Debug.Log("LINGSHAN_GEOMETRY_ICON_VALIDATION_PASS groups=2 levels=3");
    }
}
#endif
