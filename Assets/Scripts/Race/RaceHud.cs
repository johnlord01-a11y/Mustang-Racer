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

        private const float PanelX = 16f;
        private const float PanelY = 16f;
        private const float PanelWidth = 480f;
        private const float PanelHeight = 230f;
        private const int FontSize = 17;

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

            GUI.Label(new Rect(PanelX, PanelY, PanelWidth, PanelHeight),
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
                    fontSize = 72, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold
                };
                big.normal.textColor = Color.yellow;
                GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, 120f), countdown, big);
            }

            if (flow.PlayerFinished)
            {
                var results = new StringBuilder("RESULTS\n\n");
                foreach (RaceResult line in flow.Results())
                {
                    results.Append(line.Label).Append('\n');
                }

                var panel = new Rect(Screen.width * 0.5f - 170f, Screen.height * 0.22f, 340f, 240f);
                GUI.Box(panel, results.ToString());
                if (GUI.Button(new Rect(panel.x + 120f, panel.yMax - 40f, 100f, 30f), "Restart"))
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
            _text.Append("BEST   ").Append(TimeFormat.Format(counter.BestLapTime)).Append('\n');
            _text.Append("SPEED  ").Append(Mathf.RoundToInt(speedKmh)).Append(" km/h\n\n");
            _text.Append(HelpLine);
            return _text.ToString();
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
                    fontSize = FontSize,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false,
                    richText = false,
                    stretchWidth = false,
                    stretchHeight = false
                };
                _style.padding = new RectOffset(14, 14, 12, 12);
                _style.normal.textColor = Color.white;
                _style.onNormal.textColor = Color.white;
            }

            return _style;
        }
    }
}
