using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// The best lap per track, kept between runs in <see cref="PlayerPrefs"/> under
    /// <c>"bestlap." + trackId</c>. Deliberately small: one float per track, no history,
    /// no leaderboard — the HUD has one "BEST" line to fill and this keeps exactly the
    /// number that line shows.
    ///
    /// A negative, NaN or infinite time is never a lap and is never stored, so callers
    /// can submit the lap-counter sentinel (<see cref="LapCounter.NoTime"/>) without
    /// poisoning the record.
    /// </summary>
    public static class BestLapStore
    {
        /// <summary>Read the stored best lap. False (and 0) when this track has no record yet.</summary>
        public static bool TryGet(string trackId, out float seconds)
        {
            string key = Key(trackId);
            if (key.Length == 0 || !PlayerPrefs.HasKey(key))
            {
                seconds = 0f;
                return false;
            }

            seconds = PlayerPrefs.GetFloat(key);
            return true;
        }

        /// <summary>
        /// Offer a lap time. True when it is a new best — the first valid lap, or one
        /// faster than the record; a slower or invalid lap changes nothing and is False.
        /// </summary>
        public static bool Submit(string trackId, float seconds)
        {
            string key = Key(trackId);
            if (key.Length == 0 || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
            {
                return false;
            }

            if (PlayerPrefs.HasKey(key) && seconds >= PlayerPrefs.GetFloat(key))
            {
                return false; // Not faster than what is already banked.
            }

            PlayerPrefs.SetFloat(key, seconds);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>Forget this track's record.</summary>
        public static void Clear(string trackId)
        {
            string key = Key(trackId);
            if (key.Length == 0)
            {
                return;
            }

            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }

        private static string Key(string trackId)
        {
            return string.IsNullOrEmpty(trackId) ? "" : "bestlap." + trackId;
        }
    }
}
