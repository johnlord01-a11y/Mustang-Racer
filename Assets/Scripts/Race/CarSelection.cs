using System;
using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// Which cars this run's race is fought between: the one the player picked in the
    /// garage (or on the command line) and the three distinct rivals the game dealt the
    /// AI out of the five the player did not take.
    ///
    /// A static, deliberately: the garage scene and the track scene are different scenes,
    /// the bootstrap that assembles the race reads the answer the moment the track loads,
    /// and a run's pick is made exactly once. Nothing is resolved until somebody resolves
    /// it — a track opened directly, with no garage visit and no <c>--car</c>, leaves
    /// <see cref="HasSelection"/> false and every car on the grid keeps the procedural
    /// body it has always worn, which is what the test suite assembles.
    /// </summary>
    public static class CarSelection
    {
        /// <summary>PlayerPrefs key remembering the last car picked, for the garage.</summary>
        public const string PrefsKey = "OwrSelectedCar";

        /// <summary>Command-line flag that pre-picks the player's car: <c>--car 1..6</c>.</summary>
        public const string CarFlag = "--car";

        /// <summary>Catalog index of the player's car; meaningful only with a selection.</summary>
        public static int PlayerIndex { get; private set; } = CarBodyCatalog.PlayerCarIndex;

        /// <summary>Whether this run has a resolved selection at all.</summary>
        public static bool HasSelection { get; private set; }

        private static readonly int[] AiIndices = { -1, -1, -1 };

        /// <summary>
        /// Read the command line once per run, before anything loads: a <c>--car N</c>
        /// (1-based, the numbers the garage menu shows) resolves the selection up front,
        /// which is also what lets the headless photo runs pick their car. In the editor
        /// the same scan runs over the editor's own arguments and simply finds nothing.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ParseCommandLine()
        {
            Reset();

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != CarFlag)
                {
                    continue;
                }

                if (int.TryParse(args[i + 1], out int oneBased) &&
                    oneBased >= 1 && oneBased <= CarBodyCatalog.Count)
                {
                    Resolve(oneBased - 1);
                    Debug.Log($"[CarSelection] command line picked car {oneBased}: " +
                              $"{CarBodyCatalog.DisplayNames[PlayerIndex]}");
                }
                else
                {
                    Debug.LogWarning($"[CarSelection] ignoring unusable {CarFlag} value " +
                                     $"'{args[i + 1]}' (want 1..{CarBodyCatalog.Count}).");
                }

                return;
            }
        }

        /// <summary>
        /// Make <paramref name="playerIndex"/> the player's car and deal the AI three
        /// distinct rivals from the other five, at random. Also remembers the pick, so
        /// the garage can show last time's car.
        /// </summary>
        public static void Resolve(int playerIndex)
        {
            PlayerIndex = Mathf.Clamp(playerIndex, 0, CarBodyCatalog.Count - 1);

            var pool = new System.Collections.Generic.List<int>();
            for (int i = 0; i < CarBodyCatalog.Count; i++)
            {
                if (i != PlayerIndex)
                {
                    pool.Add(i);
                }
            }

            var random = new System.Random();
            for (int slot = 0; slot < AiIndices.Length; slot++)
            {
                int pick = random.Next(pool.Count);
                AiIndices[slot] = pool[pick];
                pool.RemoveAt(pick);
            }

            HasSelection = true;
            PlayerPrefs.SetInt(PrefsKey, PlayerIndex);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The imported body dealt to opponent <paramref name="slot"/> (0-based, the
        /// order <see cref="AiField"/> spawns them in). False — with
        /// <paramref name="bodyIndex"/> -1, the procedural body — when there is no
        /// selection or the slot is out of range.
        /// </summary>
        public static bool TryGetAiBodyIndex(int slot, out int bodyIndex)
        {
            bodyIndex = -1;
            if (!HasSelection || slot < 0 || slot >= AiIndices.Length)
            {
                return false;
            }

            bodyIndex = AiIndices[slot];
            return bodyIndex >= 0;
        }

        /// <summary>Forget any selection: back to no garage visit, all-procedural grid.</summary>
        public static void Reset()
        {
            HasSelection = false;
            PlayerIndex = CarBodyCatalog.PlayerCarIndex;
            for (int i = 0; i < AiIndices.Length; i++)
            {
                AiIndices[i] = -1;
            }
        }
    }
}
