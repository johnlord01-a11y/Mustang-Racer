using System.Collections;
using NUnit.Framework;
using OpenWheelRacer.Race;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// <summary>Visibility pass: both barriers contrast with the asphalt, and edge lines are visual only.</summary>
    public sealed class TrackVisibilityPlayTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static float Contrast(Color a, Color b)
        {
            float la = Luma(a) + 0.05f, lb = Luma(b) + 0.05f;
            return Mathf.Max(la, lb) / Mathf.Min(la, lb);
        }

        [Test]
        public void EveryWallPanel_ContrastsWithAsphalt()
        {
            for (int i = 0; i < 4; i++)
            {
                Assert.GreaterOrEqual(Contrast(TrackBuilder.WallPanelColor(true, i), TrackBuilder.RoadColor), 2.5f, $"inner panel {i}");
                Assert.GreaterOrEqual(Contrast(TrackBuilder.WallPanelColor(false, i), TrackBuilder.RoadColor), 2.5f, $"outer panel {i}");
            }

            Assert.AreNotEqual(TrackBuilder.WallPanelColor(true, 0), TrackBuilder.WallPanelColor(true, 1), "inner panels must alternate");
            Assert.AreNotEqual(TrackBuilder.WallPanelColor(false, 0), TrackBuilder.WallPanelColor(false, 1), "outer panels must alternate");
            Assert.GreaterOrEqual(Contrast(TrackBuilder.EdgeLineColor, TrackBuilder.RoadColor), 7f, "edge lines must stand out");
        }

        [UnityTest]
        public IEnumerator EdgeLines_OnePairPerSegment_NoColliders_WallsKeepColliders()
        {
            _root = new GameObject("VisibilityTrack");
            TrackBuilder track = _root.AddComponent<TrackBuilder>();
            if (!track.HasBuilt) track.Build();
            yield return null;

            Transform walls = _root.transform.Find("Walls");
            Transform lines = _root.transform.Find("EdgeLines");
            Assert.IsNotNull(walls, "no Walls root");
            Assert.IsNotNull(lines, "no EdgeLines root");
            Assert.AreEqual(walls.childCount, lines.childCount, "one inner and one outer line per wall pair");
            Assert.AreEqual(0, lines.GetComponentsInChildren<Collider>(true).Length, "edge lines must not collide");
            Assert.AreEqual(walls.childCount, walls.GetComponentsInChildren<Collider>(true).Length, "every wall keeps its collider");

            Renderer a = walls.Find("Wall_Inner_000").GetComponent<Renderer>();
            Renderer b = walls.Find("Wall_Inner_001").GetComponent<Renderer>();
            Assert.AreNotSame(a.sharedMaterial, b.sharedMaterial, "inner wall panels alternate materials");
            Assert.AreSame(a.sharedMaterial, walls.Find("Wall_Inner_002").GetComponent<Renderer>().sharedMaterial, "panels share materials");
        }
    }
}