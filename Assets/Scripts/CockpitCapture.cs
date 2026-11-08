using System;
using System.Collections;
using System.IO;
using OpenWheelRacer.Race;
using UnityEngine;

namespace OpenWheelRacer
{
    /// <summary>
    /// Opt-in screenshot of the built game: only when launched with
    /// <c>--capture-cockpit /absolute/path.png</c>. Waits for the race to reach GO, lets it run a
    /// few seconds so the countdown text is gone, saves one rendered frame (HUD included) and quits.
    /// Without the flag nothing is created and the game is unchanged.
    /// </summary>
    public sealed class CockpitCapture : MonoBehaviour
    {
        public const string Flag = "--capture-cockpit";
        public const float SecondsAfterGo = 5f;
        public const float TimeoutSeconds = 60f;

        private string _path;

        /// <summary>Pure parser: the absolute path after the flag, or null when absent or invalid.</summary>
        public static string ParseCapturePath(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == Flag)
                {
                    string path = args[i + 1];
                    if (string.IsNullOrEmpty(path) || path.StartsWith("-", StringComparison.Ordinal)) return null;
                    return Path.IsPathRooted(path) ? path : null;
                }
            }

            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string path = ParseCapturePath(Environment.GetCommandLineArgs());
            if (path == null) return;
            // Opt-in capture only: keep rendering when the window is not focused (the player pauses unfocused by default).
            Application.runInBackground = true;
            var go = new GameObject("CockpitCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<CockpitCapture>()._path = path;
            Debug.Log($"[CockpitCapture] armed: {path}");
        }

        private IEnumerator Start()
        {
            float waited = 0f;
            RaceSession session = null;
            while (waited < TimeoutSeconds)
            {
                if (session == null) session = FindAnyObjectByType<RaceSession>();
                if (session != null && session.Flow != null && session.Flow.Phase != RacePhase.Countdown) break;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            float after = 0f;
            while (after < SecondsAfterGo)
            {
                after += Time.unscaledDeltaTime;
                yield return null;
            }

            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(_path, shot.EncodeToPNG());
            Debug.Log($"[CockpitCapture] saved {shot.width}x{shot.height} to {_path} (renderer: {SystemInfo.graphicsDeviceName})");
            Destroy(shot);
            Application.Quit(0);
        }
    }
}
