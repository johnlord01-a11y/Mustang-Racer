using System.Collections.Generic;
using NUnit.Framework;
using OpenWheelRacer.Race;

namespace OpenWheelRacer.Tests.EditMode
{
    /// <summary>
    /// Checkpoint-ordering rules for PLAN.md Stage 3 criterion 5 ("crossing checkpoints
    /// 1-2-3-finish increments lap exactly once; skipping one does not; driving
    /// backwards does not") and criterion 6 (best lap across 3 laps).
    /// Pure logic, so the clock is pushed in through Tick and no PlayMode scene is needed.
    /// </summary>
    [TestFixture]
    public sealed class LapCounterTests
    {
        private const int Checkpoints = 4; // 0 = start/finish, then 1, 2, 3

        private LapCounter _counter;
        private readonly List<float> _completedTimes = new List<float>();

        [SetUp]
        public void SetUp()
        {
            _counter = new LapCounter(Checkpoints);
            _completedTimes.Clear();
            _counter.LapCompleted += time => _completedTimes.Add(time);
        }

        /// <summary>Runs one clean lap: burns <paramref name="lapTime"/> seconds, then 1-2-3-0.</summary>
        private void CompleteLap(float lapTime)
        {
            _counter.Tick(lapTime * 0.5f);
            _counter.Tick(lapTime * 0.5f);

            for (int index = 1; index < Checkpoints; index++)
            {
                _counter.OnCheckpoint(index);
            }

            _counter.OnCheckpoint(0);
        }

        [Test]
        public void NewCounter_IsIdleAndExpectsTheStartLine()
        {
            Assert.AreEqual(0, _counter.CurrentLap);
            Assert.AreEqual(0, _counter.CompletedLaps);
            Assert.AreEqual(0f, _counter.CurrentLapTime, 1e-6f);
            Assert.IsFalse(_counter.IsRunning);
            Assert.AreEqual(LapCounter.NoCheckpoint, _counter.LastValidCheckpoint);
            Assert.AreEqual(0, _counter.NextExpectedCheckpoint);
            Assert.AreEqual(float.PositiveInfinity, _counter.BestLapTime);
            Assert.AreEqual(float.PositiveInfinity, _counter.LastLapTime);
            Assert.IsEmpty(_completedTimes);
        }

        [Test]
        public void Tick_BeforeTheRaceStarts_DoesNotAdvanceTime()
        {
            _counter.Tick(12.5f);
            _counter.Tick(12.5f);

            Assert.AreEqual(0f, _counter.CurrentLapTime, 1e-6f);
            Assert.AreEqual(0, _counter.CurrentLap);
        }

        [Test]
        public void FirstCrossingOfStartLine_StartsLapOne_WithoutCompletingALap()
        {
            Assert.IsFalse(_counter.OnCheckpoint(0));

            Assert.IsTrue(_counter.IsRunning);
            Assert.AreEqual(1, _counter.CurrentLap);
            Assert.AreEqual(0, _counter.CompletedLaps);
            Assert.AreEqual(0f, _counter.CurrentLapTime, 1e-6f);
            Assert.AreEqual(0, _counter.LastValidCheckpoint);
            Assert.AreEqual(1, _counter.NextExpectedCheckpoint);
            Assert.IsEmpty(_completedTimes);
        }

        [Test]
        public void Tick_AfterTheRaceStarts_AdvancesTheLapClock()
        {
            _counter.OnCheckpoint(0);
            _counter.Tick(1f);
            _counter.Tick(0.5f);
            _counter.Tick(0.25f);

            Assert.AreEqual(1.75f, _counter.CurrentLapTime, 1e-5f);
        }

        [Test]
        public void InOrderLap_CountsOnceAndRecordsItsTime()
        {
            _counter.OnCheckpoint(0);
            CompleteLap(80f);

            Assert.AreEqual(1, _counter.CompletedLaps);
            Assert.AreEqual(2, _counter.CurrentLap, "Completing a lap puts the car on the next one.");
            Assert.AreEqual(80f, _counter.LastLapTime, 1e-4f);
            Assert.AreEqual(80f, _counter.BestLapTime, 1e-4f);
            Assert.AreEqual(0f, _counter.CurrentLapTime, 1e-6f, "The new lap starts from zero.");
            Assert.AreEqual(0, _counter.LastValidCheckpoint);
            Assert.AreEqual(1, _counter.NextExpectedCheckpoint);

            Assert.AreEqual(1, _completedTimes.Count);
            Assert.AreEqual(80f, _completedTimes[0], 1e-4f);

            // Crossing the line again immediately is a re-entry, not a second lap.
            Assert.IsFalse(_counter.OnCheckpoint(0));
            Assert.AreEqual(1, _counter.CompletedLaps);
            Assert.AreEqual(1, _completedTimes.Count);
        }

