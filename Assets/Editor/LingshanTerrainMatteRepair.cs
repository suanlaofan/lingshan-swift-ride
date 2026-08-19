#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Restores dry, matte terrain shading for the current EnvironmentScene.
///
/// URP Terrain Lit treats the alpha channel of a diffuse texture as smoothness
/// when a TerrainLayer uses DiffuseAlphaChannel. The terrain textures in this
/// project do not provide an alpha channel, so that mode evaluates to 1.0 and
/// makes all terrain appear wet. This tool switches only the TerrainLayers used
/// by the active scene to their existing constant smoothness values. It does not
/// touch terrain heights, splat weights, transforms, colliders, tracks, or UI.
/// </summary>
public static class LingshanTerrainMatteRepair
{
    private const string EnvironmentScenePath = "Assets/Scenes/EnvironmentScene.unity";
    private const string XueshanTerrainDataPath = "Assets/New Terrain.asset";
    private const string MistTerrainDataPath = "Assets/New Terrain 1.asset";
    private const string XueshanSnowLayerPath = "Assets/Art/Terrain/Source/NewLayer.terrainlayer";

    [MenuItem("Tools/Lingshan/Restore Matte Terrain And Xueshan")]
    public static void RestoreMatteTerrainAndXueshan()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != EnvironmentScenePath)
        {
            throw new InvalidOperationException(
                "Open " + EnvironmentScenePath + " before applying the terrain repair. Active scene: " + scene.path);
        }

        TerrainData xueshanData = LoadRequired<TerrainData>(XueshanTerrainDataPath);
        TerrainData mistData = LoadRequired<TerrainData>(MistTerrainDataPath);
        TerrainLayer xueshanSnowLayer = LoadRequired<TerrainLayer>(XueshanSnowLayerPath);

        Terrain xueshan = FindTerrain("dibian", "xueshan");
        if (xueshan == null || xueshan.terrainData != xueshanData)
        {
            throw new InvalidOperationException("dibian/xueshan is not bound to the expected terrain data.");
        }

        if (Array.IndexOf(xueshanData.terrainLayers, xueshanSnowLayer) < 0)
        {
            throw new InvalidOperationException("The existing xueshan snow layer was not found on Assets/New Terrain.asset.");
        }

        // Undo only the accidental edit to the unrelated heliu fog terrain.
        // It originally had no TerrainLayer and must not receive the xueshan snow layer.
        mistData.terrainLayers = Array.Empty<TerrainLayer>();
        EditorUtility.SetDirty(mistData);

        var usedLayers = CollectActiveSceneTerrainLayers(scene);
        foreach (TerrainLayer layer in usedLayers)
        {
            layer.smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly;
            EditorUtility.SetDirty(layer);
        }

        // xueshan's only snow layer: dry snow is non-metallic and almost matte.
        // All other xueshan layers retain their authored constant smoothness.
        xueshanSnowLayer.metallic = 0f;
        xueshanSnowLayer.specular = Color.black;
        xueshanSnowLayer.smoothness = 0.04f;
        xueshanSnowLayer.smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly;
        EditorUtility.SetDirty(xueshanSnowLayer);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Verify(scene, xueshanData, mistData, xueshanSnowLayer);
        Debug.Log(BuildReport(scene, xueshanData, mistData, xueshanSnowLayer));
    }

    [MenuItem("Tools/Lingshan/Verify Matte Terrain And Xueshan")]
    public static void VerifyOnly()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != EnvironmentScenePath)
        {
            throw new InvalidOperationException("Open " + EnvironmentScenePath + " before verifying terrain.");
        }

        TerrainData xueshanData = LoadRequired<TerrainData>(XueshanTerrainDataPath);
        TerrainData mistData = LoadRequired<TerrainData>(MistTerrainDataPath);
        TerrainLayer xueshanSnowLayer = LoadRequired<TerrainLayer>(XueshanSnowLayerPath);
        Verify(scene, xueshanData, mistData, xueshanSnowLayer);
        Debug.Log("MATTE_TERRAIN_VERIFY_OK: all active TerrainLayers use authored constant smoothness; dibian/xueshan snow is dry and heliu has no accidental snow layer.");
    }

    private static T LoadRequired<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            throw new InvalidOperationException("Missing required asset: " + path);
        }

        return asset;
    }

    private static Terrain FindTerrain(string rootName, string terrainName)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name != rootName)
            {
                continue;
            }

            Transform child = root.transform.Find(terrainName);
            return child == null ? null : child.GetComponent<Terrain>();
        }

        return null;
    }

    private static HashSet<TerrainLayer> CollectActiveSceneTerrainLayers(Scene scene)
    {
        var layers = new HashSet<TerrainLayer>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                if (terrain.terrainData == null || terrain.terrainData.terrainLayers == null)
                {
                    continue;
                }

                foreach (TerrainLayer layer in terrain.terrainData.terrainLayers)
                {
                    if (layer != null)
                    {
                        layers.Add(layer);
                    }
                }
            }
        }

        return layers;
    }

    private static void Verify(Scene scene, TerrainData xueshanData, TerrainData mistData, TerrainLayer xueshanSnowLayer)
    {
        Terrain xueshan = FindTerrain("dibian", "xueshan");
        if (xueshan == null || xueshan.terrainData != xueshanData)
        {
            throw new InvalidOperationException("dibian/xueshan terrain binding changed unexpectedly.");
        }

        if (mistData.terrainLayers == null || mistData.terrainLayers.Length != 0 || mistData.alphamapLayers != 0)
        {
            throw new InvalidOperationException("The accidental heliu snow-layer edit was not fully restored.");
        }

        if (xueshanSnowLayer.metallic != 0f
            || xueshanSnowLayer.smoothnessSource != TerrainLayerSmoothnessSource.ConstantOnly
            || Mathf.Abs(xueshanSnowLayer.smoothness - 0.04f) > 0.0001f)
        {
            throw new InvalidOperationException("xueshan snow layer is not using the intended dry matte settings.");
        }

        foreach (TerrainLayer layer in CollectActiveSceneTerrainLayers(scene))
        {
            if (layer.smoothnessSource != TerrainLayerSmoothnessSource.ConstantOnly)
            {
                throw new InvalidOperationException("TerrainLayer still samples a diffuse alpha channel for smoothness: "
                    + AssetDatabase.GetAssetPath(layer));
            }
        }
    }

    private static string BuildReport(Scene scene, TerrainData xueshanData, TerrainData mistData, TerrainLayer xueshanSnowLayer)
    {
        var report = new StringBuilder();
        report.AppendLine("MATTE_TERRAIN_REPAIR_OK");
        report.AppendLine("scene=" + scene.path);
        report.AppendLine("xueshanData=" + AssetDatabase.GetAssetPath(xueshanData));
        report.AppendLine("xueshanSnow=" + AssetDatabase.GetAssetPath(xueshanSnowLayer)
            + "; metallic=" + xueshanSnowLayer.metallic
            + "; smoothness=" + xueshanSnowLayer.smoothness
            + "; source=" + xueshanSnowLayer.smoothnessSource);
        report.AppendLine("restoredMistTerrain=" + AssetDatabase.GetAssetPath(mistData)
            + "; layerCount=" + mistData.terrainLayers.Length);
        report.AppendLine("activeSceneLayerCount=" + CollectActiveSceneTerrainLayers(scene).Count);
        return report.ToString();
    }
}
#endif
