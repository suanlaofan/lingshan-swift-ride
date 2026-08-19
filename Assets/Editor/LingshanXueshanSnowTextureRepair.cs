#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies the approved replacement snow albedo to dibian/xueshan only.
/// The existing terrain data, alphamap and all other TerrainLayers remain intact.
/// </summary>
public static class LingshanXueshanSnowTextureRepair
{
    private const string ScenePath = "Assets/Scenes/EnvironmentScene.unity";
    private const string TerrainDataPath = "Assets/New Terrain.asset";
    private const string SnowLayerPath = "Assets/Art/Terrain/Source/NewLayer.terrainlayer";
    private const string SnowAlbedoPath = "Assets/Art/Terrain/Source/Snow_Ridged_Albedo.jpg";

    [MenuItem("Tools/Lingshan/Replace Xueshan Snow Albedo")]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TerrainData terrainData = LoadRequired<TerrainData>(TerrainDataPath);
        TerrainLayer snowLayer = LoadRequired<TerrainLayer>(SnowLayerPath);
        Texture2D snowAlbedo = LoadRequired<Texture2D>(SnowAlbedoPath);
        Terrain xueshan = FindTerrain(scene, "dibian/xueshan");

        if (xueshan == null || xueshan.terrainData != terrainData)
        {
            throw new InvalidOperationException("Expected dibian/xueshan terrain binding was not found.");
        }

        TerrainLayer[] layers = terrainData.terrainLayers ?? Array.Empty<TerrainLayer>();
        if (Array.IndexOf(layers, snowLayer) < 0)
        {
            throw new InvalidOperationException("The target snow layer is not used by Assets/New Terrain.asset.");
        }

        // This is intentionally a diffuse-only replacement. The layer remains matte and
        // has no alpha-derived smoothness, so the replacement cannot reintroduce wet shine.
        snowLayer.diffuseTexture = snowAlbedo;
        snowLayer.normalMapTexture = null;
        snowLayer.maskMapTexture = null;
        snowLayer.tileSize = new Vector2(8f, 8f);
        snowLayer.tileOffset = Vector2.zero;
        snowLayer.metallic = 0f;
        snowLayer.specular = Color.black;
        snowLayer.smoothness = 0.04f;
        snowLayer.normalScale = 1f;
        snowLayer.smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly;
        EditorUtility.SetDirty(snowLayer);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Verify(scene, terrainData, snowLayer, snowAlbedo);
        Debug.Log("XUESHAN_SNOW_TEXTURE_REPLACE_OK: " + SnowAlbedoPath
            + "; normal=<none>; tile=8x8; smoothness=0.04; source=ConstantOnly");
    }

    [MenuItem("Tools/Lingshan/Verify Xueshan Snow Albedo")]
    public static void VerifyOnly()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        TerrainData terrainData = LoadRequired<TerrainData>(TerrainDataPath);
        TerrainLayer snowLayer = LoadRequired<TerrainLayer>(SnowLayerPath);
        Texture2D snowAlbedo = LoadRequired<Texture2D>(SnowAlbedoPath);
        Verify(scene, terrainData, snowLayer, snowAlbedo);
        Debug.Log("XUESHAN_SNOW_TEXTURE_VERIFY_OK: dibian/xueshan uses Snow_Ridged_Albedo only.");
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

    private static Terrain FindTerrain(Scene scene, string hierarchyPath)
    {
        string[] names = hierarchyPath.Split('/');
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!string.Equals(root.name, names[0], StringComparison.Ordinal))
            {
                continue;
            }

            Transform current = root.transform;
            for (int index = 1; index < names.Length && current != null; index++)
            {
                current = current.Find(names[index]);
            }

            return current == null ? null : current.GetComponent<Terrain>();
        }

        return null;
    }

    private static void Verify(Scene scene, TerrainData terrainData, TerrainLayer snowLayer, Texture2D snowAlbedo)
    {
        Terrain xueshan = FindTerrain(scene, "dibian/xueshan");
        if (xueshan == null || xueshan.terrainData != terrainData)
        {
            throw new InvalidOperationException("dibian/xueshan terrain binding changed unexpectedly.");
        }

        if (Array.IndexOf(terrainData.terrainLayers ?? Array.Empty<TerrainLayer>(), snowLayer) < 0)
        {
            throw new InvalidOperationException("The target snow layer is no longer assigned to xueshan.");
        }

        if (snowLayer.diffuseTexture != snowAlbedo
            || snowLayer.normalMapTexture != null
            || snowLayer.maskMapTexture != null
            || snowLayer.smoothnessSource != TerrainLayerSmoothnessSource.ConstantOnly
            || snowLayer.metallic != 0f
            || snowLayer.specular != Color.black
            || Mathf.Abs(snowLayer.smoothness - 0.04f) > 0.0001f
            || snowLayer.tileSize != new Vector2(8f, 8f))
        {
            throw new InvalidOperationException("xueshan snow layer did not converge to the intended replacement settings.");
        }
    }
}
#endif
