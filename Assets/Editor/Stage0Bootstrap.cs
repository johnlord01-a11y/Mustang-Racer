#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace OpenWheelRacer.EditorTools
{
    /// <summary>
    /// Stage 0 foundation bootstrap + verifier. Run headless by passing the
    /// -owr-run-stage0 flag; it self-exits with code 0 (pass) or 2 (fail):
    /// Unity -batchmode -nographics -projectPath &lt;project&gt; -owr-run-stage0
    /// Creates the four scenes (empty), verifies the Input Actions asset imports
    /// with Throttle/Brake/Steer/Handbrake/Reset, and reopens each scene to prove
    /// it parses. Logs "[Stage0] RESULT: PASS" or "RESULT: FAIL (n)".
    /// </summary>
    public static class Stage0Bootstrap
    {
        private const string RequestFlag = "-owr-run-stage0";
        private const string InputAssetPath = "Assets/Input/OpenWheelRacer.InputActions.inputactions";

        [InitializeOnLoadMethod]
        private static void RunIfRequested()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg == RequestFlag)
                {
                    EditorApplication.delayCall += ExecuteAndExit;
                    return;
                }
            }
        }

        private static void ExecuteAndExit()
        {
            int failures = Run();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(failures == 0
                ? "[Stage0] RESULT: PASS"
                : $"[Stage0] RESULT: FAIL ({failures} problem(s))");
            EditorApplication.Exit(failures == 0 ? 0 : 2);
        }

        private static readonly string[] ScenePaths =
        {
            "Assets/Scenes/Boot.unity",
            "Assets/Scenes/Garage.unity",
            "Assets/Scenes/Track_Proto.unity",
            "Assets/Scenes/Sandbox.unity",
        };

        public static int Run()
        {
            int failures = 0;

            foreach (var folder in new[]
            {
                "Assets/Scenes", "Assets/Scripts", "Assets/Prefabs",
                "Assets/Physics", "Assets/Input", "Assets/Tests",
                "Assets/Tests/EditMode", "Assets/Tests/PlayMode", "Assets/Editor",
            })
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;
                int slash = folder.LastIndexOf('/');
                string parent = slash >= 0 ? folder.Substring(0, slash) : "Assets";
                string leaf = slash >= 0 ? folder.Substring(slash + 1) : folder;
                AssetDatabase.CreateFolder(parent, leaf);
            }

            // 1. Empty scenes (Stage 1+ fills them).
            foreach (var path in ScenePaths)
            {
                try
                {
                    Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    if (!EditorSceneManager.SaveScene(scene, path))
                    {
                        throw new Exception("SaveScene returned false");
                    }
                    Debug.Log($"[Stage0] Scene written: {path}");
                }
                catch (Exception e)
                {
                    failures++;
                    Debug.LogError($"[Stage0] FAILED to write scene {path}: {e}");
                }
            }

            // 2. Input Actions asset: imported, with the five drive actions.
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (asset == null)
            {
                failures++;
                Debug.LogError($"[Stage0] FAILED to import Input Actions asset at {InputAssetPath}");
            }
            else
            {
                InputActionMap map = asset.FindActionMap("Vehicle", throwIfNotFound: false);
                if (map == null)
                {
                    failures++;
                    Debug.LogError("[Stage0] Input Actions asset has no 'Vehicle' map");
                }
                else
                {
                    foreach (var actionName in new[] { "Throttle", "Brake", "Steer", "Handbrake", "Reset" })
                    {
                        InputAction action = map.FindAction(actionName, throwIfNotFound: false);
                        int count = action == null ? 0 : action.bindings.Count;
                        if (count < 2)
                        {
                            failures++;
                            Debug.LogError($"[Stage0] Action '{actionName}' missing or has too few bindings: {count}");
                        }
                        else
                        {
                            Debug.Log($"[Stage0] Action '{actionName}' OK ({count} bindings)");
                        }
                    }
                }
            }

            // 3. Reopen every scene to prove the YAML parses.
            foreach (var path in ScenePaths)
            {
                try
                {
                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    if (!scene.IsValid()) throw new Exception("scene invalid after open");
                    Debug.Log($"[Stage0] Scene opens OK: {path}");
                }
                catch (Exception e)
                {
                    failures++;
                    Debug.LogError($"[Stage0] FAILED to open scene {path}: {e}");
                }
            }

            return failures;
        }
    }
}
#endif
