using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenWheelRacer.Race;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// <summary>
    /// Seam survey of the primitive circuit. The barrier a car hits is not one wall but 98 boxes laid end
    /// to end along <see cref="TrackLayout.PoseAt(float)"/>, each the <em>chord</em> of its own stretch, so
    /// neighbours share an endpoint instead of meeting corner to corner — and the road ribbon is 98 more.
    /// What a wheel finds is therefore the junction between two pieces, which no test that only asks
    /// whether the track has been built can see; both tests measure that junction and log it as <c>[Track]</c>.
    /// </summary>
    [TestFixture]
    public sealed class TrackContactPlayTests
    {
        // ---- the pieces, by the names TrackBuilder gives them ---------------------------------------

        // Piece names as the builder writes them, index last and zero-padded: the order it laid them in.
        private const string OuterWallPrefix = "Wall_Outer_";
        private const string InnerWallPrefix = "Wall_Inner_";
        private const string RoadPiecePrefix = "Road_";

        /// <summary>98 pieces a side at <see cref="TrackBuilder.SegmentSpacing"/> over a 777 m perimeter;
        /// a count near the builder's own floor of 8 means the hierarchy was walked wrongly.</summary>
        private const int MinimumPieceCount = 8;

        /// <summary>Pieces are unit cubes scaled to their size, so a face is local ±0.5 and the far end —
        /// the end the next piece is joined to — is local +0.5 along Z.</summary>
        private const float PieceFace = 0.5f;

        /// <summary>The road-facing face of a wall as a signed local X on that cube: the outer wall stands
        /// at <c>+TrackBuilder.WallOffset</c> and so has the road at its local −X, the inner the other way,
        /// and both are asked about the only face a car can be caught by.</summary>
        private const float RoadFacingNegativeX = -0.5f;
        private const float RoadFacingPositiveX = 0.5f;

        // ---- what a seam has to hold ------------------------------------------------------------------

        /// <summary>How far the road-facing corner of one wall piece may stand proud of the piece after it
        /// (m) before the junction counts as open. Not a tight figure and it cannot be: the boxes are
        /// chords, so through a turn the far corner of one stands some 24 mm outside the face of the next —
        /// half a <see cref="TrackBuilder.SegmentOverlap"/> past the shared endpoint on a chord turned 7.6°
        /// from its neighbour's, worst on the inner wall, whose chord is the shortest of the three. The
        /// gate is for a junction that has come <em>apart</em>, and that is measured in decimetres.</summary>
        private const float MaximumSeamGapM = 0.04f;

        /// <summary>The road ribbon's tolerance in the unit-cube terms its survey is measured in (±0.5 is
        /// a face). The road carries no colliders — the ground plane under it already stops a car — so its
        /// seams are read out of the transforms. 0.02 of a 12 m ribbon is 24 cm, against the couple of
        /// centimetres by which the corner of a box turned 7.6° past the next one's shoulder stands proud
        /// of the next side face; a piece that really failed to reach is short by decimetres.</summary>
        private const float RoadSeamTolerance = 0.02f;

        private GameObject _trackRoot;
        private TrackBuilder _track;

        [SetUp]
        public void SetUp()
        {
            _trackRoot = new GameObject("TrackContactSeamHarness");
            _track = _trackRoot.AddComponent<TrackBuilder>();

            // AddComponent ran Awake, which builds on awake by default; the explicit call is the
            // same fallback the bootstrap uses.
            if (!_track.HasBuilt)
            {
                _track.Build();
            }

            Assert.IsTrue(_track.HasBuilt, "builder did not build the circuit when asked");
        }

        [TearDown]
        public void TearDown()
        {
            if (_trackRoot != null) Object.Destroy(_trackRoot);
            _trackRoot = null;
            _track = null;
        }

        /// <summary>Both walls, junction by junction, at the face the traffic sees: the road-facing corner
        /// of every piece at the end the next piece is joined to has to lie inside that next piece's
        /// BoxCollider — the surface a wheel is stopped by — all the way round, the last piece wrapping to
        /// the first.</summary>
        [UnityTest]
        public IEnumerator OuterAndInnerWalls_AreContinuous()
        {
            yield return null; // one frame with every piece where the builder put it

            float outer = MeasureWallSeams("outer", OuterWallPrefix, RoadFacingNegativeX, out string outerAt);
            float inner = MeasureWallSeams("inner", InnerWallPrefix, RoadFacingPositiveX, out string innerAt);

            Assert.That(outer, Is.LessThan(MaximumSeamGapM), $"outer wall gap {outer:F4} m at {outerAt}");
            Assert.That(inner, Is.LessThan(MaximumSeamGapM), $"inner wall gap {inner:F4} m at {innerAt}");
        }

        /// <summary>The same junction on the tarmac, where there is no collider to ask: the corner of each
        /// road piece at either edge has to fall inside the box of the piece after it, read in that piece's
        /// own unit-cube frame, where ±0.5 is a face.</summary>
        [UnityTest]
        public IEnumerator Road_HasNoEdgeGaps()
        {
            yield return null;

            List<Transform> pieces = PiecesByName(RoadPiecePrefix);
            Assert.That(pieces.Count, Is.GreaterThanOrEqualTo(MinimumPieceCount),
                $"{RoadPiecePrefix}### pieces are missing beneath the builder");

            float limit = PieceFace + RoadSeamTolerance;
            float worstReach = 0f, worstX = 0f, worstZ = 0f;
            string worst = "none";

            for (int i = 0; i < pieces.Count; i++)
            {
                Transform piece = pieces[i];
                Transform next = pieces[(i + 1) % pieces.Count];

                for (int edge = 0; edge < 2; edge++)
                {
                    Vector3 local = next.InverseTransformPoint(piece.TransformPoint(
                        new Vector3(edge == 0 ? -PieceFace : PieceFace, 0f, PieceFace)));

                    float reach = Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.z));
                    if (reach <= worstReach) continue;

                    worstReach = reach;
                    worstX = Mathf.Abs(local.x);
                    worstZ = Mathf.Abs(local.z);
                    worst = $"{piece.name} -> {next.name}";
                }
            }

            Debug.Log($"[Track] road max edge reach {worstReach:F4} of a piece (|x| {worstX:F4}, " +
                      $"|z| {worstZ:F4}) at {worst}, allowed {limit:F4} across {pieces.Count} pieces");

            Assert.That(worstReach, Is.LessThanOrEqualTo(limit),
                $"the road edge at {worst} does not reach into the piece after it");
        }

        /// <summary>The road-facing corner of every piece of one wall — a local ±0.5 of a cube scaled by
        /// <see cref="TrackBuilder.WallThickness"/> — asked of the next piece's BoxCollider.</summary>
        private float MeasureWallSeams(string side, string prefix, float roadFacingX, out string worstAt)
        {
            List<Transform> pieces = PiecesByName(prefix);
            Assert.That(pieces.Count, Is.GreaterThanOrEqualTo(MinimumPieceCount),
                $"{prefix}### pieces are missing beneath the builder");

            float maxGap = 0f; worstAt = "none";

            for (int i = 0; i < pieces.Count; i++)
            {
                Transform piece = pieces[i];
                Transform next = pieces[(i + 1) % pieces.Count];
                BoxCollider box = next.GetComponent<BoxCollider>();
                Assert.That(box, Is.Not.Null, $"{next.name} carries no BoxCollider to be joined to");

                Vector3 corner = piece.TransformPoint(new Vector3(roadFacingX, 0f, PieceFace));

                // BoxCollider.ClosestPoint reads the physics-world pose, which can lag a transform the
                // builder has just moved; measure the same ±0.5 unit cube with transforms alone.
                Vector3 local = next.InverseTransformPoint(corner);
                Vector3 clamped = new Vector3(
                    Mathf.Clamp(local.x, -0.5f, 0.5f),
                    Mathf.Clamp(local.y, -0.5f, 0.5f),
                    Mathf.Clamp(local.z, -0.5f, 0.5f));
                float gap = Vector3.Distance(next.TransformPoint(clamped), corner);
                if (gap > maxGap)
                {
                    maxGap = gap;
                    worstAt = $"{piece.name} -> {next.name}";
                }
            }

            Debug.Log($"[Track] {side} max gap {maxGap:F4} m at {worstAt} across {pieces.Count} pieces, " +
                      $"gate {MaximumSeamGapM:F2} m");
            return maxGap;
        }

        /// <summary>The pieces beneath the builder's Road/Walls roots, in the order it laid them — the
        /// index it wrote last in the name is zero-padded, so sorting the names sorts the string.</summary>
        private List<Transform> PiecesByName(string prefix)
        {
            var found = new List<Transform>();
            foreach (Transform root in _track.transform)
            {
                foreach (Transform piece in root)
                {
                    if (piece.name.StartsWith(prefix, System.StringComparison.Ordinal))
                    {
                        found.Add(piece);
                    }
                }
            }

            found.Sort((first, second) => string.CompareOrdinal(first.name, second.name));
            return found;
        }
    }
}