        [Test]
        public void InOrderLap_CountsOnce_WhenEverySegmentIsTickedSeparately()
        {
            _counter.OnCheckpoint(0);
            _counter.OnCheckpoint(1);
            _counter.Tick(10f);
            _counter.OnCheckpoint(2);
            _counter.Tick(20f);
            _counter.OnCheckpoint(3);
            _counter.Tick(30f);

            Assert.AreEqual(0, _counter.CompletedLaps, "The line has not been crossed yet.");
            Assert.AreEqual(3, _counter.LastValidCheckpoint);
            Assert.AreEqual(0, _counter.NextExpectedCheckpoint);

            Assert.IsTrue(_counter.OnCheckpoint(0));
            Assert.AreEqual(1, _counter.CompletedLaps);
            Assert.AreEqual(60f, _counter.LastLapTime, 1e-4f);
            Assert.AreEqual(1, _completedTimes.Count);
        }

        [Test]
        public void SkippingACheckpoint_DoesNotCountTheLap()
        {
            _counter.OnCheckpoint(0);
            _counter.OnCheckpoint(1);
            _counter.Tick(45f);
            _counter.OnCheckpoint(2);
            _counter.Tick(45f);
            // Checkpoint 3 skipped - jumping straight to the line must not count.
            Assert.IsFalse(_counter.OnCheckpoint(0));

            Assert.AreEqual(0, _counter.CompletedLaps);
            Assert.AreEqual(1, _counter.CurrentLap);
            Assert.AreEqual(float.PositiveInfinity, _counter.LastLapTime);
            Assert.AreEqual(2, _counter.LastValidCheckpoint, "Progress holds at the last honest checkpoint.");
            Assert.AreEqual(3, _counter.NextExpectedCheckpoint);
            Assert.AreEqual(90f, _counter.CurrentLapTime, 1e-4f, "Ignored crossings keep the clock running.");
            Assert.IsEmpty(_completedTimes);

            // ... and finishing the lap properly afterwards still counts exactly once.
            _counter.OnCheckpoint(3);
            Assert.IsTrue(_counter.OnCheckpoint(0));
            Assert.AreEqual(1, _counter.CompletedLaps);
            Assert.AreEqual(1, _completedTimes.Count);
        }

        [Test]
        public void JumpingAheadOrHittingTheSameCheckpointTwice_IsIgnored()
        {
            _counter.OnCheckpoint(0);

            Assert.IsFalse(_counter.OnCheckpoint(2), "Cannot skip 1 and take 2.");
            Assert.AreEqual(1, _counter.NextExpectedCheckpoint);
            Assert.AreEqual(0, _counter.LastValidCheckpoint);

            Assert.IsFalse(_counter.OnCheckpoint(1));
            Assert.IsFalse(_counter.OnCheckpoint(1), "Re-entry of the same trigger is not a second crossing.");
            Assert.AreEqual(2, _counter.NextExpectedCheckpoint);
            Assert.AreEqual(1, _counter.LastValidCheckpoint);
            Assert.AreEqual(0, _counter.CompletedLaps);
        }

        [Test]
        public void DrivingBackwardsThroughEveryCheckpoint_DoesNotCountALap()
        {
            _counter.OnCheckpoint(0);
            _counter.Tick(5f);

            // Reversed sweep: 3, 2, 1, then the line again — each one reported as a
            // backwards crossing, which the counter must ignore outright.
            Assert.IsFalse(_counter.OnCheckpoint(3, false));
            Assert.IsFalse(_counter.OnCheckpoint(2, false));
            Assert.IsFalse(_counter.OnCheckpoint(1, false));
            Assert.IsFalse(_counter.OnCheckpoint(0, false));

            Assert.AreEqual(0, _counter.CompletedLaps);
            Assert.AreEqual(1, _counter.CurrentLap);
            Assert.AreEqual(float.PositiveInfinity, _counter.LastLapTime);
            Assert.AreEqual(0, _counter.LastValidCheckpoint, "Reversed crossings keep the honest progress.");
            Assert.AreEqual(1, _counter.NextExpectedCheckpoint);
            Assert.IsEmpty(_completedTimes);
        }

