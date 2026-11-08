using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// How far round the circuit one car has come, in metres, as a continuous along-track odometer:
    /// the figure the field is ranked by, and the player's own standing measured by the same arithmetic
    /// so the two can be put in order at all.
    ///
    /// <b>Why it exists.</b> The first version of this number was <c>laps done × the perimeter + the
    /// distance round the lap in progress</c>, read off a <see cref="LapCounter"/>. Two references were
    /// mixed in that sum: the laps only start being counted when the line is first crossed, and the
    /// distance round the lap folds back to nought the moment it is crossed again, so the total jumped
    /// by a whole lap at whichever of those two moments came first, and where the car was sitting
    /// relative to the line decided which of them that was. A car that had driven 818 m reported 1581 m
    /// of progress. Progress is bounded by the ground a car has covered — it cannot come round the
    /// circuit further than it has travelled — so a figure that can gain 777 m in no time at all is
    /// wrong however the laps happen to be counted, and the laps are not counted here at all.
    ///
    /// <b>What it does instead.</b> It adds the movement up. Every <see cref="Update"/> asks
    /// <see cref="AiDriver.TrackDistanceAt"/> where round the lap the position it is handed is, and folds
    /// the <em>difference</em> against the last answer into the value rather than the answer itself. A
    /// difference is wrap-aware: coming back over the line reads as -0.3 m and crossing it as +0.3 m,
    /// never as a lap, which is the whole trick. The value is therefore continuous, runs backwards when
    /// the car is reversed, and is the metres the car has come — so the one-lap test of it in EditMode is
    /// that a lap driven adds the perimeter and nothing else.
    ///
    /// <b>Starts where the car starts.</b> <see cref="Reset"/> zeroes the value at the pose it is given,
    /// folding it into a <em>signed</em> distance from the line: the grid sits behind the line, so a car
    /// six metres short of it starts at -6 rather than at 771, and the field is in the right order before
    /// anybody has moved. Nobody is at zero, and that is the point — the number is a difference, and only
    /// differences mean anything.
    ///
    /// <b>Carried is not driven.</b> A body that is picked up and put down has covered no road: R, the
    /// session's automatic recovery, and an <see cref="AiDriver"/> putting itself back on the centreline
    /// all move a car without driving it. Any one call's move longer than <see cref="MaxStepM"/> is
    /// therefore worth nought and only re-bases the reading, so a reset can neither add progress nor —
    /// through <see cref="Rebase"/> — take it away. Where a reset is known about, the caller says so with
    /// <see cref="Rebase"/> and the value is left exactly where it was; the clamp is the backstop for the
    /// ones that are not.
    ///
    /// Deliberately a plain C# class with no MonoBehaviour, for the reason <see cref="LapCounter"/> is:
    /// the positions are pushed in, so a lap of this can be driven and asserted in EditMode without a
    /// scene, a car or a physics step, and whatever feeds it (a fixed step, a replay scrubber) stays
    /// somebody else's problem.
    /// </summary>
    public sealed class TrackProgress
    {
        /// <summary>
        /// Largest one-call move that is still a car driving, m. Several physics steps of the fastest lap
        /// this field does — 25 m/s at the default 50 Hz is half a metre a step, and a step of scaled time
        /// is the same however fast the clock is played — and less than the shortest gap a car can be
        /// carried along by any reset this project has. Anything longer is a body being moved rather than
        /// a car driving, and adds nothing.
        /// </summary>
        public const float MaxStepM = 15f;

        /// <summary>Distance round the lap of the last position read, m, wrapped into one lap of it.</summary>
        private float _last;

        /// <summary>
        /// True once a position has been read. Without it a freshly built odometer would take the first
        /// position it is handed as a step away from distance nought — a car standing at the back straight
        /// would be credited with 388 m before it had moved.
        /// </summary>
        private bool _located;

        /// <summary>
        /// How far round the circuit the car has come, in metres, measured from the pose it was last
        /// <see cref="Reset"/> at — negative while it is behind the start line, one perimeter more than
        /// that once it has been round it. Not tied to any lap counter: a lap counted is a rule about
        /// gates, this is a measurement of road.
        /// </summary>
        public float Value { get; private set; }

        /// <summary>
        /// What <see cref="Value"/> read at the pose it was last <see cref="Reset"/> at, which is the
        /// signed distance of that pose from the start line: negative for a car on the grid, since the
        /// grid is behind the line.
        /// </summary>
        public float StartOffset { get; private set; }

        /// <summary>
        /// Start the count again from the pose the car is at now: the value becomes that pose's signed
        /// distance from the line and the baseline becomes its distance round the lap. Call this when the
        /// car appears (or when a race is starting over), not when it is merely moved — a car put back on
        /// the track by a recovery is still the car that came that far, and that is what
        /// <see cref="Rebase"/> is for.
        /// </summary>
        /// <param name="position">Where the car is, in world space.</param>
        public void Reset(Vector3 position)
        {
            float distance = AiDriver.TrackDistanceAt(position);
            if (!IsFinite(distance))
            {
                return; // Nothing worth starting from; keep whatever was already known.
            }

            _last = distance;
            _located = true;
            StartOffset = SignedOffset(distance);
            Value = StartOffset;
        }

        /// <summary>
        /// Read the car again and add what it has come since the last reading.
        ///
        /// The difference is folded into the lap rather than the raw distance taken for it, which is what
        /// makes the line a metre rather than a lap: distance round the lap reads 776.8 and then 0.1, and
        /// the move between them is -0.2, not -776.7. A move longer than <see cref="MaxStepM"/> either way
        /// is a body being carried rather than a car driving, and is worth nought — though the baseline
        /// still follows the car, so the metres it was moved are not handed back to it on the next reading.
        /// </summary>
        /// <param name="position">Where the car is now, in world space.</param>
        public void Update(Vector3 position)
        {
            float distance = AiDriver.TrackDistanceAt(position);
            if (!IsFinite(distance))
            {
                return; // A body that has gone non-finite is being recovered, not timed.
            }

            if (!_located)
            {
                Reset(position); // The first reading is the baseline, not a step.
                return;
            }

            float perimeter = TrackLayout.Length;
            float half = perimeter * 0.5f;

            float delta = distance - _last;
            if (delta > half)
            {
                delta -= perimeter; // Back over the line the long way: a step, not a lap.
            }
            else if (delta < -half)
            {
                delta += perimeter; // Over the line: a step, not a lap the other way.
            }

            if (delta > MaxStepM || delta < -MaxStepM)
            {
                delta = 0f; // A teleport. A body has been moved; nobody has driven anywhere.
            }

            _last = distance;
            Value += delta;
        }

        /// <summary>
        /// Move the baseline to <paramref name="position"/> and leave the value exactly where it is. This
        /// is what a reset asks for: the car has been carried, so the metres between where it was and
        /// where it has been put are neither gained nor lost, and the next reading has to be measured from
        /// the pose it was dropped in rather than from the one it was picked out of.
        /// </summary>
        /// <param name="position">Where the car has been put, in world space.</param>
        public void Rebase(Vector3 position)
        {
            float distance = AiDriver.TrackDistanceAt(position);
            if (!IsFinite(distance))
            {
                return;
            }

            if (!_located)
            {
                Reset(position); // Nothing counted yet, so there is nothing to leave alone.
                return;
            }

            _last = distance;
        }

        /// <summary>
        /// A distance round the lap folded into a signed distance from the start line: the back half of
        /// the lap reads as the negative distance still to come to the line, so a car standing on the grid
        /// six metres short of it starts at -6 rather than at 771. Exactly half a lap is left positive,
        /// where either reading is as honest as the other.
        /// </summary>
        private static float SignedOffset(float distance)
        {
            float perimeter = TrackLayout.Length;
            return distance > perimeter * 0.5f ? distance - perimeter : distance;
        }

        /// <summary>
        /// Whether a measurement is a number at all. A body that has gone NaN hands a NaN back from the
        /// centreline search, and one NaN added to the value would poison every reading after it.
        /// </summary>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}


