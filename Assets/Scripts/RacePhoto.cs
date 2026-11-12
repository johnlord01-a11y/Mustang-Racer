using System;
using System.Collections;
using System.IO;
using OpenWheelRacer.Race;
using UnityEngine;

namespace OpenWheelRacer
{
    /// <summary>
    /// Opt-in multi-view race photographer for the built player: launched with
    /// <c>--race-photo /absolute/prefix</c> it saves four rendered frames after GO — cockpit launch,
    /// cockpit mid-pack, then two chase-cam frames — as prefix_01..04.png and quits. No flag, no behaviour.
    /// </summary>
    public sealed class RacePhoto : MonoBehaviour
    {
        public const string Flag = "--race-photo";
        public const float TimeoutSeconds = 90f;

        // (seconds after GO, cockpit view?) — launch with the field pulling away, then wide chase views.
        private static readonly (float at, bool cockpit, string tag)[] Plan =
        {
            (2.0f, true, "cockpit"),
            (3.5f, false, "chase_near"),
            (5.5f, false, "chase_rear"),
            (8.0f, false, "chase_pack")
        };

        private string _prefix;

        public static string ParsePrefix(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == Flag)
                {
                    string p = args[i + 1];
                    if (string.IsNullOrEmpty(p) || p.StartsWith("-", StringComparison.Ordinal)) return null;
                    return Path.IsPathRooted(p) ? p : null;
                }
            }
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string prefix = ParsePrefix(Environment.GetCommandLineArgs());
            if (prefix == null) return;
            Application.runInBackground = true;
            var go = new GameObject("RacePhoto");
            DontDestroyOnLoad(go);
            go.AddComponent<RacePhoto>()._prefix = prefix;
            Debug.Log($"[RacePhoto] armed: {prefix}_NN.png");
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
            if (session == null) { Debug.LogError("[RacePhoto] no RaceSession; aborting"); Application.Quit(1); yield break; }

            CockpitView view = FindFirstObjectByType<CockpitView>();
            float t = 0f;
            int next = 0;
            while (next < Plan.Length)
            {
                t += Time.unscaledDeltaTime;
                (float at, bool cockpit, string tag) = Plan[next];
                if (t < at) { yield return null; continue; }

                view?.SetCockpit(cockpit);
                yield return new WaitForEndOfFrame(); // one frame settles after camera swap
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                string path = $"{_prefix}_{next + 1:d2}_{tag}.png";
                File.WriteAllBytes(path, shot.EncodeToPNG());
                Debug.Log($"[RacePhoto] saved {shot.width}x{shot.height} t+{t:F1}s {path}");
                Destroy(shot);
                next++;
            }
            Application.Quit(0);
        }
    }
}
