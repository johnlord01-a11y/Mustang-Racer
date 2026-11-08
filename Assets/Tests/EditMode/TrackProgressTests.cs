using NUnit.Framework;
using OpenWheelRacer.Race;
using UnityEngine;

namespace OpenWheelRacer.Tests.EditMode
{
    /// <summary>
    /// The one number a race is ranked by, driven in a straight line through the arithmetic that keeps it.
    ///
    /// <see cref="TrackProgress"/> replaced a figure built as <c>laps done × the perimeter + the way round
    /// the lap in progress</c>, and the reason it had to is the first test here: that sum mixed two
    /// references, the laps being counted only from the first crossing of the line and the way round the
    /// lap folding back to nought on every crossing after it, so it jumped by a whole lap at whichever of
    /// the two moments came first. A car reported as having come 1581 m had driven 818. Progress is bounded
    /// by the ground a car has covered, full stop — so what these tests hold it to is exactly that: a lap
    /// of road adds one perimeter and nothing else, however many times the line is crossed, and a car that
    /// has come 818 m is said to have come 818 m.
    ///
    /// The rest is the same claim from the awkward directions. The line, where the reading that wraps has
    /// to be folded into a step rather than a lap. A car stood still, which must not be credited with the
    /// metres from the line to where it happens to be. Being driven backwards, and being picked up and put
    /// down again — the last of which is what the R key, the session's automatic recovery and an opponent
    /// returned to the centreline all do, and none of them is progress in either direction.
    ///
    /// No scene, no car and no physics step: the class is plain C# and the positions are pushed in, so a
    /// lap is one hundred and five hundred calls to <see cref="TrackProgress.Update"/> along
    /// <see cref="TrackLayout.PoseAt"/>. Positions, not distances, because the measurement from one to the
    /// nearest point on the centreline is the thing being tested — reading the lap distance off
    /// <see cref="TrackLayout"/> directly would only have proved that the centreline is a loop, which it is.
    ///
    /// Figures are asserted to <see cref="ReadingM"/>, about a centreline sample pitch, because that is how
    /// good a nearest-point-on-a-polyline reading is; the accumulation of the steps themselves is exact to
    /// <see cref="ExactM"/>.
    /// </summary>
    [TestFixture]
    public sealed class TrackProgressTests
    {
        // ---- what a run is ---------------------------------------------------------------------

        /// <summary>
        /// Steps in the synthetic runs, m. Comfortably under the centreline sample pitch, so a car is
        /// never asked to account for more road in one reading than <see cref="TrackProgress.MaxStepM"/>
        /// lets through, which is what makes the exact figure below exact.
        /// </summary>
        private const float StepM = 0.5f;

        /// <summary>
        /// Metres the odometer is allowed to be out by on an exact figure — the perimeter of one lap, say.
        /// The metres are added up a step at a time in single precision, so the drift is the rounding of
        /// fifteen hundred half-metre additions of a number in the hundreds, nowhere near the lap the bug
        /// this fixture keeps out used to add.
        /// </summary>
        private const float ExactM = 0.25f;

        /// <summary>
        /// Metres one reading of where a car is round the lap is allowed to be out by, and therefore two
        /// readings of one car against each other. A place on the circuit is measured by finding the
        /// nearest point on the centreline along samples that stand
        /// <see cref="TrackLayout.SampleSpacing"/> apart, so the figure is good to about a sample pitch
        /// however exact the arithmetic that produced it is.
        /// </summary>
        private const float ReadingM = TrackLayout.SampleSpacing + 0.5f;

        /// <summary>
        /// Where the race starts: the grid slot, <see cref="TrackLayout.SpawnDistanceBeforeLine"/> metres
        /// short of the finish line, in its own lane and at its own height — the pose
        /// <see cref="RaceBootstrap"/> stands a car on and <see cref="RaceSession"/> starts its odometer at.
        /// </summary>
        private static Vector3 Grid
        {
            get { return TrackLayout.SpawnPose().position; }
        }

        /// <summary>
        /// The point on the centreline <paramref name="distance"/> metres round the lap: a distance is
        /// allowed to run past the end of the lap or start before the beginning of it, which is what a
        /// test that wants to stand a car three metres from the line needs.
        /// </summary>
        private static Vector3 At(float distance)
        {
            return TrackLayout.PoseAt(distance).position;
        }

