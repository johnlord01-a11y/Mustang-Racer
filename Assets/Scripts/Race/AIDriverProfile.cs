using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// The main driving knobs of an <see cref="AiDriver"/> as an asset, so a field of
    /// opponents can be given different characters without touching code. The defaults
    /// are exactly the constants on <see cref="AiDriver"/> — an untouched profile drives
    /// the same car as before profiles existed.
    ///
    /// Only the driving knobs live here. The recovery numbers (stuck, off-track, reverse)
    /// are about surviving, not about pace, and stay constants.
    /// </summary>
    [CreateAssetMenu(fileName = "AIDriverProfile", menuName = "OpenWheelRacer/AI Driver Profile")]
    public sealed class AIDriverProfile : ScriptableObject
    {
        /// <summary>Pace of the quickest opponent in a default-spawned field.</summary>
        public const float FastestProfileScale = 1.0f;

        /// <summary>And of the slowest; the one between splits the difference.</summary>
        public const float SlowestProfileScale = 0.94f;

        /// <summary>
        /// One pace scale per opponent <see cref="AiField"/> spawns: the fastest is the
        /// reference driver exactly as it always drove, the rest trail it enough to spread
        /// the field without dropping anybody out of the race.
        /// </summary>
        public static readonly float[] DefaultFieldPaceScales = { 1.0f, 0.97f, 0.94f };

        [Header("Speed")]
        [Tooltip("Speed aimed for where the centreline is straight, m/s.")]
        public float StraightSpeedMs = 26f;

        [Tooltip("Lateral acceleration the corner speed is worked out from, m/s².")]
        public float CornerLateralAccelMs2 = 9f;

        [Tooltip("Margin on the corner speed, so a corner taken at it is one the car survives.")]
        public float CornerSpeedScale = 0.95f;

        [Tooltip("Whole pace trim: 1 drives the numbers as written, below 1 trails the pace.")]
        public float PaceScale = 1.0f;

        [Header("Look-ahead")]
        [Tooltip("Look-ahead down the centreline standing still, m.")]
        public float LookaheadBaseM = 6f;

        [Tooltip("Extra look-ahead per m/s of speed, m per m/s.")]
        public float LookaheadPerMs = 0.6f;

        [Tooltip("Floor on the look-ahead, m.")]
        public float LookaheadMinM = 8f;

        [Tooltip("Ceiling on the look-ahead, m.")]
        public float LookaheadMaxM = 25f;

        [Header("Speed control")]
        [Tooltip("How far ahead the curvature is scanned for the speed target, m.")]
        public float BrakingHorizonM = 40f;

        [Tooltip("Step of that scan, m.")]
        public float BrakingStepM = 8f;

        [Tooltip("Speed error asking for full throttle, m/s short of the target.")]
        public float ThrottleBandMs = 4f;

        [Tooltip("Speed error asking for full brake, m/s over the target.")]
        public float BrakeBandMs = 3f;

        /// <summary>
        /// A profile carrying today's values, for a driver handed no asset. Callers keep
        /// it; this method hands out a fresh instance every call.
        /// </summary>
        public static AIDriverProfile CreateDefault()
        {
            return CreateInstance<AIDriverProfile>();
        }

        /// <summary>A default profile trimmed to the given pace (see <see cref="PaceScale"/>).</summary>
        public static AIDriverProfile CreatePaced(float paceScale)
        {
            AIDriverProfile profile = CreateDefault();
            profile.PaceScale = paceScale;
            return profile;
        }
    }
}
