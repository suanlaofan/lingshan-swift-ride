#if UNITY_EDITOR
using System.IO;
using System;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CodexAndroidBuild
{
    [MenuItem("Tools/Lingshan/Build PICO APK")]
    public static void BuildPicoApk()
    {
        LingshanPicoOptimization.ApplyIcons();
        LingshanPicoRemediation.ValidateForBuild();
        LingshanPicoOptimization.Validate();
        // A release APK is always built with explicit options, independently of the
        // Development Build checkbox in the editor's previous build configuration.
        string outputPath = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "Builds/PICO/LingshanSwiftRide-PICO.apk"));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { LingshanPicoRemediation.ScenePath },
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

        if ((report.summary.options & BuildOptions.Development) != 0)
            throw new InvalidOperationException("Expected a non-Development APK.");
        string hash;
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(outputPath))
            hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        var receipt = new BuildReceipt
        {
            completedUtc = DateTime.UtcNow.ToString("O"),
            apk = outputPath,
            sha256 = hash,
            bytes = new FileInfo(outputPath).Length,
            unityVersion = Application.unityVersion,
            productName = PlayerSettings.productName,
            packageName = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android),
            version = PlayerSettings.bundleVersion,
            versionCode = PlayerSettings.Android.bundleVersionCode,
            pipeline = LingshanPicoRemediation.ProfilePath,
            scene = LingshanPicoRemediation.ScenePath,
            developmentBuild = false,
            devicePerformanceVerified = false
        };
        File.WriteAllText(Path.ChangeExtension(outputPath, ".build.json"), JsonUtility.ToJson(receipt, true));
        Debug.Log("LINGSHAN_PICO_BUILD_SUCCESS " + JsonUtility.ToJson(receipt));
    }

    [Serializable]
    private sealed class BuildReceipt
    {
        public string completedUtc, apk, sha256, unityVersion, productName, packageName, version, pipeline, scene;
        public long bytes;
        public int versionCode;
        public bool developmentBuild, devicePerformanceVerified;
    }
}
#endif