        // ---- one lap of road is one lap of progress ------------------------------------------

        /// <summary>
        /// The bug, and the whole reason for the class: drive from the grid to a little past the finish
        /// line — 818 m of road, the reported case — and the reading must be 818 m of progress, not the
        /// 1581 m that a lap counted on the way made of it. One crossing of the line in the middle of the
        /// run, which is exactly where the old figure found a lap that had not been driven.
        /// </summary>
        [Test]
        public void Drive_EightHundredMetresOfRoad_AccumulatesTheMetresDriven()
        {
            var progress = new TrackProgress();
            progress.Reset(Grid);
            float from = progress.StartOffset;

            const float driven = 818f;
            for (float s = StepM; s <= driven; s += StepM)
            {
                progress.Update(At(from + s));
            }

            Debug.Log($"[Progress] drove {driven:F0} m from the grid, reported " +
                      $"{progress.Value - from:F1} m (was 1581 m for 818 m driven).");

            // Measured from the grid itself, which is what a place in a field is, and bounded by the road
            // — the property that was broken, and the one no reading of a car may exceed.
            Assert.GreaterOrEqual(progress.Value - from, driven - ExactM);
            Assert.LessOrEqual(progress.Value - from, driven + ExactM);
        }

        /// <summary>
        /// A whole lap driven is a perimeter of progress — 777 m, and not 777 twice. The line is crossed
        /// at the end of it, which is where the old sum found two laps in one.
        /// </summary>
        [Test]
        public void Drive_OneFullLap_AccumulatesExactlyOnePerimeter()
        {
            var progress = new TrackProgress();
            progress.Reset(Grid);
            float from = progress.StartOffset;

            for (float s = StepM; s <= TrackLayout.Length; s += StepM)
            {
                progress.Update(At(from + s));
            }

            // The perimeter is not a whole number of steps, so the loop stops 0.49 m short of the line;
            // this last reading drives the car exactly one perimeter, which is the figure asserted below.
            progress.Update(At(from + TrackLayout.Length));

            Debug.Log($"[Progress] one lap of road added {progress.Value - from:F2} m against the " +
                      $"{TrackLayout.Length:F1} m perimeter.");

            Assert.AreEqual(TrackLayout.Length, progress.Value - from, ExactM);
        }

        /// <summary>
        /// Five laps, because a fault that appears once a lap is worth nothing if it is only ever driven
        /// twice: progress keeps count of the laps it has been round by being that many perimeters large,
        /// and by nothing else.
        /// </summary>
        [Test]
        public void Drive_FiveLaps_AccumulatesFivePerimeters()
        {
            var progress = new TrackProgress();
            progress.Reset(Grid);
            float from = progress.StartOffset;

            const int laps = 5;
            for (float s = StepM; s <= laps * TrackLayout.Length; s += StepM)
            {
                progress.Update(At(from + s));
            }

            // Same whole-perimeter tail as the single lap: five perimeters are not a whole number of steps.
            progress.Update(At(from + laps * TrackLayout.Length));

            Assert.AreEqual(laps * TrackLayout.Length, progress.Value - from, ExactM * laps);
        }

        // ---- behind the line, and the crossing of it ---------------------------------------------

        /// <summary>
        /// How far a reading of where a car standing <em>on</em> the centreline is allowed to be out, m. The
        /// poses in these tests come straight out of <see cref="TrackLayout.PoseAt"/>, so they are standing
        /// on the very line they are measured against and this is the rounding of the arithmetic rather than
        /// the width of a search: <see cref="ReadingM"/> is the tolerance for a car that is off the line and
        /// has to be projected back onto it, which is the looser claim.
        /// </summary>
        private const float StandingM = 1f;

        /// <summary>
        /// The step the line is walked at, m — one centreline sample pitch, and the road the reading is
        /// asked to account for between one call and the next.
        /// </summary>
        private const float LineWalkM = TrackLayout.SampleSpacing;

