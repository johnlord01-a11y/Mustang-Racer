using NUnit.Framework;
using OpenWheelRacer.Race;
using UnityEngine;

namespace OpenWheelRacer.Tests.EditMode
{
    /// <summary>
    /// A fresh profile must carry exactly the numbers <see cref="AiDriver"/> drove with
    /// before profiles existed — those constants are still the reference, so the profile
    /// defaults are asserted against them rather than against literals that could drift
    /// together with the profile and agree on a changed value.
    /// </summary>
    public sealed class AIDriverProfileTests
    {
        [Test]
        public void CreateDefault_MatchesAiDriverReferenceConstants()
        {
            AIDriverProfile p = AIDriverProfile.CreateDefault();
            Assert.IsNotNull(p);

            Assert.AreEqual(AiDriver.StraightSpeedMs, p.StraightSpeedMs, 1e-6f);
            Assert.AreEqual(AiDriver.CornerLateralAccelMs2, p.CornerLateralAccelMs2, 1e-6f);
            Assert.AreEqual(AiDriver.CornerSpeedScale, p.CornerSpeedScale, 1e-6f);
            Assert.AreEqual(AiDriver.LookaheadBaseM, p.LookaheadBaseM, 1e-6f);
            Assert.AreEqual(AiDriver.LookaheadPerMs, p.LookaheadPerMs, 1e-6f);
            Assert.AreEqual(AiDriver.LookaheadMinM, p.LookaheadMinM, 1e-6f);
            Assert.AreEqual(AiDriver.LookaheadMaxM, p.LookaheadMaxM, 1e-6f);
            Assert.AreEqual(AiDriver.BrakingHorizonM, p.BrakingHorizonM, 1e-6f);
            Assert.AreEqual(AiDriver.BrakingStepM, p.BrakingStepM, 1e-6f);
            Assert.AreEqual(AiDriver.ThrottleBandMs, p.ThrottleBandMs, 1e-6f);
            Assert.AreEqual(AiDriver.BrakeBandMs, p.BrakeBandMs, 1e-6f);

            // A default profile is the reference pace: trimming by it changes nothing.
            Assert.AreEqual(1f, p.PaceScale, 1e-6f);

            Object.DestroyImmediate(p);
        }

        [Test]
        public void CreatePaced_TrimsOnlyThePace()
        {
            AIDriverProfile p = AIDriverProfile.CreatePaced(0.94f);
            Assert.AreEqual(0.94f, p.PaceScale, 1e-6f);
            Assert.AreEqual(AiDriver.StraightSpeedMs, p.StraightSpeedMs, 1e-6f);

            Object.DestroyImmediate(p);
        }

        [Test]
        public void DefaultFieldPaceScales_FastestIsReference_AndSpreadOutward()
        {
            float[] scales = AIDriverProfile.DefaultFieldPaceScales;
            Assert.GreaterOrEqual(scales.Length, 3, "the field spawns three opponents");
            Assert.AreEqual(AIDriverProfile.FastestProfileScale, scales[0], 1e-6f);

            for (int i = 1; i < scales.Length; i++)
            {
                Assert.Less(scales[i], scales[i - 1], "each slot trails the one ahead");
                Assert.GreaterOrEqual(scales[i], AIDriverProfile.SlowestProfileScale,
                    "nobody is trimmed out of the race");
            }
        }
    }
}
