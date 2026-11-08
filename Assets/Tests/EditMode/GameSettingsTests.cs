using NUnit.Framework;
using UnityEngine;

namespace OpenWheelRacer.Race.Tests
{
    /// <summary>
    /// The two persisted switches of <see cref="GameSettings"/>: the quality setter
    /// must never store an index this build cannot honour — a negative index or one
    /// past the level list comes back clamped — and both setters must survive a
    /// re-read, because surviving the re-read is the whole point of saving them.
    /// Whatever the machine held before the tests is put back afterwards.
    /// </summary>
    public class GameSettingsTests
    {
        private int _savedQuality;
        private int _savedVSync;

        [SetUp]
        public void SetUp()
        {
            _savedQuality = PlayerPrefs.GetInt(GameSettings.QualityKey,
                GameSettings.DefaultQualityIndex);
            _savedVSync = PlayerPrefs.GetInt(GameSettings.VSyncKey, 0);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.SetInt(GameSettings.QualityKey, _savedQuality);
            PlayerPrefs.SetInt(GameSettings.VSyncKey, _savedVSync);
            PlayerPrefs.Save();
        }

        [Test]
        public void SetQualityClampsToAValidIndexAndPersists()
        {
            int levelCount = QualitySettings.names.Length;
            Assert.Greater(levelCount, 0, "the test project should have quality levels");

            int high = GameSettings.SetQuality(levelCount + 5);
            Assert.AreEqual(levelCount - 1, high);
            Assert.AreEqual(high, QualitySettings.GetQualityLevel());
            Assert.AreEqual(high, PlayerPrefs.GetInt(GameSettings.QualityKey, -99));

            int low = GameSettings.SetQuality(-3);
            Assert.AreEqual(0, low);
            Assert.AreEqual(0, PlayerPrefs.GetInt(GameSettings.QualityKey, -99));
        }

        [Test]
        public void SetQualityKeepsAValidIndex()
        {
            Assert.Greater(QualitySettings.names.Length, 1,
                "the test project should ship more than one quality level");

            int chosen = GameSettings.SetQuality(1);

            Assert.AreEqual(1, chosen);
            Assert.AreEqual(1, PlayerPrefs.GetInt(GameSettings.QualityKey, -99));
            Assert.AreEqual(1, QualitySettings.GetQualityLevel());
        }

        [Test]
        public void SetVSyncPersists()
        {
            GameSettings.SetVSync(true);
            Assert.IsTrue(GameSettings.VSync);
            Assert.AreEqual(1, PlayerPrefs.GetInt(GameSettings.VSyncKey, -99));

            GameSettings.SetVSync(false);
            Assert.IsFalse(GameSettings.VSync);
            Assert.AreEqual(0, PlayerPrefs.GetInt(GameSettings.VSyncKey, -99));
        }

        [Test]
        public void ApplyHonoursWhatWasSaved()
        {
            GameSettings.SetVSync(true);
            GameSettings.SetQuality(1);

            GameSettings.Apply();

            Assert.IsTrue(GameSettings.VSync);
            Assert.AreEqual(1, QualitySettings.GetQualityLevel());
        }
    }
}
