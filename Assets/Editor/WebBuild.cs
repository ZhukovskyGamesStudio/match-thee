using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Сборка WebGL в Build/WebGL: меню Match Thee/Build WebGL или из командной строки
// Unity.exe -batchmode -quit -projectPath <проект> -buildTarget WebGL -executeMethod WebBuild.Build
public static class WebBuild {
    public const string OutputPath = "Build/WebGL";

    [MenuItem("Match Thee/Build WebGL")]
    public static void Build() {
        // Gzip с запасным распаковщиком в браузере: работает с любого статического сервера без настройки заголовков.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.runInBackground = true;

        string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        BuildReport report = BuildPipeline.BuildPlayer(scenes, OutputPath, BuildTarget.WebGL, BuildOptions.None);
        BuildSummary summary = report.summary;
        Debug.Log($"Match Thee: WebGL {summary.result}, {summary.totalSize / (1024f * 1024f):F1} MB, {summary.totalTime.TotalSeconds:F0} с, ошибок {summary.totalErrors}");
        if (summary.result != BuildResult.Succeeded && Application.isBatchMode) {
            EditorApplication.Exit(1);
        }
    }
}
