using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// The garage: a pick-your-Mustang screen shown when the <c>Garage</c> scene loads,
    /// before any race exists. Six buttons (or keys 1-6) — pick one and the game deals
    /// the AI three distinct rivals from the five left over, remembers the pick, and
    /// loads the track, where <see cref="RaceBootstrap"/> and <see cref="AiField"/>
    /// dress the grid from <see cref="CarSelection"/>.
    ///
    /// Like the race itself, the menu is code, not scene content: the scene file stays
    /// the empty template it has always been, and a bootstrap-created component cannot
    /// go stale inside it. Drawn with IMGUI because a six-item list is exactly its size
    /// of problem and it needs no canvas, font or prefab assets to go missing.
    ///
    /// Headless runs never wait on a human: a selection already resolved by
    /// <c>--car</c> — or a <c>--race-photo</c> run, which resolves the remembered car
    /// if the command line did not pick one — walks straight through to the track.
    /// </summary>
    public sealed class CarSelectMenu : MonoBehaviour
    {
        /// <summary>The only scene this menu assembles itself into.</summary>
        public const string GarageSceneName = "Garage";

        private const float PanelWidth = 560f;
        private const float ButtonHeight = 46f;

        private GUIStyle _titleStyle;
        private GUIStyle _subStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _creditsStyle;
        private bool _leaving;

        /// <summary>
        /// Put the menu up on any load of the garage scene — and only there: the track,
        /// the sandbox and the test runner's own scenes never see it.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (SceneManager.GetActiveScene().name != GarageSceneName)
            {
                return;
            }

            var host = new GameObject("CarSelectMenu");
            host.AddComponent<CarSelectMenu>();
        }

        private void Start()
        {
            EnsureCamera();

            bool photoRun = RacePhoto.ParsePrefix(Environment.GetCommandLineArgs()) != null;
            if (!CarSelection.HasSelection && photoRun)
            {
                // A photo run with no --car still has to race somebody: the remembered
                // car, or the player's own 2017 when nothing was ever picked.
                int remembered = PlayerPrefs.GetInt(
                    CarSelection.PrefsKey, CarBodyCatalog.PlayerCarIndex);
                CarSelection.Resolve(remembered);
            }

            if (CarSelection.HasSelection)
            {
                StartCoroutine(AdvanceAfter(0.3f));
            }
        }

        private void Update()
        {
            if (_leaving)
            {
                return;
            }

            // The project reads its devices through the Input System package; the
            // legacy UnityEngine.Input class throws under that configuration.
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            for (int i = 0; i < CarBodyCatalog.Count; i++)
            {
                if (keyboard[DigitKeys[i]].wasPressedThisFrame ||
                    keyboard[NumpadKeys[i]].wasPressedThisFrame)
                {
                    Pick(i);
                    return;
                }
            }
        }

        private static readonly Key[] DigitKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6
        };

        private static readonly Key[] NumpadKeys =
        {
            Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6
        };

        private void OnGUI()
        {
            if (_leaving)
            {
                return;
            }

            EnsureStyles();

            float panelHeight = 250f + CarBodyCatalog.Count * (ButtonHeight + 8f);
            var panel = new Rect(
                (Screen.width - PanelWidth) / 2f,
                Mathf.Max(10f, (Screen.height - panelHeight) / 2f),
                PanelWidth, panelHeight);

            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 18f,
                PanelWidth - 48f, panelHeight - 36f));

            GUILayout.Label("MUSTANG RACER", _titleStyle);
            GUILayout.Label("Choose your car - the AI picks 3 rivals from the rest",
                _subStyle);
            GUILayout.Space(14f);

            int lastPicked = PlayerPrefs.GetInt(
                CarSelection.PrefsKey, CarBodyCatalog.PlayerCarIndex);
            for (int i = 0; i < CarBodyCatalog.Count; i++)
            {
                string name = CarBodyCatalog.DisplayNames[i];
                if (i == lastPicked && CarBodyCatalog.Count > 1)
                {
                    name += "   (last time)";
                }

                if (GUILayout.Button($"{i + 1}.  {name}", _buttonStyle,
                        GUILayout.Height(ButtonHeight)))
                {
                    Pick(i);
                }

                GUILayout.Space(8f);
            }

            GUILayout.Space(6f);
            GUILayout.Label(CarBodyCatalog.CreditsLine, _creditsStyle);
            GUILayout.Space(4f);
            GUILayout.Label("Click a car or press 1-6", _creditsStyle);

            GUILayout.EndArea();
        }

        private void Pick(int index)
        {
            if (_leaving)
            {
                return;
            }

            CarSelection.Resolve(index);
            Debug.Log($"[CarSelectMenu] picked {CarBodyCatalog.DisplayNames[index]}; " +
                      "loading the track.");
            StartCoroutine(AdvanceAfter(0f));
        }

        private IEnumerator AdvanceAfter(float delay)
        {
            _leaving = true;
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            SceneManager.LoadScene(RaceBootstrap.TrackSceneName);
        }

        /// <summary>
        /// The garage scene ships with no camera in it, and a player with nothing
        /// rendering shows the menu over a void: give it a plain dark backdrop camera
        /// when the scene has none. IMGUI draws over whatever the camera clears.
        /// </summary>
        private static void EnsureCamera()
        {
            if (Camera.main != null)
            {
                return;
            }

            var cameraObject = new GameObject(RaceBootstrap.CameraName);
            cameraObject.tag = RaceBootstrap.MainCameraTag;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 34,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(16, 8, 4, 4)
            };
            _creditsStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter
            };
        }
    }
}
