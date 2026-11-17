using System;
using System.Linq;
using UnityEditor;

/// <summary>
/// One-shot build-settings pass: launches with -owr-scenes. Puts the Garage first in
/// the build so the player opens on the car-select screen, ahead of the track and the
/// sandbox; Boot stays out of the build, as it has always been.
/// </summary>
internal static class OwrSceneSetup
{
    [InitializeOnLoadMethod]
    private static void Kick()
    {
        if (!Environment.GetCommandLineArgs().Contains("-owr-scenes")) return;
        EditorApplication.delayCall += () =>
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Garage.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Track_Proto.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Sandbox.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Boot.unity", false),
            };
            UnityEngine.Debug.Log("[OWR-SCENES] build scenes: Garage, Track_Proto, Sandbox enabled; Boot disabled");
            EditorApplication.Exit(0);
        };
    }
}
