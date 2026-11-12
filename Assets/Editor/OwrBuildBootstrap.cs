using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// One-shot headless player builder: launches with -owr-build, compiles, builds the enabled
/// scenes into Builds/Linux, logs a grep-able verdict line, exits 0 on success. Temporary tool.
/// </summary>
internal static class OwrBuildBootstrap
{
    [InitializeOnLoadMethod]
    private static void Kick()
    {
        if (!Environment.GetCommandLineArgs().Contains("-owr-build")) return;
        EditorApplication.delayCall += () =>
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "/home/p620/UnityProjects/OpenWheelRacer/Builds/Linux/OpenWheelRacer.x86_64",
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            Debug.Log($"[OWR-BUILD] {s.result} scenes={scenes.Length} sizeMB={s.totalSize / 1024 / 1024} errors={s.totalErrors}");
            EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        };
    }
}
