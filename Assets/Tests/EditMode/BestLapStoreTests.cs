using NUnit.Framework;
using OpenWheelRacer.Race;
using UnityEngine;

namespace OpenWheelRacer.Tests.EditMode
{
    /// <summary>
    /// The persistent best-lap record. A throwaway track id keeps these tests off the
    /// real "Track_Proto" record, and TearDown deletes the key whatever happened, so
    /// PlayerPrefs leaves the suite the way it found it.
    /// </summary>
    public sealed class BestLapStoreTests
    {
        private const string TestTrack = "TestTrack_BestLapStore";

        [TearDown]
        public void TearDown()
        {
            BestLapStore.Clear(TestTrack);
        }

        [Test]
        public void Submit_Stores_AndTryGetReadsItBack()
        {
            Assert.IsFalse(BestLapStore.TryGet(TestTrack, out _), "a fresh track has no record");

            Assert.IsTrue(BestLapStore.Submit(TestTrack, 92.345f), "the first lap is a new best");

            Assert.IsTrue(BestLapStore.TryGet(TestTrack, out float stored));
            Assert.AreEqual(92.345f, stored, 0.0001f);

            // And it really went to disk, not just to memory.
            Assert.IsTrue(PlayerPrefs.HasKey("bestlap." + TestTrack));
        }

        [Test]
        public void Submit_SlowerTime_DoesNotReplaceFaster()
        {
            Assert.IsTrue(BestLapStore.Submit(TestTrack, 90f));
            Assert.IsTrue(BestLapStore.Submit(TestTrack, 88f), "a faster lap replaces the record");

            Assert.IsFalse(BestLapStore.Submit(TestTrack, 91f), "a slower lap is refused");
            Assert.IsFalse(BestLapStore.Submit(TestTrack, 88f), "an equal lap is refused too");

            Assert.IsTrue(BestLapStore.TryGet(TestTrack, out float stored));
            Assert.AreEqual(88f, stored, 0.0001f);
        }

        [Test]
        public void Clear_RemovesIt()
        {
            BestLapStore.Submit(TestTrack, 85f);
            Assert.IsTrue(BestLapStore.TryGet(TestTrack, out _));

            BestLapStore.Clear(TestTrack);

            Assert.IsFalse(BestLapStore.TryGet(TestTrack, out float stored));
            Assert.AreEqual(0f, stored, "a cleared record reads as no time, not a stale one");
        }
    }
}
