using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenWheelRacer.Race;
using UnityEngine;

namespace OpenWheelRacer.Tests.EditMode
{
    /// <summary>
    /// The circuit the stage 3 track is built from, checked as pure maths: is the
    /// centreline a closed loop, is it as long as the two straights and two half turns
    /// it claims to be, do the checkpoints actually sit around it, and does the
    /// direction rule a <see cref="TrackCheckpoint"/> reports crossings by hold up.
    ///
    /// Nothing here instantiates a <see cref="TrackBuilder"/>: the builder is a bag of
    /// primitives whose shape comes from <see cref="TrackLayout"/>, so measuring the
    /// layout is what pins the scene down.
    /// </summary>
    [TestFixture]
    public sealed class TrackLayoutTests
    {
        /// <summary>Two straights plus one full circle of turns: 2*200 + 2*pi*60 = 776.99 m.</summary>
        private const float ExpectedPerimeter =
            2f * TrackLayout.StraightLength + 2f * Mathf.PI * TrackLayout.TurnRadius;

        private List<Pose> _centerline;

        [SetUp]
        public void SetUp()
        {
            _centerline = TrackLayout.Centerline();
        }

        /// <summary>Length of the sampled polyline, closing sample included.</summary>
        private static float PolylineLength(List<Pose> poses)
        {
            float total = 0f;
            for (int i = 0; i + 1 < poses.Count; i++)
            {
                total += Vector3.Distance(poses[i].position, poses[i + 1].position);
            }

            return total;
        }

        [Test]
        public void Centerline_IsAClosedLoopOnTheGround()
        {
            Assert.Greater(_centerline.Count, 8, "a handful of samples is not a circuit");

            float gap = Vector3.Distance(
                _centerline[0].position,
                _centerline[_centerline.Count - 1].position);
            Assert.IsTrue(gap < 3f, $"not closed: first and last samples are {gap:F2} m apart");

            foreach (Pose pose in _centerline)
            {
                Assert.AreEqual(0f, pose.position.y, 1e-3f, "samples must lie on the y = 0 plane");
            }
        }

        [Test]
        public void Centerline_MeasuresTwoStraightsAndTwoHalfTurns()
        {
            float length = PolylineLength(_centerline);

            Assert.AreEqual(
                ExpectedPerimeter,
                length,
                ExpectedPerimeter * 0.05f,
                "perimeter must be the two straights plus one full circle of turns");
        }

        [Test]
        public void Centerline_IsSampledAtTheRequestedPitch()
        {
            float pitch = PolylineLength(_centerline) / (_centerline.Count - 1);

            Assert.AreEqual(TrackLayout.SampleSpacing, pitch, 0.25f);

            // Chords cut the corner of every turn, so the measured pitch runs a hair under
            // the arc length each step covers (about 3 mm over the 188 steps of a turn).
            Assert.AreEqual(pitch, TrackLayout.RealisedSampleSpacing(), 0.01f);
        }

        [Test]
        public void Centerline_PosesLookWhereTheTrackIsGoing()
        {
            for (int i = 0; i + 1 < _centerline.Count; i++)
            {
                Vector3 step = _centerline[i + 1].position - _centerline[i].position;
                if (step.sqrMagnitude < 1e-8f)
                {
                    continue; // the closing sample repeats the first
                }

                Assert.Greater(
                    Vector3.Dot(_centerline[i].forward, step.normalized),
                    0.99f,
                    $"sample {i} is not facing along the path");
            }
        }

        [Test]
        public void Centerline_TurnsCounterClockwiseAsSeenFromAbove()
        {
            // Sum of the signed heading changes over a lap: one full revolution, whose sign
            // says which way. Unity is left-handed with y up, and a viewer above looking
            // straight down sees +x to the right and +z up the screen; counter-clockwise for
            // that viewer is +z swinging round towards -x, and whole turns of that sense
            // accumulate along -y.
            float yaw = 0f;
            for (int i = 0; i + 1 < _centerline.Count; i++)
            {
                Vector3 a = _centerline[i].forward;
                Vector3 b = _centerline[i + 1].forward;
                yaw += Mathf.Atan2(Vector3.Cross(a, b).y, Vector3.Dot(a, b));
            }

            Assert.AreEqual(-2f * Mathf.PI, yaw, 0.1f, "one counter-clockwise revolution per lap");
        }

