using NUnit.Framework;
using OpenWheelRacer.Race;

namespace OpenWheelRacer.Tests.EditMode
{
    /// <summary>
    /// "mm:ss.fff" formatting per PLAN.md Stage 3 criterion 6 (the HUD reads like a
    /// stopwatch) and Stage 4's TimeFormat helper. Culture-independent, so these
    /// assertions hold on a machine whose locale uses a comma decimal separator.
    /// </summary>
    [TestFixture]
    public sealed class TimeFormatTests
    {
        [Test]
        public void Format_Zero_IsAllZeros()
        {
            Assert.AreEqual("00:00.000", TimeFormat.Format(0f));
        }

        [Test]
        public void Format_OneMinutesTwentyThreeSecondsFourFiveSix()
        {
            Assert.AreEqual("01:23.456", TimeFormat.Format(83.456f));
        }

        [Test]
        public void Format_SubSecondAndWholeSecondTimes()
        {
            Assert.AreEqual("00:00.001", TimeFormat.Format(0.001f));
            Assert.AreEqual("00:00.999", TimeFormat.Format(0.999f));
            Assert.AreEqual("00:01.000", TimeFormat.Format(1f));
            Assert.AreEqual("00:59.999", TimeFormat.Format(59.999f));
            Assert.AreEqual("01:00.000", TimeFormat.Format(60f));
            Assert.AreEqual("10:00.000", TimeFormat.Format(600f));
        }

        [Test]
        public void Format_RoundsToTheNearestMillisecond()
        {
            Assert.AreEqual("00:00.000", TimeFormat.Format(0.0004f));
            Assert.AreEqual("00:00.001", TimeFormat.Format(0.0005f));
            Assert.AreEqual("00:12.346", TimeFormat.Format(12.3456f));
            Assert.AreEqual("00:02.000", TimeFormat.Format(1.9995f));
        }

        [Test]
        public void Format_CarriesMillisecondsIntoSecondsAndMinutes()
        {
            Assert.AreEqual("00:01.000", TimeFormat.Format(0.9999f));
            Assert.AreEqual("01:00.000", TimeFormat.Format(59.9999f));
        }

        [Test]
        public void Format_NegativeValuesAreInvalid()
        {
            Assert.AreEqual("--:--.---", TimeFormat.Format(-1f));
            Assert.AreEqual("--:--.---", TimeFormat.Format(-0.001f));
            Assert.AreEqual("--:--.---", TimeFormat.Format(-83.456f));
            Assert.AreEqual("--:--.---", TimeFormat.Format(float.NegativeInfinity));
        }

        [Test]
        public void Format_NonFiniteValuesAreInvalid()
        {
            Assert.AreEqual("--:--.---", TimeFormat.Format(float.NaN));
            Assert.AreEqual("--:--.---", TimeFormat.Format(float.PositiveInfinity));
        }

        [Test]
        public void Format_UnsetBestLapReadsAsPlaceholder()
        {
            var counter = new LapCounter(4);
            Assert.AreEqual(float.PositiveInfinity, counter.BestLapTime);
            Assert.AreEqual("--:--.---", TimeFormat.Format(counter.BestLapTime));
        }

        [Test]
        public void Format_EndToEndTimeFromTheCounter_RoundTripsThroughTheFormatter()
        {
            var counter = new LapCounter(4);
            float[] laps = { 90f, 83.456f, 85f };

            counter.OnCheckpoint(0);
            foreach (float lap in laps)
            {
                counter.Tick(lap);
                for (int checkpoint = 1; checkpoint < 4; checkpoint++)
                {
                    counter.OnCheckpoint(checkpoint);
                }
                counter.OnCheckpoint(0);
            }

            Assert.AreEqual("01:23.456", TimeFormat.Format(counter.BestLapTime));
            Assert.AreEqual("01:25.000", TimeFormat.Format(counter.LastLapTime));
            Assert.AreEqual("00:00.000", TimeFormat.Format(counter.CurrentLapTime));
        }

        [Test]
        public void Format_AboveTheNinetyNineMinuteCap_ClampsInsteadOfOverflowing()
        {
            Assert.AreEqual("99:59.999", TimeFormat.Format(TimeFormat.MaxSeconds));
            Assert.AreEqual("99:59.999", TimeFormat.Format(float.MaxValue));
            Assert.AreEqual("60:00.000", TimeFormat.Format(3600f));
        }
    }
}