        [Test]
        public void ForwardCrossingOfTheExpectedCheckpoint_IsAccepted()
        {
            _counter.OnCheckpoint(0);
            _counter.Tick(5f);

            Assert.IsFalse(_counter.OnCheckpoint(1, true),
                "1 is the next expected checkpoint, so it counts but owes no lap.");

            Assert.AreEqual(1, _counter.LastValidCheckpoint, "A forward crossing advances progress.");
            Assert.AreEqual(2, _counter.NextExpectedCheckpoint);
            Assert.AreEqual(0, _counter.CompletedLaps);
            Assert.AreEqual(1, _counter.CurrentLap);
            Assert.AreEqual(5f, _counter.CurrentLapTime, 1e-5f, "An accepted crossing leaves the clock alone.");
            Assert.IsEmpty(_completedTimes);
        }

        [Test]
        public void StartingByDrivingBackwardsOverTheLine_StillJustStartsTheRace()
        {
            _counter.OnCheckpoint(0);

            Assert.IsTrue(_counter.IsRunning);
            Assert.AreEqual(1, _counter.CurrentLap);
            Assert.AreEqual(0, _counter.CompletedLaps);
        }

        [Test]
        public void BestLap_IsTheMinimumOfThreeLaps()
        {
            _counter.OnCheckpoint(0);
            CompleteLap(90f);
            CompleteLap(80.5f);
            CompleteLap(85f);

            Assert.AreEqual(3, _counter.CompletedLaps);
            Assert.AreEqual(4, _counter.CurrentLap);
            Assert.AreEqual(85f, _counter.LastLapTime, 1e-4f, "LastLapTime tracks recency, not speed.");
            Assert.AreEqual(80.5f, _counter.BestLapTime, 1e-4f);
            Assert.AreEqual(3, _completedTimes.Count);
            Assert.AreEqual(90f, _completedTimes[0], 1e-4f);
            Assert.AreEqual(80.5f, _completedTimes[1], 1e-4f);
            Assert.AreEqual(85f, _completedTimes[2], 1e-4f);
        }

        [Test]
        public void TieForBest_IsKeptAndStillReportsTheMinimum()
        {
            _counter.OnCheckpoint(0);
            CompleteLap(75f);
            CompleteLap(75f);

            Assert.AreEqual(2, _counter.CompletedLaps);
            Assert.AreEqual(75f, _counter.BestLapTime, 1e-4f);
        }

        [Test]
        public void Tick_IgnoresNegativeAndNonFiniteDelta()
        {
            _counter.OnCheckpoint(0);
            _counter.Tick(10f);
            _counter.Tick(-5f);
            _counter.Tick(0f);
            _counter.Tick(float.NaN);

            Assert.AreEqual(10f, _counter.CurrentLapTime, 1e-5f);
        }

        [Test]
        public void OutOfRangeCheckpointIndices_AreIgnored()
        {
            _counter.OnCheckpoint(0);

            Assert.IsFalse(_counter.OnCheckpoint(-1));
            Assert.IsFalse(_counter.OnCheckpoint(Checkpoints));
            Assert.IsFalse(_counter.OnCheckpoint(int.MaxValue));
            Assert.AreEqual(1, _counter.CurrentLap);
            Assert.AreEqual(1, _counter.NextExpectedCheckpoint);
        }

        [Test]
        public void SingleCheckpointTrack_CountsEveryCrossingAfterTheFirst()
        {
            var single = new LapCounter(1);
            Assert.IsFalse(single.OnCheckpoint(0), "First crossing starts the race.");
            Assert.IsTrue(single.OnCheckpoint(0));
            Assert.IsTrue(single.OnCheckpoint(0));
            Assert.AreEqual(2, single.CompletedLaps);
            Assert.AreEqual(3, single.CurrentLap);
        }

        [Test]
        public void Reset_ReturnsToTheColdStartState()
        {
            _counter.OnCheckpoint(0);
            CompleteLap(80f);
            CompleteLap(70f);

            _counter.Reset();

            Assert.IsFalse(_counter.IsRunning);
            Assert.AreEqual(0, _counter.CurrentLap);
            Assert.AreEqual(0, _counter.CompletedLaps);
            Assert.AreEqual(0f, _counter.CurrentLapTime, 1e-6f);
            Assert.AreEqual(LapCounter.NoCheckpoint, _counter.LastValidCheckpoint);
            Assert.AreEqual(0, _counter.NextExpectedCheckpoint);
            Assert.AreEqual(float.PositiveInfinity, _counter.LastLapTime);
            Assert.AreEqual(float.PositiveInfinity, _counter.BestLapTime);
            Assert.AreEqual(2, _completedTimes.Count, "Reset must not replay events.");

            _counter.Tick(5f);
            Assert.AreEqual(0f, _counter.CurrentLapTime, "A reset counter ignores the clock until restarted.");
        }

        [Test]
        public void Constructor_RejectsATrackWithNoCheckpoints()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new LapCounter(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new LapCounter(-3));
        }
    }
}
