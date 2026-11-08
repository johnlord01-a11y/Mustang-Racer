using NUnit.Framework;
using UnityEngine;

namespace OpenWheelRacer.Race.Tests
{
    /// <summary>
    /// The map projection of <see cref="Minimap"/>: a pure function from world space
    /// into the map rect, tested at the three points that define it — the bounds
    /// minimum lands on the rect's bottom-left corner, the maximum on the top-right,
    /// the centre in the middle — plus the behaviour the draw code leans on when the
    /// bounds collapse to nothing.
    /// </summary>
    public class MinimapTests
    {
        private static readonly Bounds Track = new Bounds(
            new Vector3(10f, 0f, -5f),
            new Vector3(200f, 2f, 100f));

        private static readonly Rect Map = new Rect(800f, 20f, 200f, 200f);

        private static void AssertClose(Vector2 expected, Vector2 actual)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-3f);
            Assert.AreEqual(expected.y, actual.y, 1e-3f);
        }

        [Test]
        public void WorldToMapBoundsMinMapsToBottomLeftCorner()
        {
            Vector2 point = Minimap.WorldToMap(Track.min, Track, Map);

            // Bottom-left of a GUI rect is x at the left edge, y at the BOTTOM,
            // which is the rect's maximum y — GUI coordinates grow downwards.
            AssertClose(new Vector2(Map.xMin, Map.yMax), point);
        }

        [Test]
        public void WorldToMapBoundsMaxMapsToTopRightCorner()
        {
            Vector2 point = Minimap.WorldToMap(Track.max, Track, Map);

            AssertClose(new Vector2(Map.xMax, Map.yMin), point);
        }

        [Test]
        public void WorldToMapBoundsCentreMapsToRectCentre()
        {
            Vector2 point = Minimap.WorldToMap(Track.center, Track, Map);

            AssertClose(new Vector2(Map.center.x, Map.center.y), point);
        }

        [Test]
        public void WorldToMapHeightReadsAlongWorldZ()
        {
            // "z up": the larger z sits higher on the map = smaller GUI y.
            Vector3 lowZ = Track.center - new Vector3(0f, 0f, Track.extents.z * 0.5f);
            Vector3 highZ = Track.center + new Vector3(0f, 0f, Track.extents.z * 0.5f);

            Vector2 low = Minimap.WorldToMap(lowZ, Track, Map);
            Vector2 high = Minimap.WorldToMap(highZ, Track, Map);

            Assert.Greater(low.y, high.y);
            Assert.AreEqual(low.x, high.x, 1e-3f);
        }

        [Test]
        public void WorldToMapIsLinearHalfway()
        {
            Vector3 quarter = Track.min + (Track.max - Track.min) * 0.25f;
            Vector2 point = Minimap.WorldToMap(quarter, Track, Map);

            AssertClose(new Vector2(Map.xMin + Map.width * 0.25f,
                Map.yMax - Map.height * 0.25f), point);
        }

        [Test]
        public void WorldToMapDegenerateBoundsLandInTheMiddle()
        {
            var flat = new Bounds(new Vector3(5f, 0f, 5f), Vector3.zero);

            Vector2 point = Minimap.WorldToMap(new Vector3(100f, 0f, -100f), flat, Map);

            AssertClose(new Vector2(Map.center.x, Map.center.y), point);
        }

        [Test]
        public void FitBoundsPadsTheCentreline()
        {
            var points = new System.Collections.Generic.List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(10f, 0f, 4f),
            };

            Bounds bounds = Minimap.FitBounds(points, 2f);

            Assert.AreEqual(-2f, bounds.min.x, 1e-3f);
            Assert.AreEqual(12f, bounds.max.x, 1e-3f);
            Assert.AreEqual(-2f, bounds.min.z, 1e-3f);
            Assert.AreEqual(6f, bounds.max.z, 1e-3f);
        }

        [Test]
        public void FitBoundsOfNothingIsFinite()
        {
            Bounds bounds = Minimap.FitBounds(
                new System.Collections.Generic.List<Vector3>(), 1f);

            Assert.IsTrue(bounds.size.x > 0f && bounds.size.z > 0f);
        }
    }
}
