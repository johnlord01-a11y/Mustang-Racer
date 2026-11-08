using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// The two player settings that outlive a scene — quality level and VSync — kept in
    /// <see cref="PlayerPrefs"/> under <c>settings.quality</c> and <c>settings.vsync</c>,
    /// and applied once on startup before the first scene shows.
    ///
    /// A plain static rather than a component: these are engine-wide switches with no
    /// scene to own them, and the pause menu writes through the same two methods the
    /// startup path uses, so a change made in the menu and a change restored at boot
    /// cannot disagree about what was saved.
    /// </summary>
    public static class GameSettings
    {
        public const string QualityKey = "settings.quality";
        public const string VSyncKey = "settings.vsync";

        /// <summary>The level a player who never opened the settings menu gets.</summary>
        public const int DefaultQualityIndex = 0;

        /// <summary>Read the saved quality index (not yet necessarily the live one).</summary>
        public static int QualityIndex =>
            PlayerPrefs.GetInt(QualityKey, DefaultQualityIndex);

        /// <summary>Read the saved VSync switch.</summary>
        public static bool VSync => PlayerPrefs.GetInt(VSyncKey, 0) != 0;

        /// <summary>Push both saved settings into the engine; called once on startup.</summary>
        public static void Apply()
        {
            // Re-run the setter so the index is clamped against this machine's level
            // count — a save made on a bigger editor must not ask for level 8 of three.
            SetQuality(QualityIndex);
            SetVSync(VSync);
        }

        /// <summary>
        /// Choose a quality level: the index is clamped to the levels this build actually
        /// has, applied with <see cref="QualitySettings.SetQualityLevel"/> and persisted.
        /// The clamped index is what gets stored, so it is always a valid one to read back.
        /// </summary>
        public static int SetQuality(int index)
        {
            string[] names = QualitySettings.names;
            int clamped = names == null || names.Length == 0
                ? 0
                : Mathf.Clamp(index, 0, names.Length - 1);

            if (names != null && names.Length > 0)
            {
                QualitySettings.SetQualityLevel(clamped, applyExpensiveChanges: true);
            }

            PlayerPrefs.SetInt(QualityKey, clamped);
            PlayerPrefs.Save();
            return clamped;
        }

        /// <summary>Switch VSync on or off (0/2 vsync count) and persist it.</summary>
        public static void SetVSync(bool on)
        {
            QualitySettings.vSyncCount = on ? 2 : 0;
            PlayerPrefs.SetInt(VSyncKey, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Startup hook: fires before the first scene loads, so the menu and the race
        /// only ever run under the quality the player left behind.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyOnStartup()
        {
            Apply();
        }
    }
}
