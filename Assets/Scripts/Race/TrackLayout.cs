using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// Geometry of the stage 3 test circuit: a closed stadium oval — two
    /// <see cref="StraightLength"/> m straights running parallel to Z, capped by two
    /// 180 degree turns of <see cref="TurnRadius"/> m radius — expressed as
    /// arc-length-parameterised poses (position on y = 0, rotation looking along the
    /// driving direction).
    ///
    /// Same reasoning as <see cref="LapCounter"/>: plain C#, no MonoBehaviour, nothing
    /// written into a scene. The shape can then be asked questions in EditMode ("is it
    /// closed?", "is it as long as advertised?", "are the checkpoints spread around
    /// it?") while <see cref="TrackBuilder"/> turns exactly these numbers into
    /// primitives instead of re-deriving the arithmetic somewhere else.
    ///
    /// Plan view (x across the track, z along it):
    ///   the start straight lies at x = +TurnRadius between z = -100 and z = +100;
    ///   turn 1 caps it at z = +100 around the centre (0, +100);
    ///   the back straight lies at x = -TurnRadius;
    ///   turn 2 caps it at z = -100 around the centre (0, -100).
    ///
    /// The car drives counter-clockwise as seen from above, so the interior of the
    /// circuit stays on the driver's left. Arc length 0 is the start/finish line at
    /// the middle of the start straight, (60, 0, 0), heading +Z; from there the lap
    /// runs 100 m of straight, 188.5 m of turn, 200 m of straight, 188.5 m of turn and
    /// the last 100 m of straight back onto the line, 777 m in all.
    /// </summary>
    public static class TrackLayout
    {
        /// <summary>Length of each of the two straights, which run parallel to Z.</summary>
        public const float StraightLength = 200f;

        /// <summary>Radius of each of the two 180 degree turns.</summary>
        public const float TurnRadius = 60f;

        /// <summary>Drivable width of the road, the centreline being its middle.</summary>
        public const float RoadWidth = 12f;

        /// <summary>
        /// Requested centreline sample pitch. The realised pitch is <see cref="Length"/>
        /// divided by <see cref="SampleCount"/>, so the samples land on a closed loop
        /// instead of leaving a one-step gap at the line.
        /// </summary>
        public const float SampleSpacing = 2f;

        /// <summary>How far the grid slot sits behind the finish line, measured along the track.</summary>
        public const float SpawnDistanceBeforeLine = 6f;

        /// <summary>Height of the spawn pose: a chassis centre clearing the y = 0 road surface.</summary>
        public const float SpawnHeight = 0.6f;

        /// <summary>Checkpoint count the builder and a <see cref="LapCounter"/> agree on.</summary>
        public const int DefaultCheckpointCount = 4;

        /// <summary>Half a straight: distance from the finish line to the beginning of a turn.</summary>
        private const float HalfStraight = StraightLength * 0.5f;

        /// <summary>Arc length of one 180 degree turn (half a circle).</summary>
        public static float TurnArcLength => Mathf.PI * TurnRadius;

        /// <summary>Perimeter of the centreline: two straights plus one full circle of turning.</summary>
        public static float Length => 2f * StraightLength + 2f * TurnArcLength;

        /// <summary>Number of equal steps the closed centreline is sampled into (always at least 8).</summary>
        public static int SampleCount()
        {
            return Mathf.Max(8, Mathf.RoundToInt(Length / SampleSpacing));
        }

        /// <summary>Distance between neighbouring centreline samples.</summary>
        public static float RealisedSampleSpacing()
        {
            return Length / SampleCount();
        }

        /// <summary>
        /// The centreline as a closed list of poses at roughly
        /// <see cref="SampleSpacing"/> metres: entry 0 is the finish line and the final
        /// entry repeats the first, so summing consecutive distances measures the whole
        /// perimeter and the road ribbon built from it has no seam.
        /// </summary>
        public static List<Pose> Centerline()
        {
            int segments = SampleCount();
            float delta = Length / segments;

            var poses = new List<Pose>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                poses.Add(PoseAt(i * delta));
            }

            return poses;
        }

        /// <summary>
        /// Pose at <paramref name="distance"/> metres of arc length from the finish
        /// line, wrapped in the driving direction — distances are not required to sit
        /// inside one lap, anything is folded back onto the loop.
        /// </summary>
        public static Pose PoseAt(float distance)
        {
            float perimeter = Length;
            float s = distance % perimeter;
            if (s < 0f)
            {
                s += perimeter;
            }

            float arc = TurnArcLength;

            // Exit half of the start straight: x = +R driving +Z, z from 0 up to +100.
            if (s <= HalfStraight)
            {
                return StraightPose(TurnRadius, s, Vector3.forward);
            }

            s -= HalfStraight;

            // Turn 1: centre (0, +100), sweeping counter-clockwise from theta 0 to pi,
            // carrying the car from (+R, +100) around to (-R, +100).
            if (s <= arc)
            {
                return TurnPose(new Vector2(0f, HalfStraight), s / TurnRadius);
            }

            s -= arc;

            // Back straight: x = -R driving -Z, z from +100 down to -100.
            if (s <= StraightLength)
            {
                return StraightPose(-TurnRadius, HalfStraight - s, Vector3.back);
            }

            s -= StraightLength;

            // Turn 2: centre (0, -100), theta pi to 2pi, from (-R, -100) back to (+R, -100).
            if (s <= arc)
            {
                return TurnPose(new Vector2(0f, -HalfStraight), Mathf.PI + s / TurnRadius);
            }

            s -= arc;

            // Entry half of the start straight: x = +R, z from -100 back up to the line.
            return StraightPose(TurnRadius, -HalfStraight + s, Vector3.forward);
        }

        /// <summary>
        /// Grid slot: on the start straight, <see cref="SpawnDistanceBeforeLine"/> metres
        /// short of the finish line, facing the driving direction, at
        /// <see cref="SpawnHeight"/>. Cross the line from here and the very first
        /// checkpoint index is the expected one.
        /// </summary>
        public static Pose SpawnPose()
        {
            return new Pose(
                new Vector3(TurnRadius, SpawnHeight, -SpawnDistanceBeforeLine),
                Quaternion.LookRotation(Vector3.forward, Vector3.up));
        }

        /// <summary>
        /// <paramref name="count"/> checkpoint poses spread evenly by distance around
        /// the lap, index 0 being the finish line at the middle of the start straight —
        /// the index a <see cref="LapCounter"/> waits on to close a lap.
        /// </summary>
        /// <param name="count">Checkpoints to place; at least one (the line alone).</param>
        public static Pose[] CheckpointPoses(int count = DefaultCheckpointCount)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count,
                    "A track needs at least one checkpoint (the start/finish line, index 0).");
            }

            var poses = new Pose[count];
            if (count == 1)
            {
                poses[0] = PoseAt(0f);
                return poses;
            }

            float delta = Length / count;
            for (int i = 0; i < count; i++)
            {
                poses[i] = PoseAt(i * delta);
            }

            return poses;
        }

        // --------------------------------------------------------------- pieces

        private static Pose StraightPose(float x, float z, Vector3 heading)
        {
            return new Pose(new Vector3(x, 0f, z), Quaternion.LookRotation(heading, Vector3.up));
        }

        /// <summary>
        /// Point on a turn of <see cref="TurnRadius"/> around <paramref name="center"/>
        /// (an x/z pair) at angle <paramref name="theta"/>, measured counter-clockwise
        /// from +x inside the x/z plane — which is also the tangent, hence the heading.
        /// </summary>
        private static Pose TurnPose(Vector2 center, float theta)
        {
            var position = new Vector3(
                center.x + TurnRadius * Mathf.Cos(theta),
                0f,
                center.y + TurnRadius * Mathf.Sin(theta));

            var heading = new Vector3(-Mathf.Sin(theta), 0f, Mathf.Cos(theta));

            return new Pose(position, Quaternion.LookRotation(heading, Vector3.up));
        }




    }
}
