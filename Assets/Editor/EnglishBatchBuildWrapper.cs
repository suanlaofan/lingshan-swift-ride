#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 英文版 batchmode 构建包装：仅负责打开构建场景并复制产物，
/// 实际构建完全复用工程自带的 CodexAndroidBuild.BuildPicoApk（场景列表、Player 设置、签名不变）。
/// </summary>
public static class EnglishBatchBuildWrapper
{
    private const string OutputDir = "/Users/liuzhenjia/Downloads/Unity_Games_English_2026-10-22/LingshanSwiftRide/APK";

    public static void BuildEnglishPicoApk()
    {
        // 原版 Validate() 通过 FindObjectsByType 检查已加载场景，batchmode 默认是空场景，因此先打开构建场景
        EditorSceneManager.OpenScene(LingshanPicoRemediation.ScenePath, OpenSceneMode.Single);

        // 原版 BindAndroidQuality 只在编辑器会话内存中写入 Android 默认画质；已提交的 QualitySettings.asset 中该映射为空，
        // batchmode 读取为 -1 导致校验失败。这里与原版一致，仅把 Android 默认画质指向唯一的 PICO 档位，不改其它设置。
        BindAndroidDefaultQuality(LingshanPicoRemediation.QualityName);

        CodexAndroidBuild.BuildPicoApk();

        string source = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "Builds/PICO/LingshanSwiftRide-PICO.apk"));
        if (!File.Exists(source))
            throw new FileNotFoundException("Expected APK from CodexAndroidBuild was not produced.", source);

        Directory.CreateDirectory(OutputDir);
        string target = Path.Combine(OutputDir, "LingshanSwiftRide-" + PlayerSettings.bundleVersion + "-en.apk");
        File.Copy(source, target, true);
        Debug.Log("ENGLISH_BUILD_WRAPPER_OUTPUT " + target);
    }

    private static void BindAndroidDefaultQuality(string qualityName)
    {
        int index = System.Array.IndexOf(QualitySettings.names, qualityName);
        if (index < 0) throw new System.InvalidOperationException("Quality level missing: " + qualityName);
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        SerializedProperty defaults = settings.FindProperty("m_PerPlatformDefaultQuality");
        int slot = -1;
        for (int i = 0; i < defaults.arraySize; i++)
            if (defaults.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue == "Android") slot = i;
        if (slot < 0) { slot = defaults.arraySize; defaults.InsertArrayElementAtIndex(slot); }
        defaults.GetArrayElementAtIndex(slot).FindPropertyRelative("first").stringValue = "Android";
        defaults.GetArrayElementAtIndex(slot).FindPropertyRelative("second").intValue = index;
        settings.ApplyModifiedPropertiesWithoutUndo();
        QualitySettings.SetQualityLevel(index, true);
        Debug.Log("ENGLISH_BUILD_WRAPPER_ANDROID_DEFAULT_QUALITY " + qualityName + "=" + index);
    }
}
#endif
