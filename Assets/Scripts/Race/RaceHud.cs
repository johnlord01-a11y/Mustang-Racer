using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// Lap readout in the top-left corner: lap number, the field position, the clock, last and
    /// best lap, road speed and one line of controls (PLAN.md Stage 3 acceptance criterion 6).
    ///
    /// IMGUI rather than a Canvas: the stage is a prototype with no canvas, no font
    /// asset and no TextMeshPro dependency, and <c>OnGUI</c> is in every build of the
    /// engine. Stage 4 replaces it with themed UXML once the visual language exists; the
    /// formatting does not live here, it lives in <see cref="TimeFormat"/>, so the two
    /// can be swapped without the numbers changing shape.
    ///
    /// Reads through <see cref="RaceSession"/> rather than owning any state: the HUD is a
    /// pure projection of the counter, and it draws nothing until a session exists so a
    /// scene that is still booting never shows a lap-0 timer ticking away.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaceHud : MonoBehaviour
    {
        /// <summary>The controls reminder, kept in one place so it cannot drift from the bindings.</summary>
        public const string HelpLine =
            "W throttle  S brake/reverse  A/D steer\nSpace handbrake  R reset  C view  Esc pause";

        // Base sizes are authored for a 1080p screen and multiplied by HudScale, so the
        // panel keeps the same proportion of the screen at any resolution.
        public const float PanelX = 10f;
        public const float PanelY = 10f;
        public const float PanelWidth = 288f;   // 480 * 0.6
        public const float PanelHeight = 138f;  // 230 * 0.6
        public const int FontSize = 10;         // 17 * 0.6 rounded down

        /// <summary>Countdown glyph height at 1080p (was 72).</summary>
        public const int CountdownFontSize = 48;

        /// <summary>Results panel size and button size at 1080p (were 340x240 and 100x30).</summary>
        public const float ResultsWidth = 204f;
        public const float ResultsHeight = 144f;
        public const float ButtonWidth = 60f;
        public const float ButtonHeight = 18f;

        public const float MinScale = 0.6f;
        public const float MaxScale = 1.5f;

        /// <summary>Resolution scale: Screen.height over 1080, clamped to a sane range.</summary>
        public static float HudScale()
        {
            return Mathf.Clamp(Screen.height / 1080f, MinScale, MaxScale);
        }

        /// <summary>Metres per second to kilometres per hour — the unit the readout is in.</summary>
        private const float KmhPerMs = 3.6f;

        private RaceSession _session;
        private GUIStyle _style;
        private GUISkin _skin;

        /// <summary>Reused every repaint; OnGUI runs several times per frame and allocates enough as it is.</summary>
        private readonly StringBuilder _text = new StringBuilder(256);

        private void OnGUI()
        {
            if (_session == null)
            {
                _session = GetComponent<RaceSession>();
            }

            LapCounter counter = _session != null ? _session.Counter : null;
            if (counter == null)
            {
                return; // Before RaceSession.Init there is nothing worth drawing.
            }

            float s = HudScale();
            GUI.Label(new Rect(PanelX * s, PanelY * s, PanelWidth * s, PanelHeight * s),
                BuildReadout(counter, _session.Car, _session.Opponents,
                    _session.PlayerProgress.Value),
                Style());

            RaceFlow flow = _session.Flow;
            if (flow == null)
            {
                return;
            }

            string countdown = flow.CountdownLabel;
            if (countdown.Length > 0)
            {
                var big = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(CountdownFontSize * s),
                    alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold
                };
                big.normal.textColor = Color.yellow;
                GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, 80f * s), countdown, big);
            }

            if (flow.PlayerFinished)
            {
                var results = new StringBuilder("RESULTS\n\n");
                foreach (RaceResult line in flow.Results())
                {
                    results.Append(line.Label).Append('\n');
                }

                var panel = new Rect(Screen.width * 0.5f - ResultsWidth * s * 0.5f,
                    Screen.height * 0.22f, ResultsWidth * s, ResultsHeight * s);
                GUI.Box(panel, results.ToString());
                if (GUI.Button(new Rect(panel.x + (ResultsWidth - ButtonWidth) * 0.5f * s,
                        panel.yMax - (ButtonHeight + 12f) * s, ButtonWidth * s, ButtonHeight * s), "Restart"))
                {
                    GamePause.RestartActiveScene();
                }
            }
        }

        /// <summary>
        /// The six lines, in the order a driver checks them. Times go through
        /// <see cref="TimeFormat"/>, which renders the no-lap-yet sentinel as
        /// <c>--:--.---</c>; speed is rounded, not truncated, so a standing start
        /// settles on 0 km/h instead of flickering.
        /// </summary>
        private string BuildReadout(LapCounter counter, VehicleController car,
            IReadOnlyList<AiDriver> opponents, float playerProgress)
        {
            float speedKmh = car != null ? car.CurrentSpeedMs * KmhPerMs : 0f;

            _text.Clear();
            _text.Append("LAP    ").Append(FormatLap(counter.CurrentLap)).Append(_session != null && _session.Flow != null ? "/" + _session.Flow.TotalLaps : "").Append('\n');
            _text.Append("POS    ").Append(FormatPosition(playerProgress, opponents)).Append('\n');
            _text.Append("RACE   ").Append(TimeFormat.Format(_session != null && _session.Flow != null ? _session.Flow.RaceTime : 0f)).Append('\n');
            _text.Append("LAP T  ").Append(TimeFormat.Format(counter.CurrentLapTime)).Append('\n');
            _text.Append("LAST   ").Append(TimeFormat.Format(counter.LastLapTime)).Append('\n');
            _text.Append("BEST   ").Append(TimeFormat.Format(StoredOrSessionBest(counter))).Append('\n');
            _text.Append("SPEED  ").Append(Mathf.RoundToInt(speedKmh)).Append(" km/h\n\n");
            _text.Append(HelpLine);
            return _text.ToString();
        }

        /// <summary>
        /// The BEST line reads the persistent track record when there is one, falling
        /// back to the session's own best before the store has been written; the
        /// sentinel (no record, no lap yet) renders as <see cref="TimeFormat.Invalid"/>.
        /// </summary>
        private static float StoredOrSessionBest(LapCounter counter)
        {
            if (BestLapStore.TryGet(TrackBuilder.TrackId, out float stored))
            {
                return stored;
            }

            return counter.BestLapTime;
        }

        /// <summary>
        /// 1-based lap in progress. Zero means the line has not been crossed since the
        /// session started, which reads better as a dash than as a phantom lap zero.
        /// </summary>
        private static string FormatLap(int lap)
        {
            return lap > 0 ? lap.ToString() : "--";
        }

        /// <summary>
        /// <c>2/4 provisional</c> — the player's place out of the size of the field, and the admission
        /// that it is nothing more than that: it counts who has travelled furthest round the circuit,
        /// which is what a position means once cars are racing, but there are no start lights holding the
        /// field and no finish rules ending it, so a win by this number is a win nobody agreed to race
        /// for.
        ///
        /// Metres come round the circuit, compared head to head, and both sides of that comparison are the
        /// same measurement: the player's <see cref="RaceSession.PlayerProgress"/> against each
        /// opponent's <see cref="AiDriver.Progress"/>, each one an odometer of the road it has covered
        /// started at its own grid slot. A lap-2 car and a lap-1 car cannot be put in order by how far
        /// round their own laps they are, which is what the figure this replaces tried to do — the two
        /// readings were not on one scale, and the player could be placed behind a car they had visibly
        /// just been passed by. A field of one is a solo run and says so as 1/1.
        /// </summary>
        private static string FormatPosition(float playerProgress, IReadOnlyList<AiDriver> opponents)
        {
            int ahead = 1; // The player, who is at least in their own race.
            int field = 1;

            if (opponents == null)
            {
                return $"{ahead}/{field} provisional";
            }

            for (int i = 0; i < opponents.Count; i++)
            {
                AiDriver rival = opponents[i];
                if (rival == null)
                {
                    continue; // A car removed from the field stops being a place to lose.
                }

                field++;
                if (rival.Progress > playerProgress)
                {
                    ahead++;
                }
            }

            return $"{ahead}/{field} provisional";
        }

        /// <summary>
        /// Box-styled label, built once per skin. The copy keeps the skin's sliced box
        /// background, which is the cheapest legible panel there is; the skin is watched
        /// because it is rebuilt between play sessions and a cached style pointing at the
        /// old one stops drawing.
        /// </summary>
        private GUIStyle Style()
        {
            if (_style == null || _skin != GUI.skin)
            {
                _skin = GUI.skin;
                _style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = Mathf.Max(1, Mathf.RoundToInt(FontSize * HudScale())),
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false,
                    richText = false,
                    stretchWidth = false,
                    stretchHeight = false
                };
                int padX = Mathf.Max(1, Mathf.RoundToInt(14 * HudScale()));
                int padY = Mathf.Max(1, Mathf.RoundToInt(12 * HudScale()));
                _style.padding = new RectOffset(padX, padX, padY, padY);
                _style.normal.textColor = Color.white;
                _style.onNormal.textColor = Color.white;
            }

            return _style;
        }
    }
}
