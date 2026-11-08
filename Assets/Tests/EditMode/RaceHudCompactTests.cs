using NUnit.Framework;
using OpenWheelRacer.Race;
using UnityEngine;

namespace OpenWheelRacer.Tests.EditMode
{
    /// <summary>
    /// The HUD used to be drawn at fixed pixel sizes tuned for 1080p: a 480x230 panel
    /// with a 17 px font, a 72 px countdown and a 340x240 results box. Those are the
    /// "old numbers" below; the compact HUD must be at most 60% of them.
    /// </summary>
    public sealed class RaceHudCompactTests
    {
        // The pre-compaction numbers, kept here so the 60% rule is measured, not remembered.
        private const float OldPanelWidthPx = 480f;
        private const int OldFontSize = 17;
        private const float FullHdWidth = 1920f;

        [Test]
        public void RaceHud_Compact_AtFullHd()
        {
            // Panel width as a fraction of screen width, then and now, at 1080p.
            // HudScale is 1 by definition at 1080p; the constants are the 1080p base sizes.
            float oldFraction = OldPanelWidthPx / FullHdWidth;          // 0.25
            float newFraction = RaceHud.PanelWidth * 1f / FullHdWidth;  // scale = 1 at 1080p

            Assert.That(newFraction, Is.LessThanOrEqualTo(oldFraction * 0.6f + 0.0001f));
            Assert.That(RaceHud.FontSize, Is.LessThanOrEqualTo((int)(OldFontSize * 0.6f)));

            // Scale tracks Screen.height / 1080 and is clamped to [0.6, 1.5]; the headless
            // test player is not necessarily 1080p, so assert the clamp holds wherever we run.
            Assert.That(RaceHud.MinScale, Is.EqualTo(0.6f));
            Assert.That(RaceHud.MaxScale, Is.EqualTo(1.5f));
            Assert.That(RaceHud.HudScale(), Is.InRange(RaceHud.MinScale, RaceHud.MaxScale));

            // Panel still hugs the top-left corner with a small margin.
            Assert.That(RaceHud.PanelX, Is.LessThanOrEqualTo(20f));
            Assert.That(RaceHud.PanelY, Is.LessThanOrEqualTo(20f));
        }
    }
}
