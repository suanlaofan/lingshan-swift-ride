#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Read-only diagnostic for terrain ownership, material templates and TerrainLayer
/// values. Kept separate from the repair tool so visual decisions are based on
/// the exact scene object selected by the user.
/// </summary>
public static class LingshanTerrainAudit
{
    private const string ScenePath = "Assets/Scenes/EnvironmentScene.unity";

    public static void Report()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var terrains = new List<Terrain>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            terrains.AddRange(root.GetComponentsInChildren<Terrain>(true));
        }

        var report = new StringBuilder();
        report.AppendLine("TERRAIN_AUDIT_BEGIN");
        report.AppendLine("Count=" + terrains.Count);
        foreach (Terrain terrain in terrains)
        {
            AppendTerrain(report, terrain);
        }

        report.AppendLine("TERRAIN_AUDIT_END");
        Debug.Log(report.ToString());
    }

    private static void AppendTerrain(StringBuilder report, Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        report.AppendLine("--- TERRAIN " + GetHierarchyPath(terrain.transform));
        report.AppendLine("dataPath=" + AssetDatabase.GetAssetPath(data));
        report.AppendLine("dataName=" + (data == null ? "<null>" : data.name));
        report.AppendLine("materialPath=" + AssetDatabase.GetAssetPath(terrain.materialTemplate));
        report.AppendLine("shader=" + (terrain.materialTemplate == null || terrain.materialTemplate.shader == null
            ? "<null>" : terrain.materialTemplate.shader.name));
        report.AppendLine("drawInstanced=" + terrain.drawInstanced + ", reflectionProbeUsage=" + terrain.reflectionProbeUsage);

        if (data == null)
        {
            return;
        }

        report.AppendLine("size=" + data.size + ", heightmapResolution=" + data.heightmapResolution
            + ", alphamapResolution=" + data.alphamapResolution + ", alphamapLayers=" + data.alphamapLayers);
        TerrainLayer[] layers = data.terrainLayers;
        report.AppendLine("layerCount=" + (layers == null ? 0 : layers.Length));
        if (layers == null)
        {
            return;
        }

        for (int index = 0; index < layers.Length; index++)
        {
            TerrainLayer layer = layers[index];
            if (layer == null)
            {
                report.AppendLine("layer[" + index + "]=<null>");
                continue;
            }

            report.AppendLine("layer[" + index + "] path=" + AssetDatabase.GetAssetPath(layer)
                + "; name=" + layer.name
                + "; diffuse=" + AssetDatabase.GetAssetPath(layer.diffuseTexture)
                + "; normal=" + AssetDatabase.GetAssetPath(layer.normalMapTexture)
                + "; mask=" + AssetDatabase.GetAssetPath(layer.maskMapTexture)
                + "; smoothness=" + layer.smoothness
                + "; metallic=" + layer.metallic
                + "; normalScale=" + layer.normalScale
                + "; tile=" + layer.tileSize);
        }
    }

    private static string GetHierarchyPath(Transform current)
    {
        var parts = new List<string>();
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }
}
#endif