        [Test]
        public void Centerline_TurnCentresAreWhereTheShapeSaysTheyAre()
        {
            // Middle of a turn is a quarter of its arc past the end of a straight, and the
            // car there is squarely sideways to the track.
            Pose firstTurn = TrackLayout.PoseAt(TrackLayout.StraightLength * 0.5f
                + TrackLayout.TurnArcLength * 0.5f);

            Assert.AreEqual(0f, firstTurn.position.x, 1e-2f);
            Assert.AreEqual(
                TrackLayout.StraightLength * 0.5f + TrackLayout.TurnRadius,
                firstTurn.position.z,
                1e-2f);
            Assert.AreEqual(-1f, firstTurn.forward.x, 1e-2f);
        }

        [Test]
        public void CheckpointPoses_DefaultsToFourPointsSpreadAroundTheLap()
        {
            Pose[] poses = TrackLayout.CheckpointPoses();

            Assert.AreEqual(TrackLayout.DefaultCheckpointCount, poses.Length);
            Assert.AreEqual(4, poses.Length);

            for (int i = 0; i < poses.Length; i++)
            {
                for (int j = i + 1; j < poses.Length; j++)
                {
                    Assert.Greater(
                        Vector3.Distance(poses[i].position, poses[j].position),
                        1f,
                        $"checkpoints {i} and {j} are in the same place");
                }
            }

            // Evenly spaced: every chord between neighbours — the wrap-around one included —
            // comes out the same length.
            float reference = Vector3.Distance(poses[0].position, poses[1].position);
            Assert.Greater(reference, 100f, "checkpoints must be spread round the lap, not bunched up");

            for (int i = 0; i < poses.Length; i++)
            {
                Pose next = poses[(i + 1) % poses.Length];
                Assert.AreEqual(
                    reference,
                    Vector3.Distance(poses[i].position, next.position),
                    0.05f,
                    $"the gap after checkpoint {i} differs from the others");
            }

            // And they sit around the interior instead of hugging one corner of the oval.
            Vector3 centroid = Vector3.zero;
            foreach (Pose pose in poses)
            {
                centroid += pose.position;
            }

            Assert.Less(Vector3.Distance(centroid / poses.Length, Vector3.zero), 1f);
        }

        [Test]
        public void CheckpointPoses_IndexZeroIsTheFinishLineOnTheStartStraight()
        {
            Pose line = TrackLayout.CheckpointPoses()[0];

            Assert.AreEqual(0f, line.position.y, 1e-3f);
            Assert.AreEqual(TrackLayout.TurnRadius, line.position.x, 1e-2f,
                "the line belongs in the middle of the start straight, at x = +TurnRadius");
            Assert.AreEqual(0f, line.position.z, 1e-2f, "the line sits at the middle of the straight");
            Assert.Greater(Vector3.Dot(line.forward, Vector3.forward), 0.999f,
                "the line faces the driving direction");
            // The line is exactly where arc length 0 is, i.e. the start of the lap.
            Assert.Less(
                Vector3.Distance(line.position, TrackLayout.PoseAt(0f).position),
                1e-3f);
        }

        [Test]
        public void CheckpointPoses_AllOfThemSitOnTheRoad()
        {
            Pose[] poses = TrackLayout.CheckpointPoses();

            foreach (Pose pose in poses)
            {
                float nearest = float.PositiveInfinity;
                foreach (Pose sample in _centerline)
                {
                    nearest = Mathf.Min(nearest, Vector3.Distance(pose.position, sample.position));
                }

                Assert.Less(nearest, TrackLayout.RealisedSampleSpacing(),
                    "a checkpoint was placed off the centreline");
            }
        }

