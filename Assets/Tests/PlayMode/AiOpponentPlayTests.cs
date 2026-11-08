using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenWheelRacer;
using OpenWheelRacer.Race;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// <summary>
    /// The first AI milestone (PLAN.md Stage 6): a field that races. Five things are claimed about the
    /// opponents and each is run end to end in real play — that the boot puts them on the grid without
    /// putting two cars in the same place and without taking the camera off the player; that they drive
    /// a whole lap of the built circuit forwards, at speed, with both turns between them and the line
    /// and never under the road; that the laps they cross stay theirs and never appear in the player's
    /// <see cref="LapCounter"/>; that they drive without a keyboard while the player's own car sits
    /// parked on its controls; and that one of them nose-deep in a barrier comes back out of it by
    /// itself and goes on racing.
    ///
    /// <b>How the race is put together.</b> The boot test loads the real <c>Track_Proto</c> scene and
    /// lets <see cref="RaceBootstrap"/> assemble it, because that is the thing under test there. The
    /// four that have to watch several seconds of racing build the same race by hand the way
    /// <see cref="RaceBootstrapPlayTests"/> does — <see cref="TrackBuilder"/>, one human-driven car on
    /// the grid, a <see cref="RaceSession"/> over it, then <see cref="AiField"/> behind it — because a
    /// test that is timing cars needs to own the clock, the parked player and the count of cars on the
    /// circuit (the stuck-in-a-wall test wants exactly one opponent, so that nothing else on the road
    /// can touch the player it is also watching).
    ///
    /// <b>The clock.</b> A lap of this circuit is forty-some seconds of simulation, and four of these
    /// tests need one, so the runs are played at <see cref="SimSpeed"/>. Every window is then counted
    /// in <em>simulated</em> seconds — <see cref="Time.time"/> and <see cref="Time.fixedDeltaTime"/>
    /// steps, never wall clock — which is what lets the same 90 s of simulation be a fair ceiling on a
    /// lap whether the machine is rendering at a hundred frames a second or struggling at ten.
    /// <see cref="Time.timeScale"/> goes back to 1 in the <c>TearDown</c> and the pause is cleared both
    /// ways, since both are statics that would otherwise be inherited by the next fixture in the run.
    ///
    /// <b>No devices.</b> Nothing here reads the Input System, as in the other fixtures: the player's
    /// car is held through <see cref="VehicleController.SetTestControls"/> — on the handbrake rather
    /// than the foot brake, because under <see cref="VehicleTuning.ReverseEngageSpeedMs"/> the brake
    /// key <i>is</i> the reverse and a "parked" car held on it would spend the test driving backwards
    /// down the straight. That is also the honest form of the independence claim: the opponents are not
    /// given the keyboard to ignore, they are never near one, and what is asserted is that they race
    /// with the one car that <i>is</i> wired to it standing still.
    ///
    /// Key numbers (laps, top speed, distance round, time taken, recoveries, resets) are logged with an
    /// <c>[AI]</c> prefix as each run finishes, so a run of this fixture reads as a race report and a
    /// failure says what the field actually did rather than only that it did not do what was asked.
    /// </summary>
    [TestFixture]
    public sealed class AiOpponentPlayTests
    {
        // ---- the stage -------------------------------------------------------------------

        /// <summary>
        /// Clock the long runs are played at. Nothing about the simulation changes — a physics step is
        /// still one <see cref="Time.fixedDeltaTime"/> of it — only how many of them a frame carries.
        /// </summary>
        private const float SimSpeed = 3f;

        /// <summary>Opponents the fixture that races asks the field for.</summary>
        private const int OpponentCount = 3;

        /// <summary>Frames given to the field's first <see cref="AiDriver.Update"/> before it is timed.</summary>
        private const int FirstSampleFrames = 2;

        /// <summary>
        /// Simulation a lap may take before the run is given up on. A lap is 777 m at the opponent's
        /// ~22 m/s average, so a clean field is round it in the forties; the rest is the margin for a
        /// car that had to reverse out of something, and for a slow frame or two at the start.
        /// </summary>
        private const float LapTimeoutSeconds = 90f;

        /// <summary>Laps a run is played out to before the field is judged.</summary>
        private const int LapsWanted = 1;

        /// <summary>Top speed an opponent has to reach during its lap, m/s, to have driven rather crawled.</summary>
        private const float MinimumTopSpeedMs = 15f;

        /// <summary>No opponent may go below this. The road lies at y = 0, so this is a car in a hole.</summary>
        private const float FloorY = -1f;

        /// <summary>How far the parked player may drift while the field goes round it, m.</summary>
        private const float PlayerStayToleranceM = 2f;

        /// <summary>
        /// How far the parked player may move in the one-opponent test, m. Nothing else is on the road
        /// to touch it, so this is a car being held by its own drivetrain and nothing else.
        /// </summary>
        private const float PlayerParkedToleranceM = 0.5f;

        /// <summary>
        /// Centre-to-centre distance under which two of these cars count as having touched, m: a chassis
        /// is 3.7 m long by 1.7 across, so a pair further apart than twice that diagonal is certainly
        /// not a contact, and a gate that a parked car has to stay put against is only honest once a
        /// lap's worth of traffic going past it at 25 m/s has been accounted for.
        /// </summary>
        private const float ContactDistanceM = 4f;

        /// <summary>Simulation the wall-bound opponent is given to reverse out and get back to speed.</summary>
        private const float StuckWindowSeconds = 20f;

        /// <summary>
        /// The progress the recovered opponent has to be holding at the end of the window for the run to be
        /// judged at all, m, and the figure the wait is abandoned on if it is not reached. The wall run is
        /// played out to the longer <see cref="MinimumRecoveredDistanceM"/>; this is the floor under it, below
        /// which there is no story to tell about the recovery.
        /// </summary>
        private const float MinimumResumedProgressM = 30f;

        /// <summary>
        /// Road an opponent has to have actually covered while the player sits parked, m — the shortest
        /// distance that says a car drove itself rather than rolled off a grid slot.
        /// </summary>
        private const float MinimumIndependentDistanceM = 25f;

        /// <summary>
        /// The least ground a counted lap has to have cost, m. A lap of the centreline is
        /// <see cref="TrackLayout.Length"/>, and the shortest way round that still sweeps every gate in
        /// order — hugging the inside of both 60 m turns — is about forty of those metres shorter, so
        /// this sits under that and over anything short of a lap.
        /// </summary>
        private static readonly float LapDistanceFloorM = 0.85f * TrackLayout.Length;

        /// <summary>
        /// One step of the odometer larger than this is not a car driving, it is a car being moved: a
        /// hand reset moves a body, and metres it was carried are not metres it travelled. Four times
        /// what the fastest car covers in one physics tick at the speed these runs are played at.
        /// </summary>
        private const float OdometerStepLimitM = 5f;

        /// <summary>
        /// Where round the lap the stuck opponent is put: the middle of the back straight, far from the
        /// start line so that thirty metres of progress is thirty metres and not a wrap of the lap.
        /// </summary>
        private const float StuckPlaceDistanceM = 350f;

        /// <summary>
        /// Lateral offset the stuck opponent is set down at, m off the centreline outwards. The nose of
        /// a chassis reaches about 1.85 m ahead of its centre and the wall's face stands
        /// <see cref="TrackBuilder.WallOffset"/> m out, so from here the car arrives at it under its own
        /// nudge rather than being placed inside it.
        /// </summary>
        private const float StuckPlaceLateralM = 4f;

        /// <summary>Speed it is given toward the wall, m/s — a nudge, so the nose arrives by itself.</summary>
        private const float StuckNudgeMs = 3f;

        /// <summary>
        /// The road the recovered opponent has to have come inside <see cref="StuckWindowSeconds"/> for this
        /// test to be satisfied, m. Twice the <see cref="MinimumResumedProgressM"/> figure the wait itself
        /// gives up on: thirty metres says the car came out of the wall, fifty says it pointed the thing back
        /// down the road and went, which is what the gates below are held to. It is also the figure the run is
        /// played out to, so that the metres of progress that are checked against the ground covered are a
        /// drive away from the barrier rather than the manoeuvre out of it.
        /// </summary>
        private const float MinimumRecoveredDistanceM = 50f;

        /// <summary>
        /// The speed the recovered opponent has to have been seen at during its run, m/s. Progress on its own
        /// can be made by being lucky; speed is made by driving, and this is the one that says the throttle
        /// was ever asked for.
        /// </summary>
        private const float MinimumRecoveredSpeedMs = 3f;

        /// <summary>
        /// How much further the recovered opponent has to end up from the wall it was nosed into than the
        /// closest it ever came to it, m. A car that comes out of a barrier and then sits alongside it has
        /// not come out of it; this is the distance the reverse and the drive away are worth.
        /// </summary>
        private const float AwayFromWallM = 3f;

        /// <summary>
        /// How far the parked player may move while one opponent digs itself out of a wall at the other end
        /// of the circuit, m. There is nothing else on the road in that test and nothing near the car at all,
        /// so this is a car held by its own handbrake on its own tarmac, and all it is allowed to spend is
        /// the millimetres a ride height has left in it once the springs have stopped.
        /// </summary>
        private const float ParkedHeldToleranceM = 0.05f;

        /// <summary>
        /// How far the parked player may move while a field goes one lap of it, m. The same claim with a lap
        /// of traffic passing close enough to be heard: still nought, but measured over forty seconds of it
        /// rather than twenty.
        /// </summary>
        private const float ParkedLapToleranceM = 0.1f;

        /// <summary>
        /// Simulation the parked car is given before it is watched. The grid pose is stood a couple of
        /// centimetres above the ride height the springs settle at, and a car watched from the frame it
        /// appeared in is a car caught on the way down; what is being claimed here is the millimetres left
        /// after the suspension has finished, so it is given the second or so that takes and watched from
        /// there.
        /// </summary>
        private const float ParkSettleSeconds = 1.5f;

        /// <summary>
        /// Metres an opponent's place on the circuit may run ahead of the ground its own body covered, m.
        /// Progress is bounded by road — nobody comes round further than they have driven — and this slack is
        /// the width of the two measurements rather than of the fault, which was 777 m of lap appearing in no
        /// time at all beside 818 m of driving.
        /// </summary>
        private const float ProgressOverGroundM = 2f;

        /// <summary>
        /// The tighter figure the wall run is held to, m. One car on the road, nothing to lean on and a
        /// window of twenty seconds in which to fetch itself out: the reading may run ahead of the ground by
        /// a centimetre or two of the difference between a body and a line and by nothing else.
        /// </summary>
        private const float RecoveredProgressOverGroundM = 1f;

        /// <summary>
        /// How much of the ground an opponent covered has to show in the distance it came: a place on the
        /// circuit that arrived on far less road than the field spent was not earned by driving round.
        /// </summary>
        private const float ProgressGroundRatio = 0.9f;

        /// <summary>
        /// Metres of credit over and above <see cref="LaneCreditM"/> for an opponent whose place on the
        /// circuit is allowed to run ahead of the ground its body covered. The lane is held and not railed to:
        /// a car wandering half a metre tighter through a turn than the lane it was given says it has come a
        /// little further round than it has, and that is honest driving rather than the fault this is looking
        /// for, which was 777 m of lap appearing beside 818 m of driving.
        /// </summary>
        private const float InsideLaneSlackM = 1f;

        /// <summary>
        /// How many radians an opponent turns through in one lap, and so how much a lane held off the
        /// centreline is worth in metres of difference between the road it covered and the centreline it is
        /// measured against. The circuit is two straights and two 180 degree turns
        /// (<see cref="TrackLayout.StraightLength"/>, <see cref="TrackLayout.TurnRadius"/>), so the whole of
        /// the difference is made in the turns and it is one full turn of the steering per lap.
        /// </summary>
        private const float TurnRadiansPerLap = 2f * Mathf.PI;

        /// <summary>
        /// The figure the one-step gate is floored at, m — half a metre, the road a car does at 25 m/s in one
        /// 50 Hz step, for the same reason <see cref="WallRecoveryPlayTests"/> holds a reverse to it. See
        /// <see cref="ContinuousStepLimitM"/> for why the field is measured against its own speed as well.
        /// </summary>
        private const float StepGateFloorM = 0.5f;

        /// <summary>
        /// Slack on the fastest step the field is allowed to make, so that a car a little over the speed its
        /// driver asked for is still driving rather than being carried.
        /// </summary>
        private const float StepGateScale = 1.1f;

        /// <summary>Frames given to a scene load plus the <see cref="RaceBootstrap"/> that rides on it.</summary>
        private const int SceneLoadFrames = 10;

        private GameObject _trackRoot;
        private GameObject _carObject;
        private GameObject _sessionRoot;
        private TrackBuilder _track;
        private VehicleController _player;
        private RaceSession _session;
        private readonly List<AiDriver> _opponents = new List<AiDriver>();

        /// <summary>Where the parked player has been pushed to, and how close anything came.</summary>
        private PlayerWatch _watch;

        /// <summary>Whether the last run ran its field out of time, and how long the run took.</summary>
        private bool _runTimedOut;
        private float _runSeconds;

        [SetUp]
        public void SetUp()
        {
            // Every window here is measured off the engine clock, and the pause is a static: a fixture
            // that left the clock stopped or the panel up would have these tests wait on seconds that
            // never arrive and report a timeout instead of the truth.
            GamePause.SetPaused(false);
            Time.timeScale = 1f;

            // These tests measure driving from the start, so they explicitly skip the race countdown.
            RaceSession.CountdownOverrideSeconds = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            RaceSession.CountdownOverrideSeconds = null;
            Time.timeScale = 1f;
            GamePause.SetPaused(false);

            // Deferred destruction as the other fixtures do it. The opponents are whole cars rather
            // than components on somebody else's body, and an AiDriver unsubscribes itself from the
            // gates and frees the material its body was painted with as it goes.
            for (int i = 0; i < _opponents.Count; i++)
            {
                AiDriver driver = _opponents[i];
                if (driver != null) Object.Destroy(driver.gameObject);
            }

            _opponents.Clear();

            if (_sessionRoot != null) Object.Destroy(_sessionRoot);
            if (_carObject != null) Object.Destroy(_carObject);
            if (_trackRoot != null) Object.Destroy(_trackRoot);

            _sessionRoot = null;
            _session = null;
            _carObject = null;
            _player = null;
            _trackRoot = null;
            _track = null;
            _watch = null;
        }

        // ---- the race, put together ------------------------------------------------------

        /// <summary>
        /// Circuit, one human-driven car on the grid, the session over it, and a field of
        /// <paramref name="opponentCount"/> opponents behind it — the same order
        /// <see cref="RaceBootstrap"/> uses, because the field is lined up behind wherever the player's
        /// car actually stands and the session has to exist before it can be told who it is racing.
        /// The clock goes to <see cref="SimSpeed"/> on the way out; the player's car is parked on its
        /// own handbrake before anything moves, so the only car being driven in any of these tests is
        /// one of the opponents.
        /// </summary>
        private void BuildRace(int opponentCount)
        {
            _trackRoot = new GameObject("AiOpponentTrack");
            _track = _trackRoot.AddComponent<TrackBuilder>();
            if (!_track.HasBuilt)
            {
                _track.Build();
            }

            Assert.IsTrue(_track.HasBuilt, "the harness needs the primitive circuit to race on");
            Assert.AreEqual(TrackLayout.DefaultCheckpointCount, _track.Checkpoints.Length,
                "the field's counters are sized to the track's gates, so the track has to have them");

            _carObject = VehicleController.BuildCar(null);
            _carObject.name = "Player_Car";

            Pose spawn = _track.Spawn;
            _carObject.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

            _player = _carObject.GetComponent<VehicleController>();
            Assert.IsNotNull(_player.PhysicsBody, "BuildCar must deliver an assembled physics body");
            _player.PhysicsBody.position = spawn.position;
            _player.PhysicsBody.rotation = spawn.rotation;
            _player.PhysicsBody.linearVelocity = Vector3.zero;
            _player.PhysicsBody.angularVelocity = Vector3.zero;
            _player.ArmSpawnPoint();

            ParkPlayer();

            _sessionRoot = new GameObject("AiOpponentSession");
            _session = _sessionRoot.AddComponent<RaceSession>();
            _session.Init(_track, _player);

            _opponents.AddRange(AiField.Spawn(_track, opponentCount));
            _session.SetOpponents(_opponents);

            Assert.AreEqual(opponentCount, _opponents.Count,
                "AiField handed back a smaller field than it was asked to build");

            Time.timeScale = SimSpeed;
        }

        /// <summary>
        /// Hold the player's car still with the handbrake, and with nothing else. The foot brake is
        /// deliberately not held: at or under <see cref="VehicleTuning.ReverseEngageSpeedMs"/> the brake
        /// key is the reverse, so a "braked" car at a standstill is a car on its way backwards down the
        /// straight, which is a different test to the one being run. Throttle at nil with the rear discs
        /// locked is a car that stays where the grid put it.
        /// </summary>
        private void ParkPlayer()
        {
            _player.SetTestControls(0f, 0f, 0f, handbrake: true);
        }

        /// <summary>
        /// Whole frames of simulation — never scaled seconds, so a slow render slows the report and not
        /// the verdict.
        /// </summary>
        private static IEnumerator WaitForFrames(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                yield return null;
            }
        }

        /// <summary>
        /// Hold for <paramref name="seconds"/> of simulation counted in fixed steps — the same clock the
        /// cars' physics runs on, so a window means the same thing on any machine. Used to give a parked car
        /// its suspension to settle on before the millimetres it is allowed to move in are measured: the grid
        /// pose is stood at <see cref="TrackLayout.SpawnHeight"/>, above the ride height the springs come to
        /// rest at, so a car watched from the frame it appeared in is a car caught on the way down.
        /// </summary>
        private static IEnumerator FixedSteps(float seconds)
        {
            for (int step = 0, steps = StepsFor(seconds); step < steps; step++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private static int StepsFor(float seconds) { return Mathf.Max(1, Mathf.CeilToInt(seconds / FixedDelta())); }

        private static float FixedDelta() { return Mathf.Max(0.005f, Time.fixedDeltaTime); }

        // ---- the run, and what it leaves behind -------------------------------------------

        /// <summary>
        /// What one opponent did between the two ends of a run. Two distances are kept.
        /// <see cref="StartProgress"/> and <see cref="ProgressGain"/> are the driver's own place on the
        /// circuit with laps folded in as metres, which is the figure that says how far round the lap it
        /// has come; <see cref="OdometerM"/> is ground added up tick by tick between the body's own
        /// positions, which is the figure that says how far it actually travelled. They are not the same
        /// measurement — the first takes a step of a whole lap the moment the line is crossed, because
        /// that is how <see cref="AiDriver.Progress"/> is built for ranking — and a lap is judged by the
        /// second.
        /// </summary>
        private sealed class DriverRun
        {
            public AiDriver Driver;
            public float StartProgress;
            public int StartLaps;
            public float TopSpeedMs;
            public float MinY = float.PositiveInfinity;
            public float OdometerM;
            public Vector3 LastPosition;
            public bool Seen;

            /// <summary>The physics-step record kept on the car itself, or null if none was armed.</summary>
            public AiOpponentStepProbe Probe;

            public float ProgressGain => Driver.Progress - StartProgress;
            public int LapsThisRun => Driver.Laps - StartLaps;

            /// <summary>One physics tick of the car's own motion, on the way to the odometer.</summary>
            public void Observe(Vector3 position)
            {
                float step = Vector3.Distance(LastPosition, position);

                if (Seen && step <= OdometerStepLimitM)
                {
                    OdometerM += step;
                }

                LastPosition = position;
                Seen = true;
            }
        }

        /// <summary>
        /// The parked player, watched: how far it has been moved from where the grid left it, and the
        /// closest any opponent came while the field went round it. The second figure is what makes the
        /// first honest — a car that is shunted by another has been moved by physics, not by the devices
        /// or the driver the test is claiming independence from.
        /// </summary>
        private sealed class PlayerWatch
        {
            public Vector3 ParkedAt;
            public float StartY;
            public float MaxDisplacementM;
            public float MaxHorizontalM;
            public float MaxVerticalM;
            public float ClosestApproachM = float.PositiveInfinity;

            public bool WasTouched => ClosestApproachM < ContactDistanceM;
        }

        /// <summary>
        /// Take the measuring baseline of every opponent from where it stands and the lap it has done
        /// right now. Has to be called after the drivers have been given a frame to locate themselves,
        /// or the first reading of <see cref="AiDriver.Progress"/> is the zero it holds before it has
        /// been round the centreline at all.
        /// </summary>
        private DriverRun[] StartRuns()
        {
            var runs = new DriverRun[_opponents.Count];
            for (int i = 0; i < _opponents.Count; i++)
            {
                AiDriver driver = _opponents[i];
                AiOpponentStepProbe probe = driver != null
                    ? driver.gameObject.AddComponent<AiOpponentStepProbe>()
                    : null;

                if (probe != null && driver.Car != null)
                {
                    probe.Arm(driver.Car.PhysicsBody, driver);
                }

                runs[i] = new DriverRun
                {
                    Driver = driver,
                    StartProgress = driver != null ? driver.Progress : 0f,
                    StartLaps = driver != null ? driver.Laps : 0,
                    Probe = probe
                };
            }

            return runs;
        }

        /// <summary>
        /// One sample of the whole field on the way to the run records: the fastest each car has been going,
        /// the lowest its body has been, and the ground it has covered since the last look. Every run in this
        /// fixture calls this once per <see cref="WaitForFixedUpdate"/> — the lap runs from inside
        /// <see cref="RunUntilLaps"/> and the wall run from its own window — so a top speed of nought or a
        /// lowest height of infinity in a report means the car was never seen rather than that it never went,
        /// which is the difference between a failure and a measurement that was not taken.
        /// </summary>
        private void SampleRuns(DriverRun[] runs)
        {
            for (int i = 0; i < runs.Length; i++)
            {
                DriverRun run = runs[i];
                AiDriver driver = run.Driver;
                if (driver == null || driver.Car == null) continue;

                if (driver.Car.CurrentSpeedMs > run.TopSpeedMs)
                {
                    run.TopSpeedMs = driver.Car.CurrentSpeedMs;
                }

                Vector3 position = driver.Car.PhysicsBody != null
                    ? driver.Car.PhysicsBody.position
                    : driver.Car.transform.position;
                if (position.y < run.MinY) run.MinY = position.y;

                run.Observe(position);
            }
        }

        /// <summary>
        /// How many metres an opponent's place on the circuit may run ahead of the ground its own body
        /// covered, m. Progress is measured against the centreline and the car drives a lane held off it, so
        /// a car on the inside of a turn is credited with the extra metres the line it is standing on has to
        /// travel to keep level with the one it is measured against: a lane of <paramref name="laneOffsetM"/>
        /// off the centreline is worth that many metres per radian turned, and the field turns one full turn
        /// per lap. Metres, signed — a car holding the outside of the road covers more road than it is
        /// credited with and earns no credit here, since that is the side of the allowance the fault this is
        /// bound by could not be made on.
        /// </summary>
        private static float LaneCreditM(float laneOffsetM)
        {
            return Mathf.Max(0f, -laneOffsetM * TurnRadiansPerLap);
        }

        /// <summary>
        /// The farthest round the circuit an opponent may be said to have come for the road
        /// <paramref name="odometerM"/> it covered, m: the road itself, the metres its lane is worth against
        /// the line it is measured on, and a metre of steering wander. A place on the circuit that arrived on
        /// less ground than this was not driven into place, and the fault this figure exists to catch is the
        /// one where a whole lap appeared between two samples of a body that had moved half a metre.
        /// </summary>
        private static float ProgressCeilingM(float odometerM, float laneOffsetM)
        {
            return odometerM + ProgressOverGroundM + LaneCreditM(laneOffsetM) + InsideLaneSlackM;
        }

        /// <summary>
        /// The largest one-step move of a body that is still the car going rather than being carried, m, for
        /// a car whose fastest measured speed was <paramref name="topSpeedMs"/>. Half a metre — the figure
        /// <see cref="WallRecoveryPlayTests"/> holds a reverse to, and the road a car does at 25 m/s in one
        /// step of a 50 Hz simulation — is both the floor of it and the whole of it anywhere under that
        /// speed; past it the claim has to travel with the physics or a car driven honestly fast is charged
        /// with being a teleport, so the ceiling becomes the road the fastest speed the car was actually seen
        /// doing can cover in one step, at the <see cref="StepGateScale"/> slack of it. A body picked up and
        /// put down is caught whichever way the clock is played, because it arrives at walking pace or lower
        /// and it arrives in metres.
        /// </summary>
        private static float ContinuousStepLimitM(float topSpeedMs)
        {
            return Mathf.Max(StepGateFloorM, topSpeedMs * FixedDelta() * StepGateScale);
        }

        /// <summary>Start watching the parked player from the pose it is in right now.</summary>
        private void StartPlayerWatch()
        {
            _watch = new PlayerWatch
            {
                ParkedAt = _player.PhysicsBody.position,
                StartY = _player.PhysicsBody.position.y
            };
        }

        /// <summary>One sample of the parked car: how far it has been moved, and how near anything came.</summary>
        private void SamplePlayer()
        {
            Vector3 here = _player.PhysicsBody.position;

            float displacement = Vector3.Distance(here, _watch.ParkedAt);
            if (displacement > _watch.MaxDisplacementM) _watch.MaxDisplacementM = displacement;

            Vector3 hereFlat = new Vector3(here.x, _watch.ParkedAt.y, here.z);
            float horizontal = Vector3.Distance(hereFlat, _watch.ParkedAt);
            if (horizontal > _watch.MaxHorizontalM) _watch.MaxHorizontalM = horizontal;

            float vertical = Mathf.Abs(here.y - _watch.ParkedAt.y);
            if (vertical > _watch.MaxVerticalM) _watch.MaxVerticalM = vertical;

            for (int i = 0; i < _opponents.Count; i++)
            {
                AiDriver driver = _opponents[i];
                Rigidbody body = driver != null && driver.Car != null ? driver.Car.PhysicsBody : null;
                if (body == null) continue;

                float approach = Vector3.Distance(body.position, here);
                if (approach < _watch.ClosestApproachM) _watch.ClosestApproachM = approach;
            }
        }

        /// <summary>
        /// Play the field out until every opponent — or, with <paramref name="everyCar"/> false, any one
        /// of them — has <paramref name="lapsWanted"/> counted laps, or
        /// <paramref name="timeoutSeconds"/> of simulated time has gone, whichever is first, sampling
        /// top speed, the lowest the body has been and the parked player on every physics tick. Timing
        /// and measuring only: the gates belong to the test that asked for the run, so a failure names
        /// the lap it was waiting for rather than this loop.
        /// </summary>
        private IEnumerator RunUntilLaps(DriverRun[] runs, int lapsWanted, float timeoutSeconds,
            bool everyCar)
        {
            float startedAt = Time.time;
            float deadline = startedAt + timeoutSeconds;
            _runTimedOut = true;

            while (true)
            {
                yield return new WaitForFixedUpdate();

                SampleRuns(runs);

                bool anyFinished = false;
                bool allFinished = true;

                for (int i = 0; i < runs.Length; i++)
                {
                    DriverRun run = runs[i];
                    bool finished = run.Driver != null && run.LapsThisRun >= lapsWanted;
                    anyFinished |= finished;
                    allFinished &= finished;
                }

                SamplePlayer();

                if (everyCar ? allFinished : anyFinished)
                {
                    _runTimedOut = false;
                    break;
                }

                if (Time.time >= deadline) break;
            }

            _runSeconds = Time.time - startedAt;
        }

        /// <summary>
        /// The race report: one line per opponent — laps counted, distance come against the perimeter
        /// of the circuit, top speed, how low the body went, where across the road it is holding, how
        /// many times it had to reverse out of something and how many times it had to be put back by
        /// hand — and then the field as a whole, the time it took, and what happened to the parked
        /// player. Logged before every gate is asserted, so the numbers are on the record either way.
        /// </summary>
        private void LogRun(string label, DriverRun[] runs)
        {
            float topSpeed = 0f;
            int mostLaps = 0;
            int recoveries = 0;
            int resets = 0;

            for (int i = 0; i < runs.Length; i++)
            {
                DriverRun run = runs[i];
                AiDriver driver = run.Driver;
                if (driver == null) continue;

                topSpeed = Mathf.Max(topSpeed, run.TopSpeedMs);
                mostLaps = Mathf.Max(mostLaps, driver.Laps);
                recoveries += driver.RecoveryCount;
                resets += driver.ResetCount;

                string continuity = run.Probe != null
                    ? $"biggest one-step body move {run.Probe.MaxStepM:F3} m at {run.Probe.MaxStepSpeedMs:F1} m/s " +
                      $"over {run.Probe.Steps} physics steps" +
                      (run.Probe.SkippedResetSteps > 0
                          ? $" ({run.Probe.SkippedResetSteps} step(s) of hand reset apart)"
                          : string.Empty)
                    : "no physics-step probe";

                Debug.Log($"[AI] {label}: {driver.Name} laps={driver.Laps} " +
                          $"progress={run.ProgressGain:F0} m ground={run.OdometerM:F0} m " +
                          $"(lap is {TrackLayout.Length:F0} m) top={run.TopSpeedMs:F1} m/s " +
                          $"minY={run.MinY:F2} m lateral={driver.Lateral:F1} m lane={driver.LaneOffset:F1} m " +
                          $"recoveries={driver.RecoveryCount} resets={driver.ResetCount}; {continuity}");
            }

            string playerLine = _watch != null
                ? $"player moved {_watch.MaxDisplacementM:F2} m (horizontal {_watch.MaxHorizontalM:F3} m, " +
                  $"vertical {_watch.MaxVerticalM:F3} m, within 4 m={_watch.WasTouched}) with closest approach " +
                  $"{_watch.ClosestApproachM:F1} m (touched={_watch.WasTouched})"
                : "player not watched";

            Debug.Log($"[AI] {label}: {runs.Length} opponents over {_runSeconds:F1} sim s at {SimSpeed:F0}x " +
                      $"({(_runTimedOut ? "timeout reached" : "field through")}), laps={mostLaps}, " +
                      $"top={topSpeed:F1} m/s, recoveries={recoveries}, resets={resets}; {playerLine}");
        }

        /// <summary>
        /// The volume a car occupies this frame, taken from every collider worn on the body it was built
        /// with — the chassis box and the four wheels. Opponents wear a painted cube as well, but its
        /// collider is destroyed by <see cref="AiField"/> for being a second collider on one car, so
        /// what physics has to keep apart is exactly this.
        /// </summary>
        private static Bounds CombinedBounds(VehicleController car)
        {
            Collider[] colliders = car.GetComponentsInChildren<Collider>();
            Assert.Greater(colliders.Length, 0, $"{car.name} is wearing no collider to take a bound from");

            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                bounds.Encapsulate(colliders[i].bounds);
            }

            return bounds;
        }

        /// <summary>
        /// Clear the scene the boot test loaded, roots only, so the rest of the fixture does not inherit
        /// a live race under the harness it builds for itself. Objects outside that scene are left alone,
        /// and a scene that is not the track is left alone entirely — clearing whatever happens to be
        /// active would take the test runner down with it.
        /// </summary>
        private static void DestroyActiveSceneObjects()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != RaceBootstrap.TrackSceneName)
            {
                return;
            }

            foreach (GameObject gameObject in Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (gameObject != null && gameObject.scene == scene
                    && gameObject.transform.parent == null)
                {
                    Object.Destroy(gameObject);
                }
            }
        }

        // ---- a: what the boot lines up on the grid -----------------------------------------

        /// <summary>
        /// The real scene, loaded for real, and the field the bootstrap is meant to have lined up behind
        /// the player: three opponents, none of them standing inside another — the guard that has to be
        /// there before physics is, because cars that start overlapping separate by exploding — each one
        /// painted a colour nobody else in the field wears, and the camera still on the player rather
        /// than on whichever car an object lookup happened to return first.
        ///
        /// Self-contained the way the restart test is: it builds nothing of its own, and everything it
        /// brings into the scene goes back out again in the <c>finally</c> along with the clock and the
        /// pause, so the tests after it start in an empty scene rather than under a live circuit.
        /// </summary>
        [UnityTest]
        public IEnumerator Boot_SpawnsThreeOpponents_NoOverlaps_DistinctColours_CameraOnPlayer()
        {
            // RGB distance. The field's own three colours are better than 0.7 apart, so this only has
            // to be far enough from zero to tell a reused colour from a deliberate one.
            const float MinimumColourSeparation = 0.1f;

            try
            {
                GamePause.SetPaused(false);
                Time.timeScale = 1f;

                SceneManager.LoadScene(RaceBootstrap.TrackSceneName);
                yield return WaitForFrames(SceneLoadFrames);

                Assert.AreEqual(RaceBootstrap.TrackSceneName, SceneManager.GetActiveScene().name,
                    "the track scene never became the active scene");

                RaceSession[] sessions =
                    Object.FindObjectsByType<RaceSession>(FindObjectsSortMode.None);
                Assert.AreEqual(1, sessions.Length, "the boot left this scene with exactly one session");
                Assert.IsNotNull(sessions[0].Car, "the session the boot built is not driving a car");

                RaceSession session = sessions[0];
                IReadOnlyList<AiDriver> opponents = session.Opponents;
                Assert.AreEqual(AiField.DefaultOpponentCount, opponents.Count,
                    "the boot is expected to line up the default field, which is three cars");

                var reported = new List<string>();
                for (int i = 0; i < opponents.Count; i++)
                {
                    AiDriver driver = opponents[i];
                    Assert.IsNotNull(driver, $"opponent {i} came back out of the field as nothing");
                    Assert.IsNotNull(driver.Car, $"opponent {i} has no car to drive");
                    Assert.IsNotNull(driver.Counter, $"{driver.Name} has no lap counter of its own");
                    Assert.AreEqual(TrackLayout.DefaultCheckpointCount, driver.Counter.CheckpointCount,
                        $"{driver.Name}'s counter is not sized to the gates the track was built with");
                    Assert.IsFalse(driver.Car.HumanControlled,
                        $"{driver.Name} is wired to the player's keyboard");
                    Assert.AreEqual($"AI_{i + 1}", driver.Name,
                        $"opponent {i} is called {driver.Name}; the field numbers itself from one");
                    Assert.AreEqual(0, driver.RecoveryCount,
                        $"{driver.Name} had needed a recovery before anything had happened to it");
                    Assert.AreEqual(0, driver.ResetCount,
                        $"{driver.Name} had been reset before anything had happened to it");

                    reported.Add($"{driver.Name}@{driver.Car.PhysicsBody.position.y:F1} m " +
                                 $"({driver.Color.r:F2}, {driver.Color.g:F2}, {driver.Color.b:F2})");
                }

                for (int i = 0; i < opponents.Count; i++)
                {
                    for (int j = i + 1; j < opponents.Count; j++)
                    {
                        Assert.Greater(Separation(opponents[i].Color, opponents[j].Color),
                            MinimumColourSeparation,
                            $"{opponents[i].Name} and {opponents[j].Name} wear the same colour, so the " +
                            "field cannot be read at 25 m/s");
                    }
                }

                AssertGridClear(session.Car, opponents, "on the grid");
                AssertCameraOnPlayer(session);

                Debug.Log($"[AI] boot: {opponents.Count} opponents assembled behind the player — " +
                          $"[{string.Join(", ", reported.ToArray())}], {opponents.Count + 1} car bounds " +
                          $"pairwise clear, colours distinct, camera on {session.Car.name}");
            }
            finally
            {
                GamePause.SetPaused(false);
                Time.timeScale = 1f;
                DestroyActiveSceneObjects();
            }
        }

        /// <summary>
        /// No two of the cars may be standing inside one another. The grid is two lanes and a row every
        /// eight metres of centreline, so at rest there is better than a metre to spare across the road
        /// and better than four along it; an overlap is the field standing on itself, which is the one
        /// start no physics can be recovered from. <paramref name="when"/> says which moment of the race
        /// is being measured, because a pair that has touched since the lights is a different story from
        /// a pair that began the lap inside each other.
        /// </summary>
        private static void AssertGridClear(VehicleController player, IReadOnlyList<AiDriver> opponents,
            string when)
        {
            int count = opponents.Count + 1;
            var names = new string[count];
            var volumes = new Bounds[count];

            names[0] = player != null ? player.name : "the player's car";
            volumes[0] = CombinedBounds(player);

            for (int i = 0; i < opponents.Count; i++)
            {
                names[i + 1] = opponents[i].Name;
                volumes[i + 1] = CombinedBounds(opponents[i].Car);
            }

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    Assert.IsFalse(volumes[i].Intersects(volumes[j]),
                        $"{names[i]} overlaps {names[j]} {when}: centre {volumes[i].center} extent " +
                        $"{volumes[i].size} against centre {volumes[j].center} extent {volumes[j].size}");
                }
            }
        }

        /// <summary>
        /// The camera belongs to the player and to nobody else in the field: <c>Camera.main</c> exists,
        /// carries a <see cref="ChaseCamera"/>, and that rig's target is the very transform of the car
        /// the session times — not one opponent's, and not none. Three cars joining a race is no reason
        /// for the player to stop being the one being followed.
        /// </summary>
        private static void AssertCameraOnPlayer(RaceSession session)
        {
            Camera main = Camera.main;
            Assert.IsNotNull(main, "the assembled scene has no Camera.main to look through");

            ChaseCamera chase = main.GetComponent<ChaseCamera>();
            Assert.IsNotNull(chase, "the scene camera carries no ChaseCamera, so nothing follows a car");
            Assert.IsNotNull(chase.target, "the chase rig has no target at all");

            Assert.IsTrue(session.Car.HumanControlled,
                "the car the session times is not the one a person drives");
            Assert.AreSame(session.Car.transform, chase.target,
                "the chase camera is not following the player's car");

            IReadOnlyList<AiDriver> opponents = session.Opponents;
            for (int i = 0; i < opponents.Count; i++)
            {
                Assert.AreNotSame(opponents[i].Car.transform, chase.target,
                    $"{opponents[i].Name} has taken the camera off the player");
            }
        }

        /// <summary>
        /// How far apart two body colours are, as plain distance through RGB — the measure a colour has
        /// to fail against to be the same one to somebody watching the field go past.
        /// </summary>
        private static float Separation(Color a, Color b)
        {
            float red = a.r - b.r;
            float green = a.g - b.g;
            float blue = a.b - b.b;
            return Mathf.Sqrt(red * red + green * green + blue * blue);
        }

        // ---- b: a lap, driven forwards, at speed, and on the road ----------------------------

        /// <summary>
        /// The field, raced. Every opponent is played out to one counted lap — counted, which on this
        /// circuit means it swept the line and the three gates beyond it, in order and in the driving
        /// direction, because that is what <see cref="LapCounter"/> will and will not accept — inside
        /// <see cref="LapTimeoutSeconds"/> of simulation. With the laps, four things have to be true of
        /// the lap it took. It was driven: better than <see cref="MinimumTopSpeedMs"/>, not a crawl. It
        /// cost it a lap of road — <see cref="LapDistanceFloorM"/> m under the body at least, which is
        /// both turns and both straights and no shortcut across the infield. The car stayed above ground.
        /// And it was never picked up and put down again: <see cref="AiDriver.ResetCount"/> is the one
        /// rescue a racing field may not need, while <see cref="AiDriver.RecoveryCount"/> — the reverse a
        /// driver puts in for itself out of a barrier — is part of racing and is logged rather than held
        /// against it.
        /// </summary>
        [UnityTest]
        public IEnumerator Opponents_DriveALap_CrossAllCheckpointsForward()
        {
            BuildRace(OpponentCount);

            yield return WaitForFrames(FirstSampleFrames);
            DriverRun[] runs = StartRuns();
            StartPlayerWatch();

            yield return RunUntilLaps(runs, LapsWanted, LapTimeoutSeconds, everyCar: true);
            LogRun("lap", runs);

            for (int i = 0; i < runs.Length; i++)
            {
                DriverRun run = runs[i];
                AiDriver driver = run.Driver;
                Assert.IsNotNull(driver, $"opponent {i} went missing mid-run");

                string who = driver.Name;
                Assert.GreaterOrEqual(run.LapsThisRun, LapsWanted,
                    $"{who} had counted {driver.Laps} laps after {_runSeconds:F1} of the " +
                    $"{LapTimeoutSeconds:F0} sim s a lap is allowed, having covered {run.OdometerM:F0} m " +
                    $"of the {TrackLayout.Length:F0} m circuit");
                Assert.Greater(run.TopSpeedMs, MinimumTopSpeedMs,
                    $"{who} never exceeded {run.TopSpeedMs:F1} m/s, so it crawled round rather than raced");
                Assert.Greater(run.OdometerM, LapDistanceFloorM,
                    $"{who} covered {run.OdometerM:F0} m of ground for a {TrackLayout.Length:F0} m lap, " +
                    "which is not a lap's worth of road — whatever it swept, it did not go round both " +
                    "turns");
                Assert.Greater(run.ProgressGain, TrackLayout.Length,
                    $"{who}'s place on the circuit moved only {run.ProgressGain:F0} m against a " +
                    $"{TrackLayout.Length:F0} m perimeter, so it never made it the length of the lap");
                Assert.Greater(run.MinY, FloorY,
                    $"{who} was {run.MinY:F2} m below the road surface while doing its lap");
                Assert.AreEqual(0, driver.ResetCount,
                    $"{who} was put back on the centreline by hand {driver.ResetCount} times to make its " +
                    $"lap, out of {driver.RecoveryCount} reverse recoveries");

                // The two figures the lap is judged on have to agree with one another: the place it made on
                // the circuit is bounded by the road its own body covered, and it has to show most of that
                // road too. The first is the bound progress may never be found over — a lap of the circuit
                // appearing between two samples of a car that had moved half a metre is the fault this whole
                // fixture exists to catch — and the second is the same claim from the other end, that a
                // reading which stops short of the ground spent is not measuring the lap either.
                float ceilingM = ProgressCeilingM(run.OdometerM, driver.LaneOffset);
                Assert.LessOrEqual(run.ProgressGain, ceilingM,
                    $"{who} was credited with coming {run.ProgressGain:F0} m round the circuit on " +
                    $"{run.OdometerM:F0} m of road it actually covered: the ceiling for that much ground is " +
                    $"{ceilingM:F0} m, of which {LaneCreditM(driver.LaneOffset):F0} m is what its " +
                    $"{driver.LaneOffset:F1} m lane is worth against the centreline it is measured on");

                Assert.GreaterOrEqual(run.ProgressGain, ProgressGroundRatio * run.OdometerM,
                    $"{who} covered {run.OdometerM:F0} m of road and was credited with coming only " +
                    $"{run.ProgressGain:F0} m round the circuit, so {run.OdometerM - run.ProgressGain:F0} m " +
                    "of it went where the reading never noticed");
            }
        }

        // ---- c: an opponent's lap stays the opponent's --------------------------------------

        /// <summary>
        /// The checkpoints lie across the whole road, so a field of three sweeping the line three times
        /// is three crossings of every gate, and the player's own bookkeeping must not notice a single
        /// one of them. The player's car is held on its brake, its counter read before the field is
        /// released and after an opponent has been round the circuit and over the line: still no laps,
        /// still no lap in progress, and still the same last gate it accepted before anything moved —
        /// which on a counter that keeps its book in indices is the assertion that nothing at all was
        /// ever reported to it, rather than only that nothing was counted.
        ///
        /// The hold is the handbrake and not the foot brake, deliberately: at a standstill the brake key
        /// is the reverse (see <see cref="VehicleTuning.ReverseEngageSpeedMs"/>), so a car "held on the
        /// brakes" down there is a car backing down the straight, and a player that has been driven off
        /// the grid by its own pedals during the test is a worse witness, not a better one.
        /// </summary>
        [UnityTest]
        public IEnumerator PlayerLaps_NotCountedFromAi()
        {
            BuildRace(OpponentCount);

            yield return WaitForFrames(FirstSampleFrames);

            LapCounter player = _session.Counter;
            Assert.IsNotNull(player, "the session has no counter of the player's laps to read");

            int lapsBefore = player.CompletedLaps;
            int lastGateBefore = player.LastValidCheckpoint;
            int nextGateBefore = player.NextExpectedCheckpoint;
            Assert.IsFalse(player.IsRunning, "the player's lap had already started before anything moved");

            DriverRun[] runs = StartRuns();
            StartPlayerWatch();

            yield return RunUntilLaps(runs, LapsWanted, LapTimeoutSeconds, everyCar: false);
            LogRun("player-clean", runs);

            int bestLap = 0;
            for (int i = 0; i < runs.Length; i++)
            {
                bestLap = Mathf.Max(bestLap, runs[i].LapsThisRun);
            }

            Assert.GreaterOrEqual(bestLap, 1,
                "no opponent completed a lap, so this test proved nothing about whose laps they were");

            Assert.AreEqual(lapsBefore, player.CompletedLaps,
                $"the player was credited with {player.CompletedLaps - lapsBefore} lap while an opponent " +
                $"went {bestLap} and never crossed a gate");
            Assert.AreEqual(lastGateBefore, player.LastValidCheckpoint,
                "the player's counter accepted a gate it was never driven through");
            Assert.AreEqual(nextGateBefore, player.NextExpectedCheckpoint,
                "the sequence the player's counter is waiting for moved without the player moving");
            Assert.IsFalse(player.IsRunning,
                "an opponent's crossing of the line started the player's lap");

            Debug.Log($"[AI] player-clean: player laps={player.CompletedLaps} " +
                      $"lastGate={player.LastValidCheckpoint} nextGate={player.NextExpectedCheckpoint} " +
                      $"while an opponent went {bestLap} lap(s); player moved " +
                      $"{_watch.MaxDisplacementM:F2} m");
        }

        // ---- d: two drivers, one keyboard -----------------------------------------------------

        /// <summary>
        /// The field drives itself and nothing else is driving it. Every opponent's car is built
        /// human-controlled-false, which is the switch that decides whether
        /// <see cref="VehicleController"/> ever loads the input map for that car at all, so an opponent
        /// is not a car being handed a keyboard and told to ignore it — it is a car with no hands on it.
        /// The behaviour is what carries the claim though: one car in the scene <i>is</i> wired to the
        /// devices, the player's, and it is held at nil throttle, nil brake, nil lock with nothing but
        /// its handbrake on it, while every opponent puts a lap's worth of distance down beside it. If
        /// the field were reading the player's controls, or writing to them, that parked car would be
        /// somewhere else by the end — and it is held to the tenth of a millimetre of a hand's breadth
        /// rather than to the two metres that would hide a shove, because the car is on its own tarmac with
        /// nothing in the scene that could touch it and a suspension that has already finished settling.
        /// </summary>
        [UnityTest]
        public IEnumerator AiInput_Independent()
        {
            BuildRace(OpponentCount);

            Assert.IsTrue(_player.HumanControlled,
                "the player's car is not the one wired to the devices, so this proves nothing");

            for (int i = 0; i < _opponents.Count; i++)
            {
                VehicleController car = _opponents[i].Car;
                Assert.IsNotNull(car, $"opponent {i} has no car to check the wiring of");
                Assert.IsFalse(car.HumanControlled,
                    $"{_opponents[i].Name}'s car is wired to the player's keyboard");
            }

            yield return WaitForFrames(FirstSampleFrames);

            // The springs come to rest before the millimetres the parked car is allowed to move in are
            // measured, so that what is being watched is a car held and not a car still arriving at its own
            // ride height. The field gets the same simulation either way and simply starts a little further
            // up the straight than the grid left it.
            yield return FixedSteps(ParkSettleSeconds);

            DriverRun[] runs = StartRuns();
            StartPlayerWatch();

            yield return RunUntilLaps(runs, LapsWanted, LapTimeoutSeconds, everyCar: false);
            LogRun("input", runs);

            // The player is still held on the controls it was given, so nothing in the scene has taken
            // them over: the same zero hold, re-asserted at the end of the run.
            ParkPlayer();

            for (int i = 0; i < runs.Length; i++)
            {
                DriverRun run = runs[i];
                Assert.Greater(run.OdometerM, MinimumIndependentDistanceM,
                    $"{run.Driver.Name} covered only {run.OdometerM:F0} m of road while the player sat " +
                    "still, so it was not driving itself");
            }

            if (_watch.MaxDisplacementM > PlayerStayToleranceM)
            {
                Assert.IsTrue(_watch.WasTouched,
                    $"the parked player moved {_watch.MaxDisplacementM:F2} m from the grid while the field " +
                    $"went about its business, and nothing came within " +
                    $"{_watch.ClosestApproachM:F1} m of it to have pushed it");
            }

            // Held means held, and a lap of the field going past is no excuse for the number to be any
            // bigger: there is no air to push with and nothing else on the road, so a parked car that has
            // moved a tenth of a metre has either been touched by a car that had no business coming that
            // close or has been driven by something other than its own handbrake. The first is a field that
            // cannot be raced alongside a stalled car; the second is the claim this test is made of.
            Assert.Less(_watch.MaxDisplacementM, ParkedLapToleranceM,
                $"the parked player moved {_watch.MaxDisplacementM:F2} m while an opponent went its lap " +
                $"beside it, against the {ParkedLapToleranceM * 1000f:F0} mm a handbrake on its own tarmac " +
                $"is allowed; closest anything came was {_watch.ClosestApproachM:F2} m away " +
                $"(touched={_watch.WasTouched})");
        }

        // ---- e: nose in the barrier ---------------------------------------------------------

        /// <summary>
        /// One opponent, put nose-first into the outside wall on the back straight — set down square on
        /// it with a nudge, so it arrives under its own momentum and is then held there by throttle it
        /// asked for itself, which is the predicament the reverse was built for and the one a reset would
        /// paper over. It is then left alone for <see cref="StuckWindowSeconds"/> of simulation, sampled
        /// every fixed step the way the lap runs are, and what the sampling has to account for it by is the
        /// <see cref="MinimumRecoveredDistanceM"/> metres of circuit it comes — metres it could not add
        /// standing against a barrier, so they can only come from backing out of it, pointing the car back
        /// down the road and driving away. Four things are then held against the record it leaves: the car
        /// was seen doing them (a speed and a height are on the record, and neither is the infinity of a
        /// measurement nobody took); it was never picked up, either by count or by a body moving further in
        /// one physics step than its own speed is worth; those metres are bounded by the ground the body
        /// covered; and it ended up gone from the wall rather than merely off its nose. And at the end of it
        /// the car has to be inside the walls again rather than somewhere along them.
        ///
        /// The player sits in the same scene, on its handbrake and out of the way at the other end of
        /// the circuit, and is watched throughout: nothing that happens to an opponent may reach across
        /// and move it — and it is watched to the centimetre, its suspension having been left to settle
        /// before the watching starts. One opponent rather than three because this is the one test where a
        /// car being shoved is a reason the test would lie — with a single opponent on the road, the only
        /// thing that could have moved the player is the thing being tested.
        /// </summary>
        [UnityTest]
        public IEnumerator StuckAi_Recovers_WithoutMovingPlayer()
        {
            BuildRace(1);

            yield return WaitForFrames(FirstSampleFrames);

            AiDriver driver = _opponents[0];
            Assert.IsNotNull(driver, "the field did not produce the opponent this test needs");
            Assert.IsNotNull(driver.Car, $"{driver.Name} has no car to put into a wall");

            // The parked car's springs are given leave to come to rest before the centimetres it is watched
            // for are counted, and before the wall run begins: what the figure below is claiming is that a
            // handbrake holds a car on its own tarmac, and a car still dropping onto its ride height from the
            // pose it was built in would spend that allowance on nothing at all.
            yield return FixedSteps(ParkSettleSeconds);

            StartPlayerWatch();
            JamAgainstOuterWall(driver);

            // A step of the driver's own bookkeeping first, so the distance the run is measured from is
            // where it was put and not where it was standing before it was moved.
            yield return WaitForFrames(FirstSampleFrames);

            DriverRun[] runs = StartRuns();
            DriverRun run = runs[0];
            AiOpponentStepProbe probe = run.Probe;
            Assert.IsNotNull(probe, $"no physics-step probe was armed on {driver.Name}");

            // The wall itself, as the point the nose was sent at, so that "gone from the wall" is a distance
            // from the barrier and not a distance travelled: a car that reversed six metres and then sat there
            // has travelled and has not gone anywhere.
            Vector3 wallPoint = WallContactPoint();
            Rigidbody wallBody = driver.Car.PhysicsBody;
            Assert.IsNotNull(wallBody, $"{driver.Name} lost its body between the grid and the wall");

            float closestToWallM = Vector3.Distance(wallBody.position, wallPoint);
            float farthestFromWallM = closestToWallM;

            float startedAt = Time.time;
            float windowEnd = startedAt + StuckWindowSeconds;

            // Every figure this test judges comes from the same sampling the lap runs do, once a fixed step:
            // a top speed of nought and a lowest height of infinity are what a window in which nothing looked
            // reports, and they are not evidence about the car.
            while (Time.time < windowEnd
                   && (run.ProgressGain < MinimumRecoveredDistanceM
                       || farthestFromWallM - closestToWallM < AwayFromWallM))
            {
                yield return new WaitForFixedUpdate();

                SampleRuns(runs);
                SamplePlayer();

                float distanceToWall = Vector3.Distance(wallBody.position, wallPoint);
                if (distanceToWall < closestToWallM) closestToWallM = distanceToWall;
                if (distanceToWall > farthestFromWallM) farthestFromWallM = distanceToWall;
            }

            _runSeconds = Time.time - startedAt;
            _runTimedOut = run.ProgressGain < MinimumResumedProgressM;
            LogRun("stuck", runs);

            float distanceFromWallM = Vector3.Distance(wallBody.position, wallPoint);
            Debug.Log($"[AI] stuck: {driver.Name} was {closestToWallM:F1} m from the wall at its closest and " +
                      $"is {distanceFromWallM:F1} m from it now, having come {farthestFromWallM:F1} m at the " +
                      $"farthest; progress {run.ProgressGain:F0} m on {run.OdometerM:F0} m of ground, top " +
                      $"{run.TopSpeedMs:F1} m/s, minY {run.MinY:F2} m, biggest one-step body move " +
                      $"{probe.MaxStepM:F3} m in {probe.Steps} physics steps, player moved " +
                      $"{_watch.MaxDisplacementM:F3} m");

            Assert.GreaterOrEqual(driver.RecoveryCount, 1,
                $"{driver.Name} made {run.ProgressGain:F0} m against a barrier without ever putting the " +
                "reverse in, so this never tested the recovery");
            Assert.Greater(run.ProgressGain, MinimumResumedProgressM,
                $"{driver.Name} was still where the wall held it after {_runSeconds:F1} of the " +
                $"{StuckWindowSeconds:F0} sim s it is allowed to fetch itself out: {run.ProgressGain:F0} m " +
                $"come, {driver.RecoveryCount} reverses tried, {driver.ResetCount} resets needed");
            Assert.Less(Mathf.Abs(driver.Lateral), TrackLayout.RoadWidth * 0.5f,
                $"{driver.Name} is back up to speed but {driver.Lateral:F1} m off the centreline, so it " +
                "never came back inside the walls");
            Assert.IsFalse(driver.IsRecovering,
                $"{driver.Name} is still reversing out of it after {_runSeconds:F1} s");

            // The window was watched. Half a metre of body in one physics step is the road at 25 m/s, so a
            // speed and a height on the record are what the sampling that put them there looked like, and a
            // nought or an infinity is what a run nobody sampled reports — which is a broken measurement
            // rather than a car that failed to drive, and has to be told apart from one.
            Assert.IsTrue(Measured(run.TopSpeedMs) && run.TopSpeedMs > MinimumRecoveredSpeedMs,
                $"{driver.Name} was never seen above {run.TopSpeedMs:F1} m/s across the {_runSeconds:F1} s " +
                $"of its {StuckWindowSeconds:F0} s window, so nothing on the record says it drove out of the " +
                $"wall at all (it came {run.ProgressGain:F0} m)");
            Assert.IsTrue(Measured(run.MinY) && run.MinY > FloorY,
                $"{driver.Name}'s body was lowest seen at {run.MinY:F2} m, which is either below the road or " +
                $"the infinity of a height nobody measured, over a run that came {run.ProgressGain:F0} m");

            // No hand was laid on the body: it came out of the wall under its own throttle or it did not.
            Assert.AreEqual(0, driver.ResetCount,
                $"{driver.Name} needed {driver.ResetCount} hand reset(s) out of the wall, after " +
                $"{driver.RecoveryCount} reverse(s) of its own — the {run.ProgressGain:F0} m it came is the " +
                "rescue doing it rather than the recovery");

            // And the metres it came were driven in the numbers the physics offers, one step at a time: the
            // body moved no further in one step than the speed it was seen at can cover in one, which is the
            // continuity the reset above would have broken and the reason the progress figure means anything.
            Assert.Greater(probe.Steps, 0, $"no physics step of {driver.Name} was ever sampled");
            float stepLimitM = ContinuousStepLimitM(Mathf.Max(run.TopSpeedMs, probe.MaxStepSpeedMs));
            Assert.Less(probe.MaxStepM, stepLimitM,
                $"{driver.Name} moved {probe.MaxStepM:F3} m in one physics step at " +
                $"{probe.MaxStepSpeedMs:F1} m/s without ever being reset ({driver.ResetCount} resets), " +
                $"against the {stepLimitM:F2} m that step of that speed is worth — {probe.Steps} steps of " +
                $"{FixedDelta() * 1000f:F0} ms sampled, so a body was moved with nothing driving it");

            // Gone from the wall, measured from the barrier rather than from wherever it happened to point:
            // the metres it put behind it are what the reverse and the drive away are worth.
            Assert.Greater(distanceFromWallM - closestToWallM, AwayFromWallM,
                $"{driver.Name} came {run.ProgressGain:F0} m round the circuit and ended " +
                $"{distanceFromWallM:F1} m from the wall it was nosed into, {closestToWallM:F1} m of that " +
                $"accounted for by how close it ever got, so it is {distanceFromWallM - closestToWallM:F1} m " +
                $"gone from it against the {AwayFromWallM:F0} m asked for");

            // The place it made on the circuit is bounded by the road its own body covered — the wall is not
            // road, and neither are the metres a body is carried.
            Assert.Greater(run.ProgressGain, MinimumRecoveredDistanceM,
                $"{driver.Name} was {run.ProgressGain:F0} m round the circuit at the end of its " +
                $"{_runSeconds:F1} s, against the {MinimumRecoveredDistanceM:F0} m of driving away that " +
                "counts as back in the race");
            Assert.LessOrEqual(run.ProgressGain, run.OdometerM + RecoveredProgressOverGroundM,
                $"{driver.Name} was credited with {run.ProgressGain:F0} m round the circuit on {run.OdometerM:F0} m " +
                "of ground its body covered, so metres it did not drive are in the place it was given");

            if (_watch.MaxDisplacementM > PlayerParkedToleranceM)
            {
                Assert.IsTrue(_watch.WasTouched,
                    $"the player moved {_watch.MaxDisplacementM:F2} m while an opponent dug itself out of a " +
                    $"wall, and nothing came within {_watch.ClosestApproachM:F0} m of it to have pushed it");
            }

            // And the held car held to the centimetre. There is one other car on the road in this test, it is
            // at the other end of the circuit, and there is nothing else in the scene to push with.
            Assert.Less(_watch.MaxDisplacementM, ParkedHeldToleranceM,
                $"the parked player moved {_watch.MaxDisplacementM:F3} m while {driver.Name} dug itself out " +
                $"of the wall, against the {ParkedHeldToleranceM * 1000f:F0} mm a handbrake on its own tarmac " +
                $"is worth; closest anything came was {_watch.ClosestApproachM:F1} m away " +
                $"(touched={_watch.WasTouched})");
        }

        // ---- placing an opponent in a wall ---------------------------------------------------

        /// <summary>
        /// Set an opponent down square against the outside wall down the back straight: the centreline
        /// pose at <see cref="StuckPlaceDistanceM"/>, stepped out to where the wall is waiting, turned to
        /// face along that step rather than down the road, and given <see cref="StuckNudgeMs"/> of speed
        /// into it. Written to the physics body the way <see cref="AiDriver"/> writes its own reset —
        /// velocities killed, then pose, then the nudge — and to nothing else. The driver is left holding
        /// the wheel it was already holding, which is the point: it finds out for itself, within
        /// <see cref="AiDriver.StuckSeconds"/>, that it has stopped going anywhere.
        /// </summary>
        private static void JamAgainstOuterWall(AiDriver driver)
        {
            Pose centre = TrackLayout.PoseAt(StuckPlaceDistanceM);

            // The outside of this circuit is the right-hand side of the road and the oval is flat, so the
            // step out to the wall is level with the road and asks for no correction of its own.
            Vector3 outward = centre.right;
            outward.y = 0f;
            outward.Normalize();

            Rigidbody body = driver.Car.PhysicsBody;
            Assert.IsNotNull(body, $"{driver.Name} has no physics body to place against a wall");

            Vector3 position = centre.position + outward * StuckPlaceLateralM
                               + Vector3.up * TrackLayout.SpawnHeight;
            Quaternion rotation = Quaternion.LookRotation(outward, Vector3.up);

            body.angularVelocity = Vector3.zero;
            body.linearVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = outward * StuckNudgeMs;

            driver.Car.transform.SetPositionAndRotation(position, rotation);

            Debug.Log($"[AI] stuck: {driver.Name} pointed at the outside wall {StuckPlaceDistanceM:F0} m " +
                      $"round the lap, {StuckPlaceLateralM:F1} m out, at {StuckNudgeMs:F1} m/s");
        }

        /// <summary>
        /// The point on the outside wall that <see cref="JamAgainstOuterWall"/> sends a car at: the
        /// centreline pose at <see cref="StuckPlaceDistanceM"/>, stepped out to the inner face line of the
        /// wall (<see cref="TrackBuilder.WallOffset"/> from the centreline, which is where a nose arrives).
        /// How far the recovered car got is measured from here rather than from where it was standing, so
        /// that the claim is about the barrier it left and not about the metres it happened to spend: a car
        /// that backed off six metres and then sat in the road has travelled and has not gone away.
        /// </summary>
        private static Vector3 WallContactPoint()
        {
            Pose centre = TrackLayout.PoseAt(StuckPlaceDistanceM);

            Vector3 outward = centre.right;
            outward.y = 0f;
            outward.Normalize();

            return centre.position + outward * TrackBuilder.WallOffset;
        }

        /// <summary>
        /// Whether a measured figure is a number at all. The runs leave two figures at their own starting
        /// values until something samples them — a top speed at nought and a lowest height at infinity — so
        /// an unmeasured one has to be told from a measured one before either is held to a gate.
        /// </summary>
        private static bool Measured(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Keeps the widest single move one car's physics body makes, and the speed it was going at the moment it
    /// made it, on every step of the physics. One step bigger than the road the speed covers in a step is a
    /// body that has been picked up and put down; that is the whole of what this looks for, and it is the
    /// reason the progress figure of a run can be believed at all.
    ///
    /// <b>Why it is a component and not part of the test loop.</b> A coroutine waiting on
    /// <see cref="WaitForFixedUpdate"/> is resumed once per <i>frame</i>, and these runs are played at a
    /// <see cref="Time.timeScale"/> above one, so a loop that looks as though it is reading the body once a
    /// physics tick is really reading it once every few ticks and calling the sum of them one step. This runs
    /// from <see cref="FixedUpdate"/>, which is one a tick by construction.
    /// </summary>
    public sealed class AiOpponentStepProbe : MonoBehaviour
    {
        /// <summary>Widest step taken with the driver's hand-reset count unchanged, m.</summary>
        public float MaxStepM { get; private set; }

        /// <summary>The speed the car was read at on the step that set <see cref="MaxStepM"/>, m/s.</summary>
        public float MaxStepSpeedMs { get; private set; }

        /// <summary>Physics steps sampled since the probe was armed.</summary>
        public int Steps { get; private set; }

        /// <summary>
        /// Steps set apart by a hand reset, and so left out of <see cref="MaxStepM"/>: a reset carries the
        /// body, which is counted, reported and asserted against by the test that owns the run rather than
        /// being measured here as an unexplained jump.
        /// </summary>
        public int SkippedResetSteps { get; private set; }

        private Rigidbody _body;
        private AiDriver _driver;
        private Vector3 _last;
        private int _lastResetCount;
        private bool _seen;

        /// <summary>Start measuring from the pose the body is in now — call after the car has been placed.</summary>
        public void Arm(Rigidbody body, AiDriver driver)
        {
            _body = body;
            _driver = driver;
            _seen = false;
            MaxStepM = 0f;
            MaxStepSpeedMs = 0f;
            Steps = 0;
            SkippedResetSteps = 0;
        }

        private void FixedUpdate()
        {
            if (_body == null) return;

            Vector3 position = _body.position;
            int resetCount = _driver != null ? _driver.ResetCount : 0;

            if (!_seen)
            {
                _last = position;
                _lastResetCount = resetCount;
                _seen = true;
                return;
            }

            float step = Vector3.Distance(_last, position);
            _last = position;
            Steps++;

            if (resetCount != _lastResetCount)
            {
                SkippedResetSteps++;
                _lastResetCount = resetCount;
                return;
            }

            if (step > MaxStepM)
            {
                MaxStepM = step;
                MaxStepSpeedMs = _driver != null && _driver.Car != null
                    ? _driver.Car.CurrentSpeedMs
                    : 0f;
            }
        }
    }
}
