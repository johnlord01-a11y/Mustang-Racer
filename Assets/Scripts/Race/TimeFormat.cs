using System;
using System.Globalization;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// Culture-proof lap-time formatting in the "mm:ss.fff" shape every HUD element
    /// and save file uses (PLAN.md Stage 3 acceptance criterion 6 / Stage 4 helper).
    ///
    /// Pure static math on purpose: no UnityEngine, no culture of the machine, so
    /// EditMode tests assert exactly what the player reads on screen.
    /// </summary>
    public static class TimeFormat
    {
        /// <summary>
        /// Shown when there is no time to show — most importantly BestLapTime, which
        /// is <see cref="float.PositiveInfinity"/> until a lap is actually completed.
        /// </summary>
        public const string Invalid = "--:--.---";

        /// <summary>
        /// Largest displayable time. Minutes are not rolled up into hours (a stint
        /// reads better as 74:05.120), so the field is capped at 99:59.999 to keep the
        /// HUD width fixed and the arithmetic far away from any long overflow.
        /// </summary>
        public const float MaxSeconds = 5999.999f;

        private const long MsPerMinute = 60000L;
        private const long MaxTotalMs = 5999999L; // 99:59.999

        /// <summary>
        /// Formats a duration as "mm:ss.fff".
        /// 0 -> "00:00.000", 83.456 -> "01:23.456".
        /// Negative, NaN and infinite values -> "--:--.---".
        /// Sub-millisecond input is rounded to the nearest millisecond, halves away
        /// from zero, exactly like a physical stopwatch.
        /// </summary>
        public static string Format(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
            {
                return Invalid;
            }

            double totalMs = Math.Round((double)seconds * 1000.0, MidpointRounding.AwayFromZero);
            if (totalMs > MaxTotalMs)
            {
                totalMs = MaxTotalMs;
            }

            long ms = (long)totalMs;
            long minutes = ms / MsPerMinute;
            long remainder = ms % MsPerMinute;
            int secs = (int)(remainder / 1000L);
            int millis = (int)(remainder % 1000L);

            return string.Format(CultureInfo.InvariantCulture,
                "{0:00}:{1:00}.{2:000}", minutes, secs, millis);
        }
    }
}