        [Test]
        public void CheckpointPoses_CountsBelowOneAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TrackLayout.CheckpointPoses(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => TrackLayout.CheckpointPoses(-3));

            Assert.AreEqual(1, TrackLayout.CheckpointPoses(1).Length);
        }

        [Test]
        public void SpawnPose_SitsSixMetresBehindTheLineOnTheStartStraight()
        {
            Pose spawn = TrackLayout.SpawnPose();
            Pose line = TrackLayout.CheckpointPoses(1)[0];

            Assert.AreEqual(TrackLayout.SpawnHeight, spawn.position.y, 1e-3f);
            Assert.AreEqual(TrackLayout.TurnRadius, spawn.position.x, 1e-2f);
            Assert.AreEqual(-TrackLayout.SpawnDistanceBeforeLine, spawn.position.z, 1e-2f);
            Assert.Greater(Vector3.Dot(spawn.forward, Vector3.forward), 0.999f,
                "the grid slot faces down the straight, not back towards the line");
            Assert.AreEqual(
                TrackLayout.SpawnDistanceBeforeLine,
                Mathf.Abs(line.position.z - spawn.position.z),
                1e-2f);
        }

        [Test]
        public void IsForward_IsTrueForMotionAlongTheCheckpointForward()
        {
            Assert.IsTrue(TrackCheckpoint.IsForward(new Vector3(0f, 0f, 25f), Vector3.forward));
            Assert.IsTrue(TrackCheckpoint.IsForward(new Vector3(0f, 0f, 0.2f), Vector3.forward),
                "a crawling car still crossed forwards");

            Vector3 turned = Quaternion.Euler(0f, 90f, 0f) * Vector3.forward; // roughly +x
            Assert.IsTrue(TrackCheckpoint.IsForward(Vector3.right, turned),
                "the checkpoint may face any way, only its own axis matters");

            Assert.IsTrue(TrackCheckpoint.IsForward(new Vector3(5f, 0f, 5f), Vector3.forward),
                "sliding across while gaining ground along the track is a forward crossing");
        }

        [Test]
        public void IsForward_IsFalseForMotionAgainstTheCheckpointForward()
        {
            Assert.IsFalse(TrackCheckpoint.IsForward(new Vector3(0f, 0f, -25f), Vector3.forward));
            Assert.IsFalse(TrackCheckpoint.IsForward(new Vector3(0f, 0f, -0.2f), Vector3.forward),
                "reversing over a trigger is the case LapCounter must be able to ignore");

            Vector3 turned = Quaternion.Euler(0f, 90f, 0f) * Vector3.forward;
            Assert.IsFalse(TrackCheckpoint.IsForward(Vector3.left, turned));

            Assert.IsFalse(TrackCheckpoint.IsForward(new Vector3(-1f, 0f, -0.01f), Vector3.forward),
                "sideways drift with any backwards component reads as backwards");
        }

        [Test]
        public void IsForward_CountsSidewaysAndStationaryAsForward()
        {
            // The rule the trigger documents is dot >= 0, so only an actual backwards sweep
            // is refused. Perpendicular motion is not a crossing in either direction and
            // matters little: a crossing of a checkpoint that is not the expected one is
            // dropped by LapCounter regardless of the flag it carries.
            Assert.IsTrue(TrackCheckpoint.IsForward(Vector3.right, Vector3.forward));
            Assert.IsTrue(TrackCheckpoint.IsForward(Vector3.zero, Vector3.forward));
        }

        [Test]
        public void IsForward_DoesNotVouchForANonFiniteVelocity()
        {
            Assert.IsFalse(TrackCheckpoint.IsForward(new Vector3(float.NaN, 0f, 0f), Vector3.forward));
            Assert.IsFalse(TrackCheckpoint.IsForward(new Vector3(0f, 0f, float.NegativeInfinity), Vector3.forward));
        }
    }
}
