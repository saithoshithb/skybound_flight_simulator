using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Builds the Windows game. The result runs on its own, outside the Editor and Unity Hub.</summary>
public static class SkyboundBuild
{
    const string OutputFolder = "Builds/Windows";
    const string ExeName = "SkyboundMotion.exe";
    const string FlightScenePath = "Assets/Scenes/Flight.unity";
    const int WINDOW_WIDTH = 1280;
    const int WINDOW_HEIGHT = 720;

    [MenuItem("Skybound/Build Windows Game")]
    public static void BuildFromMenu()
    {
        var report = Build();
        if (report != null && report.summary.result == BuildResult.Succeeded && !Application.isBatchMode)
            EditorUtility.RevealInFinder(Path.Combine(OutputFolder, ExeName));
    }

    /// <summary>For a build from the command line. Exits non-zero if it fails.</summary>
    public static void Run()
    {
        var report = Build();
        if (report == null || report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    /// <summary>
    /// What the game needs from the player settings. It keeps running without focus so the serial link
    /// stays drained; the flight itself pauses when focus goes (App.OnApplicationFocus).
    /// </summary>
    public static void ApplyPlayerSettings()
    {
        PlayerSettings.companyName = "Skybound";
        PlayerSettings.productName = "Skybound Motion";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.skybound.motion");
        // System.IO.Ports is only in this profile. A .NET Standard build can't reach the COM port.
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Unity_4_8);
        PlayerSettings.runInBackground = true;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.defaultScreenWidth = WINDOW_WIDTH;
        PlayerSettings.defaultScreenHeight = WINDOW_HEIGHT;
        PlayerSettings.forceSingleInstance = true;
    }

    static BuildReport Build()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[Skybound] Stop Play mode first, then build.");
            return null;
        }
        SkyboundSceneSetup.Run();
        ApplyPlayerSettings();
        Directory.CreateDirectory(OutputFolder);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { FlightScenePath },
            locationPathName = Path.Combine(OutputFolder, ExeName),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        });
        var s = report.summary;
        var text = $"[Skybound] Build {s.result}: {s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalMinutes:0.0} min. {s.outputPath}";
        if (s.result == BuildResult.Succeeded) Debug.Log(text);
        else Debug.LogError(text);
        return report;
    }
}
