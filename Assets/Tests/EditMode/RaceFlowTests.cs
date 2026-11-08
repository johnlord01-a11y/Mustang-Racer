using NUnit.Framework;
using OpenWheelRacer.Race;

namespace OpenWheelRacer.Tests.EditMode
{
    public sealed class RaceFlowTests
    {
        private static RaceFlow Field(int laps = 3, float countdown = 3f)
        {
            var f = new RaceFlow(laps, countdown);
            f.Register("Player", true); f.Register("AI_1", false); f.Register("AI_2", false);
            return f;
        }

        [Test] public void Defaults_AreThreeLaps_ThreeSecondCountdown()
        {
            var f = new RaceFlow();
            Assert.AreEqual(3, f.TotalLaps); Assert.AreEqual(3f, f.CountdownSeconds);
            Assert.AreEqual(RacePhase.Countdown, f.Phase);
        }

        [Test] public void LapsAndCountdown_AreClamped()
        {
            Assert.AreEqual(1, new RaceFlow(0, 3f).TotalLaps);
            Assert.AreEqual(1, new RaceFlow(-5, 3f).TotalLaps);
            Assert.AreEqual(5, new RaceFlow(5, 3f).TotalLaps);
            Assert.AreEqual(0f, new RaceFlow(3, -1f).CountdownSeconds);
        }

        [Test] public void Countdown_Labels_ThreeTwoOneGo_AndLocksEveryone()
        {
            var f = Field();
            Assert.AreEqual("3", f.CountdownLabel); Assert.IsFalse(f.CanDrive("Player")); Assert.IsFalse(f.CanDrive("AI_1"));
            f.Tick(1f); Assert.AreEqual("2", f.CountdownLabel);
            f.Tick(1f); Assert.AreEqual("1", f.CountdownLabel); Assert.IsFalse(f.CanDrive("AI_2"));
            f.Tick(1f); Assert.AreEqual(RacePhase.Racing, f.Phase); Assert.AreEqual("GO", f.CountdownLabel);
            Assert.IsTrue(f.CanDrive("Player")); Assert.IsTrue(f.CanDrive("AI_1"));
            f.Tick(1.1f); Assert.AreEqual("", f.CountdownLabel);
        }

        [Test] public void RaceClock_StartsAtGo_CarryingOvershoot()
        {
            var f = Field();
            f.Tick(2.5f); Assert.AreEqual(0f, f.RaceTime);
            f.Tick(0.75f); Assert.AreEqual(0.25f, f.RaceTime, 1e-5f);
            f.Tick(1f); Assert.AreEqual(1.25f, f.RaceTime, 1e-5f);
        }

        [Test] public void ZeroCountdown_StartsRacingImmediately_ForTests()
        {
            var f = Field(3, 0f);
            Assert.AreEqual(RacePhase.Racing, f.Phase); Assert.IsTrue(f.CanDrive("Player"));
        }

        [Test] public void LapsBeforeGo_DoNotFinish()
        {
            var f = Field(1);
            Assert.IsFalse(f.OnLapCompleted("AI_1", 1));
            Assert.IsFalse(f.IsFinished("AI_1"));
        }

        [Test] public void Finish_OnlyAtConfiguredLaps_OnceEach_AndLocksThatCar()
        {
            var f = Field(3, 0f);
            Assert.IsFalse(f.OnLapCompleted("AI_1", 2));
            f.Tick(10f);
            Assert.IsTrue(f.OnLapCompleted("AI_1", 3));
            Assert.IsFalse(f.OnLapCompleted("AI_1", 4), "a car finishes once");
            Assert.IsFalse(f.CanDrive("AI_1")); Assert.IsTrue(f.CanDrive("Player"));
            Assert.IsFalse(f.PlayerFinished);
        }

        [Test] public void Results_FinishOrderAndTimes_UnfinishedReportedTruthfully()
        {
            var f = Field(3, 0f);
            f.Tick(60f); f.OnLapCompleted("AI_2", 3);
            f.Tick(1.5f); f.OnLapCompleted("Player", 3);
            Assert.IsTrue(f.PlayerFinished); Assert.AreEqual(RacePhase.Racing, f.Phase, "AI keep racing after the player");
            var r = f.Results();
            Assert.AreEqual("AI_2", r[0].Id); Assert.AreEqual(1, r[0].Position); Assert.AreEqual(60f, r[0].Time, 1e-4f);
            Assert.AreEqual("Player", r[1].Id); Assert.AreEqual(2, r[1].Position); Assert.AreEqual(61.5f, r[1].Time, 1e-4f);
            Assert.AreEqual("AI_1", r[2].Id); Assert.IsFalse(r[2].Finished); Assert.AreEqual(0, r[2].Position);
            f.Tick(5f); f.OnLapCompleted("AI_1", 3);
            Assert.AreEqual(RacePhase.Finished, f.Phase);
            Assert.AreEqual("AI_1", f.Results()[2].Id); Assert.AreEqual(3, f.Results()[2].Position);
        }

        [Test] public void SameTimeFinishes_KeepReportOrder_Deterministically()
        {
            var f = Field(1, 0f);
            f.Tick(30f); f.OnLapCompleted("AI_1", 1); f.OnLapCompleted("Player", 1);
            var r = f.Results();
            Assert.AreEqual("AI_1", r[0].Id); Assert.AreEqual("Player", r[1].Id);
            Assert.AreEqual(r[0].Time, r[1].Time);
        }

        [Test] public void Restart_ResetsCountdownClockAndFinishes()
        {
            var f = Field(1);
            f.Tick(3f); f.Tick(20f); f.OnLapCompleted("Player", 1);
            f.Restart();
            Assert.AreEqual(RacePhase.Countdown, f.Phase); Assert.AreEqual(0f, f.RaceTime);
            Assert.AreEqual("3", f.CountdownLabel); Assert.IsFalse(f.PlayerFinished);
            Assert.IsFalse(f.IsFinished("Player"));
            foreach (var x in f.Results()) Assert.IsFalse(x.Finished);
        }
    }
}
