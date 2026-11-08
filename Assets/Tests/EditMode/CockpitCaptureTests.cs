using NUnit.Framework;

namespace OpenWheelRacer.Tests.EditMode
{
    public sealed class CockpitCaptureTests
    {
        [Test] public void NoFlag_ReturnsNull() =>
            Assert.IsNull(CockpitCapture.ParseCapturePath(new[] { "game", "-batchmode" }));

        [Test] public void AbsolutePath_IsReturned() =>
            Assert.AreEqual("/tmp/a.png", CockpitCapture.ParseCapturePath(new[] { "game", "--capture-cockpit", "/tmp/a.png" }));

        [Test] public void RelativeOrMissingPath_IsRejected()
        {
            Assert.IsNull(CockpitCapture.ParseCapturePath(new[] { "game", "--capture-cockpit", "a.png" }));
            Assert.IsNull(CockpitCapture.ParseCapturePath(new[] { "game", "--capture-cockpit" }));
            Assert.IsNull(CockpitCapture.ParseCapturePath(new[] { "game", "--capture-cockpit", "-logFile" }));
            Assert.IsNull(CockpitCapture.ParseCapturePath(null));
        }
    }
}
