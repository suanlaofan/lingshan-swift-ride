using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RollerCoasterAudioInstaller
{
    private const string EnvironmentScenePath = "Assets/Scenes/EnvironmentScene.unity";
    private const string AudioFolder = "Assets/Resources/RollerCoasterAudio";
    private const string RollerCoasterRootName = "RollerCoaster_Placeholder";

    private static readonly string[] StreamingClipNames =
    {
        "Ancient_Mystery_BGM",
        "Rail_Rumble_Loop",
        "Mountain_Wind_Loop"
    };

    static RollerCoasterAudioInstaller()
    {
        EditorApplication.update += TryAutomaticInstall;
    }

    [MenuItem("Tools/Environment/Install Roller Coaster Audio")]
    public static void InstallFromMenu()
    {
        ConfigureAudioImportSettings();
        if (!InstallInActiveScene(true))
        {
            Debug.LogError("Roller coaster audio requires EnvironmentScene with RollerCoaster_Placeholder loaded.");
        }
    }

    public static void ConfigureForBuilder(
        GameObject rollerCoasterRoot,
        RollerCoasterRide ride,
        Transform vehicleRoot)
    {
        if (rollerCoasterRoot == null || ride == null || vehicleRoot == null)
        {
            return;
        }

        RollerCoasterAudioDirector director = rollerCoasterRoot.GetComponent<RollerCoasterAudioDirector>();
        if (director == null)
        {
            director = rollerCoasterRoot.AddComponent<RollerCoasterAudioDirector>();
        }

        director.Configure(ride, vehicleRoot);
        EditorUtility.SetDirty(director);
    }

    private static void TryAutomaticInstall()
    {
        if (EditorApplication.isCompiling ||
            EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return;
        }

        EditorApplication.update -= TryAutomaticInstall;
        if (scene.path != EnvironmentScenePath)
        {
            return;
        }

        ConfigureAudioImportSettings();
        InstallInActiveScene(false);
    }

    private static bool InstallInActiveScene(bool force)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != EnvironmentScenePath)
        {
            return false;
        }

        GameObject root = scene.GetRootGameObjects()
            .FirstOrDefault(candidate => candidate.name == RollerCoasterRootName);
        if (root == null)
        {
            return false;
        }

        RollerCoasterRide ride = root.GetComponent<RollerCoasterRide>();
        Transform vehicleRoot = root.transform.Find("RideVehicle");
        if (ride == null || vehicleRoot == null)
        {
            return false;
        }

        RollerCoasterAudioDirector director = root.GetComponent<RollerCoasterAudioDirector>();
        if (!force && director != null && director.HasCompleteConfiguration)
        {
            return true;
        }

        if (director == null)
        {
            director = root.AddComponent<RollerCoasterAudioDirector>();
        }

        director.Configure(ride, vehicleRoot);
        if (!director.HasCompleteConfiguration)
        {
            Debug.LogError("Roller coaster audio clips were not imported completely; installation will retry after refresh.");
            UnityEngine.Object.DestroyImmediate(director);
            return false;
        }

        EditorUtility.SetDirty(director);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Roller coaster audio installed: 4 continuous layers and 13 event clips are active.");
        return true;
    }

    private static void ConfigureAudioImportSettings()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
            {
                continue;
            }

            bool streaming = StreamingClipNames.Any(name =>
                string.Equals(name, System.IO.Path.GetFileNameWithoutExtension(path), StringComparison.Ordinal));
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            AudioClipLoadType targetLoadType = streaming
                ? AudioClipLoadType.Streaming
                : AudioClipLoadType.CompressedInMemory;
            float targetQuality = streaming ? 0.68f : 0.82f;

            bool changed =
                settings.loadType != targetLoadType ||
                settings.compressionFormat != AudioCompressionFormat.Vorbis ||
                settings.sampleRateSetting != AudioSampleRateSetting.OptimizeSampleRate ||
                !Mathf.Approximately(settings.quality, targetQuality) ||
                importer.forceToMono ||
                importer.loadInBackground != streaming ||
                settings.preloadAudioData == streaming;

            if (!changed)
            {
                continue;
            }

            settings.loadType = targetLoadType;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
            settings.quality = targetQuality;
            settings.preloadAudioData = !streaming;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.loadInBackground = streaming;
            importer.SaveAndReimport();
        }
    }
}