        /// <summary>
        /// Largest change in the value that one step of <see cref="LineWalkM"/> may make, m. Two metres of
        /// road have to read as two metres, and the one place they are at risk of not is the finish line,
        /// where the distance round the lap comes back from 777 to nought and has to be folded into a step
        /// of nought point three rather than taken for a lap of 777. That fold is the whole bug in one
        /// figure, and this is the figure it is measured against.
        /// </summary>
        private const float LineStepM = 2.1f;

        /// <summary>
        /// The grid is behind the finish line and the reading says so: a car stood eight, sixteen or
        /// twenty-four metres short of the line has come minus that much, which is what puts the field in
        /// the right order before anybody has moved — where the distance round the lap instead would have
        /// reported the same three cars at 769, 761 and 753 m and left a marshal sorting the grid by hand.
        /// Walking forward over the line then has to be a walk: two metres at a time, two metres of
        /// reading, and no one step anywhere near the perimeter the wrap has to be worth if the line is to
        /// stay a line and not a lap.
        /// </summary>
        [Test]
        public void GridBehindLine_StartsNegative_AndCrossingLineIsContinuous()
        {
            float[] behind = { 8f, 16f, 24f };

            for (int i = 0; i < behind.Length; i++)
            {
                float back = behind[i];
                var progress = new TrackProgress();
                progress.Reset(At(TrackLayout.Length - back));

                Assert.AreEqual(-back, progress.StartOffset, StandingM,
                    $"a car stood {back:F0} m short of the line read {progress.StartOffset:F1} m to start " +
                    "with, so a grid slot behind the line cannot be told from one standing in front of it");
                Assert.AreEqual(progress.StartOffset, progress.Value, 0f,
                    "the reading starts at its own start: a car that has not moved has come nought metres");
                Assert.Less(progress.Value, 0f,
                    $"{back:F0} m short of the line has to read as behind it, and read {progress.Value:F1} m");

                float last = progress.Value;
                float worst = 0f;
                for (float s = LineWalkM; s <= back + 2f * LineWalkM; s += LineWalkM)
                {
                    progress.Update(At(TrackLayout.Length - back + s));

                    float jump = Mathf.Abs(progress.Value - last);
                    if (jump > worst) worst = jump;
                    Assert.LessOrEqual(jump, LineStepM,
                        $"stepping {LineWalkM:F0} m forward at {TrackLayout.Length - back + s:F0} m round " +
                        $"the lap moved the reading {jump:F1} m — the line has been counted as a lap rather " +
                        "than crossed");

                    last = progress.Value;
                }

                Assert.Greater(progress.Value, 0f,
                    $"having driven {back + 2f * LineWalkM:F0} m from {back:F0} m short of the line the car " +
                    $"is through it, and still reads {progress.Value:F1} m");

                Debug.Log($"[Progress] {back:F0} m behind the line started at {progress.StartOffset:F1} m, " +
                          $"walked through it in {LineWalkM:F0} m steps with nothing worse than {worst:F2} m " +
                          $"in one of them, and came out at {progress.Value:F1} m.");
            }
        }

        /// <summary>
        /// Two hundred metres of circuit in one call is not two hundred metres driven. A body is picked up
        /// and put down by R, by the session's automatic recovery and by an <see cref="AiDriver"/> putting
        /// itself back on the centreline, and progress is bounded by the ground a car has covered — so a
        /// move longer than <see cref="TrackProgress.MaxStepM"/> is worth nought, and worth nought in both
        /// directions: the baseline follows the body, so the metres it was carried are neither given to it
        /// nor asked back on the next reading. What it drives after being put down is its own again.
        /// </summary>
        [Test]
        public void Teleport_DoesNotManufactureProgress()
        {
            var progress = new TrackProgress();
            progress.Reset(Grid);
            float from = progress.StartOffset;

            for (float s = StepM; s <= 10f; s += StepM)
            {
                progress.Update(At(from + s));
            }

            float before = progress.Value;
            progress.Update(At(from + 210f)); // two hundred metres of road in one call

            Assert.AreEqual(before, progress.Value, ExactM,
                $"carried 200 m down the road and the reading went {progress.Value - before:F2} m, so a body " +
                "being moved was worth road when it is worth nothing at all");

            Assert.AreEqual(10f, progress.Value - from, ReadingM,
                "the whole of the reading after being carried is the ten metres that were driven, which is " +
                "the bound on road progress may never be found over");

            progress.Update(At(from + 212f));
            Assert.AreEqual(2f, progress.Value - before, ExactM,
                "the metres after being put down are its own, measured from where it was dropped rather than " +
                "from where it was picked up");

            Debug.Log($"[Progress] carried 200 m and reported {progress.Value - before - 2f:F2} m of it; " +
                      $"{progress.Value - from:F1} m claimed against the 10 m driven.");
        }

