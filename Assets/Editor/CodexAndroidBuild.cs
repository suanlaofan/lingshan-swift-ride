#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class CodexAndroidBuild
{
    [MenuItem("Tools/Lingshan/Build PICO APK")]
    public static void BuildPicoApk()
    {
        string outputPath = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "Builds/PICO/LingshanSwiftRide-PICO.apk"));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/EnvironmentScene.unity" },
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.StrictMode
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new System.Exception(
                "PICO APK build failed: " + report.summary.result +
                " (" + report.summary.totalErrors + " errors)");
        }

        UnityEngine.Debug.Log(
            "PICO APK build succeeded: " + outputPath +
            " (" + report.summary.totalSize + " bytes)");
    }
}
#endif
