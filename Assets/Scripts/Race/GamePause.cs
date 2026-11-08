using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// Esc stops the session and puts up a three-button panel (PLAN.md Stage 4, authored
    /// early because Stage 3 is unplayable without a way out of a crashed lap).
    ///
    /// Pausing is <see cref="Time.timeScale"/> = 0: the car's FixedUpdate never runs, the
    /// lap clock in <see cref="RaceSession"/> stops being ticked, and the chase camera
    /// holds still because its lerp is scaled by <c>Time.deltaTime</c> too. Everything
    /// that matters stops by itself; nothing here has to know about any of it.
    ///
    /// <see cref="IsPaused"/> is static on purpose — the session and anything else that
    /// would otherwise burn time while the player is in the menu reads it directly, which
    /// beats wiring a reference through every component that cares.
    ///
    /// IMGUI again (see <see cref="RaceHud"/>): no canvas, no event system, no font
    /// asset, and buttons that work with nothing but a mouse.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GamePause : MonoBehaviour
    {
        /// <summary>Panel buttons.</summary>
        public const string ResumeLabel = "Resume";

        /// <summary>Panel buttons.</summary>
        public const string RestartLabel = "Restart";

        /// <summary>Panel buttons.</summary>
        public const string QuitLabel = "Quit";

        private const float PanelWidth = 320f;
        private const float PanelHeight = 266f;
        private const float ButtonHeight = 46f;
        private const float ButtonMargin = 36f;

        private GUIStyle _titleStyle;
        private GUISkin _skin;

        /// <summary>Backing store of <see cref="IsPaused"/>; never set except through it.</summary>
        private static bool _paused;

        /// <summary>
        /// True while the clock is stopped and the panel is on screen. Setting it does the
        /// whole job — the scale is what actually stops the game, so the flag is not allowed
        /// to get away from it; anything that merely wants to know can read it from anywhere.
        /// </summary>
        public static bool IsPaused
        {
            get => _paused;
            set
            {
                _paused = value;
                Time.timeScale = value ? 0f : 1f;
            }
        }

        /// <summary>Stop or restart the clock; the single place <see cref="Time.timeScale"/> is touched.</summary>
        public static void SetPaused(bool paused)
        {
            IsPaused = paused;
        }

        /// <summary>Esc in, Esc out.</summary>
        public static void TogglePause()
        {
            SetPaused(!IsPaused);
        }

        private void Update()
        {
            // Keyboard.current is null before the Input System has produced a device (and
            // outright when the project is on the old input manager); Esc is then simply
            // not available rather than throwing every frame.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                TogglePause();
            }
        }

        private void OnGUI()
        {
            if (!IsPaused)
            {
                return;
            }

            float width = Mathf.Min(PanelWidth, Screen.width);
            float height = Mathf.Min(PanelHeight, Screen.height);
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f,
                width, height);

            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.xMin, panel.yMin + 20f, width, 40f), "PAUSED", Title());

            var resume = new Rect(panel.xMin + ButtonMargin, panel.yMin + 82f,
                width - 2f * ButtonMargin, ButtonHeight);
            if (GUI.Button(resume, ResumeLabel))
            {
                SetPaused(false);
            }

            var restart = new Rect(resume.x, resume.yMax + 12f, resume.width, ButtonHeight);
            if (GUI.Button(restart, RestartLabel))
            {
                RestartActiveScene();
            }

            var quit = new Rect(restart.x, restart.yMax + 12f, restart.width, ButtonHeight);
            if (GUI.Button(quit, QuitLabel))
            {
                QuitGame();
            }
        }

        /// <summary>
        /// Reload the active scene. The clock is restored first: the new scene inherits
        /// <see cref="Time.timeScale"/>, and restarting into a frozen session is the one
        /// thing a restart button must never do.
        /// </summary>
        public static void RestartActiveScene()
        {
            SetPaused(false);

            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
                return;
            }

            // Scenes outside the build settings cannot be loaded by index. Loading by name
            // is what the editor offers while prototyping — Track_Proto is normally one of
            // them — so warn rather than fail silently.
            Debug.LogWarning($"[GamePause] '{scene.name}' is not in the build settings; " +
                             "restarting it by name, which only works in the editor.");
            SceneManager.LoadScene(scene.name);
        }

        /// <summary>
        /// Close the game. The clock is handed back first, so a quit out of the menu cannot
        /// leave a zero <see cref="Time.timeScale"/> behind it — which matters in the editor,
        /// where stopping play mode leaves the next session running on whatever scale the
        /// last one stopped at. The editor stops the play session, because
        /// <see cref="Application.Quit()"/> does nothing there.
        /// </summary>
        public static void QuitGame()
        {
            SetPaused(false);

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// This component's lifetime is the scene's. Leaving play mode or reloading the
        /// scene with the clock stopped would leave every later scene, and the editor
        /// itself, running at zero — so the paused state dies with it.
        /// </summary>
        private void OnDestroy()
        {
            IsPaused = false;
            Time.timeScale = 1f;
        }

        /// <summary>Centred bold title, rebuilt whenever the skin changes.</summary>
        private GUIStyle Title()
        {
            if (_titleStyle == null || _skin != GUI.skin)
            {
                _skin = GUI.skin;
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 30,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                _titleStyle.normal.textColor = Color.white;
            }

            return _titleStyle;
        }
    }
}