        /// <summary>
        /// Where a reset is known about the caller says so, and the reading keeps exactly the value it had:
        /// the metres between the barrier and the centreline were neither driven nor lost, so a car put back
        /// is neither rewarded for the crash nor fined for it. The baseline goes with the body, and what it
        /// drives from the pose it was dropped in is all its own from then on — the carried metres do not
        /// come back as a debt that has somehow to be driven off.
        /// </summary>
        [Test]
        public void Rebase_AfterReset_KeepsValue()
        {
            var progress = new TrackProgress();
            progress.Reset(Grid);
            float from = progress.StartOffset;

            for (float s = StepM; s <= 20f; s += StepM)
            {
                progress.Update(At(from + s));
            }

            float before = progress.Value;
            progress.Rebase(At(from + 60f)); // picked up and put down 40 m along

            // Nothing at all is written to the value by a rebase, so this is equality rather than a
            // tolerance: the car keeps the place it had come to to the last centimetre.
            Assert.AreEqual(before, progress.Value, 0f,
                $"a rebase left the reading at {progress.Value:F1} m when it was handed {before:F1} m, so " +
                "being put back on the road took a place in the field away");

            progress.Update(At(from + 62f));
            Assert.AreEqual(2f, progress.Value - before, ExactM,
                "the first two metres after being put down have to be its own, from the pose it was dropped " +
                "in and not from the one it was picked out of");

            for (float s = 4f; s <= 20f; s += StepM)
            {
                progress.Update(At(from + 60f + s));
            }

            Assert.AreEqual(20f, progress.Value - before, ExactM,
                $"it came {progress.Value - before:F1} m from the pose it was put down at for 20 m of road, " +
                "so the metres it was carried came back as a debt it now had to drive off");

            Debug.Log($"[Progress] rebased 40 m along after {before - from:F0} m driven and stayed at " +
                      $"{before:F1} m, then added {progress.Value - before:F1} m for the 20 m driven from " +
                      "where it was put down.");
        }

        /// <summary>
        /// Reverse and the reading goes backwards, because it is a measurement of road rather than a tally
        /// of gates: the same twenty metres driven the other way take twenty metres off. A figure that could
        /// only ever go up would credit a car that had wound itself backwards out of a barrier with the
        /// metres it had just un-done, and rank it ahead of the car it had reversed away from — which is the
        /// same fault as a lap appearing out of nowhere, spent in the other direction.
        /// </summary>
        [Test]
        public void Reverse_DecreasesProgress()
        {
            var progress = new TrackProgress();
            progress.Reset(Grid);
            float from = progress.StartOffset;

            // Forwards first, so that the reverse is road given back rather than the grid slot coming off.
            for (float s = StepM; s <= 30f; s += StepM)
            {
                progress.Update(At(from + s));
            }

            float ahead = progress.Value;
            for (float s = StepM; s <= 20f; s += StepM)
            {
                progress.Update(At(from + 30f - s));
            }

            Debug.Log($"[Progress] came about and drove 20 m backwards: {ahead - progress.Value:F1} m off a " +
                      $"reading of {ahead:F1} m, from a grid at {from:F1} m.");

            Assert.GreaterOrEqual(ahead - progress.Value, 20f - ExactM,
                $"20 m backwards took only {ahead - progress.Value:F1} m off the reading, so going the wrong " +
                "way the right distance was worth nothing");
            Assert.LessOrEqual(ahead - progress.Value, 20f + ExactM,
                $"20 m backwards took {ahead - progress.Value:F1} m off the reading, which is more road than " +
                "was given back");
            Assert.Greater(progress.Value, from,
                "twenty metres given back from thirty along is not the grid slot coming back off: the car " +
                "still stands ahead of where it was reset");
        }
    }
}

