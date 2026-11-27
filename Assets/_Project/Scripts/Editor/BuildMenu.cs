using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Builds the main scene (Room_Scene) as a Windows 64-bit player into Builds/Windows/.
public static class BuildMenu
{
    const string ScenePath = "Assets/_Project/Scenes/Room_Scene.unity";
    const string OutputDir = "Builds/Windows";
    const string ExeName = "ET_VR_TOOL.exe";

    [MenuItem("Tools/Einstein Telescope/Build/Windows (Room_Scene)")]
    public static void BuildWindows()
    {
        if (!File.Exists(ScenePath))
        {
            Debug.LogError("Build: scene not found: " + ScenePath);
            return;
        }

        Directory.CreateDirectory(OutputDir);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(OutputDir, ExeName),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"Build succeeded: {summary.outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime:mm\\:ss})");
            EditorUtility.RevealInFinder(summary.outputPath);
        }
        else
        {
            Debug.LogError($"Build {summary.result}: {summary.totalErrors} errors, {summary.totalWarnings} warnings. See Console.");
        }
    }
}
