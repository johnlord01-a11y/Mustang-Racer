using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenWheelRacer;
using OpenWheelRacer.Race;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// <summary>
    /// What the car actually feels like on the built circuit, measured rather than read out of
    /// the tuning file: how fast the steering rack comes off centre, how much lock is left once
    /// the speed is up, whether a keyboard player holding full lock at 20 m/s ends up spinning,
    /// and whether the handbrake still puts the rear end out.
    ///
    /// The stage is assembled the way <see cref="RaceBootstrapPlayTests"/> assembles it — a
    /// <see cref="TrackBuilder"/> plus a <see cref="VehicleController.BuildCar"/> dropped on
    /// <see cref="TrackBuilder.Spawn"/> and armed with
    /// <see cref="VehicleController.ArmSpawnPoint"/>, this fixture keeping its own private copy
    /// of that setup and teardown so nothing is shared between fixtures — and the car is driven
    /// only through <see cref="VehicleController.SetTestControls"/>. Everything then runs in
    /// real play: nothing calls <c>Physics.Simulate</c> or <c>VehicleController.StepTestFrame</c>,
    /// every window is measured in <see cref="Time.fixedDeltaTime"/> steps of a live clock resumed
    /// with <see cref="WaitForFixedUpdate"/>, and the numbers come from the same
    /// <see cref="VehicleController.CurrentSteerAngle"/> / <see cref="VehicleController.CurrentSpeedMs"/>
    /// the HUD shows a player.
    ///
    /// Every test logs what it measured with an <c>[Handling]</c> prefix before it asserts, so a
    /// run of this fixture is a tuning readout whether or not the gates are met.
    ///
    /// Two facts about the prototype circuit shape what is measured:
    ///  * the grid lies 6 m short of the line on a 200 m straight, so there are only ~106 m of
    ///    open road ahead and the walls stand 6.5 m either side of the centreline
    ///    (<see cref="TrackBuilder.WallOffset"/>) — a hard turn at 20 m/s does reach one, and the
    ///    metrics are then taken through the contact rather than pretended away;
    ///  * the ground the builder lays carries no <c>PhysicMaterial</c>, so the grip the
    ///    <c>WheelCollider</c> friction curves are scaled by sits well under the tuned road the
    ///    Stage 1 referee drives on, and a throttle-only run needs a second or two and a good part
    ///    of the straight to reach 18 m/s. Nothing here writes to the rigid body to make a speed,
    ///    though: the throttle is the only way onto one, and every measurement is logged together
    ///    with the speed its approach actually delivered and how far down the straight it arrived.
    /// </summary>
    [TestFixture]
    public sealed class HandlingMetricsPlayTests
    {
        // ---- harness ------------------------------------------------------------------------

        /// <summary>Fixed time the car is left alone to find its ride height before being driven.</summary>
        private const float SettleSeconds = 0.6f;

        /// <summary>Fixed time the car is brought back to rest on the grid between manoeuvres.</summary>
        private const float ResetSettleSeconds = 0.5f;

        /// <summary>Full steering input: the keyboard held hard, not an analog wheel.</summary>
        private const float FullSteer = 1f;

        /// <summary>Fraction of the settled lock angle the ramp of the first test is timed to.</summary>
        private const float NinetyFraction = 0.9f;

        // ---- 1: the rack must not snap --------------------------------------------------------

        /// <summary>Throttle held to reach the "rest-ish" speed the ramp is measured from.</summary>
        private const float RestThrottle = 0.3f;

        /// <summary>Fixed time that light throttle is held for before the steering is asked for.</summary>
        private const float RestSeconds = 1f;

        /// <summary>How long the ramp is sampled for, and so how long the lock has to settle in.</summary>
        private const float RampWindowSeconds = 0.6f;

        /// <summary>How long after the input the first reading of the rack is taken.</summary>
        private const float RampSampleSeconds = 0.05f;

        /// <summary>Shortest time 90% of lock may be reached in before the rack counts as ramped.</summary>
        private const float MinimumNinetyPercentSeconds = 0.15f;

        /// <summary>Below this settled lock angle the 90% figure is meaningless rather than measured.</summary>
        private const float SettledAngleFloorDegrees = 5f;

        // ---- 2: the lock left over at speed -----------------------------------------------------

        /// <summary>The two speeds the settled full-lock angle is compared between, m/s.</summary>
        private const float LowLockSpeedMs = 8f;

        private const float HighLockSpeedMs = 25f;

        /// <summary>Fixed time full lock is wound on and held for while the angle is sampled.</summary>
        private const float SteerHoldSeconds = 0.5f;

        /// <summary>Throttle held against the lock, so the speed measured is the speed asked for.</summary>
        private const float HoldThrottle = 1f;

        /// <summary>How much the high-speed lock may be of the low-speed lock before authority is gone.</summary>
        private const float MaxHighToLowLockRatio = 0.5f;

        /// <summary>How far a measured speed may sit from the speed it was asked for, m/s.</summary>
        private const float SpeedToleranceMs = 3f;

        // ---- 3: the sustained keyboard turn ------------------------------------------------------

        /// <summary>Speed the straight is brought up to before lock is held on, m/s.</summary>
        private const float TurnEntrySpeedMs = 20f;

        /// <summary>Fixed time full lock is held for, which is how long a spin takes to show.</summary>
        private const float TurnSeconds = 2.5f;

        /// <summary>Throttle held through the sustained turn — committed, but not a launch.</summary>
        private const float TurnThrottle = 0.5f;

        /// <summary>Most the nose may point away from where the car is going before it is a spin.</summary>
        private const float MaximumSideslipDegrees = 25f;

        /// <summary>Below this speed the direction travelled is the yaw of a standstill, not a slip.</summary>
        private const float SideslipMinimumSpeedMs = 3f;

        /// <summary>
        /// Scale of the primitive plane this turn is taken on. A Unity plane is 10 m across at scale 1,
        /// so this is a 500 m sheet of tarmac with no wall, kerb or edge inside the window: full lock at
        /// <see cref="TurnEntrySpeedMs"/> holds the car to a radius of roughly 30 m, so
        /// <see cref="TurnSeconds"/> of it wants some 40 m either side of where it started, and the
        /// circuit's walls stand <see cref="TrackBuilder.WallOffset"/> out.
        /// </summary>
        private const float TurnPlaneScale = 50f;

        /// <summary>Height the car is set down at on that plane: chassis centre clear of the surface,
        /// leaving it the same short drop to its ride height the grid pose gives it on the circuit.</summary>
        private const float TurnPlaneSpawnHeight = 1f;

        // ---- 4: the handbrake turn ---------------------------------------------------------------

        /// <summary>Speed both halves of the handbrake comparison are entered at, m/s.</summary>
        private const float SlideEntrySpeedMs = 18f;

        /// <summary>Steering the slide is entered with; full lock would need no handbrake at all.</summary>
        private const float SlideSteer = 0.6f;

        /// <summary>Fixed time the slide is held for, both with and without the handbrake.</summary>
        private const float SlideSeconds = 1f;

        /// <summary>Throttle held in both halves, so the handbrake stays the only difference.</summary>
        private const float SlideThrottle = 0.5f;

        // ---- getting to a speed on a 106 m straight -----------------------------------------------

        /// <summary>Throttle held while an approach to a target speed is under way.</summary>
        private const float ApproachThrottle = 1f;

        /// <summary>
        /// Most the throttle-only approach may run for before it is given up on. Generous on purpose:
        /// the road the builder lays has no <c>PhysicMaterial</c>, so a second of standing start buys
        /// less speed here than it would on the tuned surface the Stage 1 referee drives on.
        /// </summary>
        private const float ApproachTimeoutSeconds = 6f;

        /// <summary>How close to the target the throttle is closed, m/s.</summary>
        private const float ApproachStopToleranceMs = 0.1f;

        /// <summary>Fixed time at the end of an approach left for suspension and rack to settle in.</summary>
        private const float ApproachSettleSeconds = 0.25f;

        private GameObject _trackRoot;
        private TrackBuilder _track;
        private GameObject _carObject;
        private VehicleController _car;

        /// <summary>The wall-less stage of <see cref="SustainedKeyboardTurn_DoesNotSpin"/> and the
        /// contact counter witness to it. Both stay null on every other test of this fixture, which is
        /// driven on the circuit the <see cref="TrackBuilder"/> lays.</summary>
        private GameObject _turnPlane;
        private NonGroundContactCounter _turnContacts;

        /// <summary>The grid pose every metric is measured relative to: along, across and from.</summary>
        private Vector3 _gridPosition;
        private Vector3 _gridForward;
        private Vector3 _gridRight;

        [SetUp]
        public void SetUp()
        {
            // Every window in this fixture is measured off the engine clock, so a fixture that
            // ran the pause menu (a static) or stopped time some other way before it would have
            // these tests wait on seconds that never arrive and report a timeout instead of a
            // measurement.
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            // The clock goes back for whichever test runs next wherever it came from, and the two
            // roots of this fixture's own stage go with it — deferred, because the builder frees
            // the materials it created in its own OnDestroy.
            Time.timeScale = 1f;

            if (_carObject != null) Object.Destroy(_carObject);
            if (_trackRoot != null) Object.Destroy(_trackRoot);

            // The sustained turn tears its own plane down at the end of the test; this is the leak
            // guard for the run that asserted its way out of it early, because a 500 m sheet left
            // lying on y = 0 would sit under every circuit stage that ran after it.
            if (_turnPlane != null) Object.Destroy(_turnPlane);

            _carObject = null;
            _car = null;
            _trackRoot = null;
            _track = null;
            _turnPlane = null;
            _turnContacts = null;
        }

        // ---- the stage: the real circuit, the real car, the grid it is armed with ------------------

        /// <summary>
        /// Circuit plus car on the grid, exactly as <see cref="RaceBootstrapPlayTests"/> and
        /// <c>RaceBootstrap</c> make them — build first, then place transform and rigid body, then
        /// arm the reset pose — kept private to this fixture on purpose: these tests measure the
        /// car on the road, so they want the road and the car and nothing else in the scene (no
        /// session, no camera, no HUD) to keep every number they read about the tyres.
        /// </summary>
        private void BuildTrackAndCarOnGrid()
        {
            _trackRoot = new GameObject("HandlingMetricsTrackHarness");
            _track = _trackRoot.AddComponent<TrackBuilder>();

            // AddComponent ran Awake, which builds on awake by default; the explicit call is the
            // same fallback the bootstrap uses.
            if (!_track.HasBuilt)
            {
                _track.Build();
            }

            Assert.IsTrue(_track.HasBuilt, "builder did not build when asked");
            Assert.AreEqual(TrackLayout.DefaultCheckpointCount, _track.Checkpoints.Length,
                "the metrics are meant to be taken on the prototype circuit, not an empty plane");

            _carObject = VehicleController.BuildCar(null);
            Pose spawn = _track.Spawn;
            _carObject.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

            _car = _carObject.GetComponent<VehicleController>();
            Assert.IsNotNull(_car.PhysicsBody, "BuildCar must deliver an assembled physics body");
            _car.PhysicsBody.position = spawn.position;
            _car.PhysicsBody.rotation = spawn.rotation;
            _car.ArmSpawnPoint();

            _gridPosition = spawn.position;
            _gridForward = Flatten(spawn.rotation * Vector3.forward);
            _gridRight = Flatten(spawn.rotation * Vector3.right);

            // Straight ahead and standing still until a test says otherwise, so no test can start
            // coasting out of whatever its predecessor was left holding.
            _car.SetTestControls(0f, 0f, 0f);
        }

        /// <summary>
        /// The wall-less stage <see cref="SustainedKeyboardTurn_DoesNotSpin"/> is driven on: one
        /// primitive plane at <see cref="TurnPlaneScale"/> — 500 m of tarmac, its surface on y = 0 like
        /// the ground the builder lays — with the car set down on it at
        /// <see cref="TurnPlaneSpawnHeight"/> and armed with a spawn pose of its own. No ground ribbon,
        /// no walls, no triggers, and no edge the car can be run off inside a manoeuvre, so every number
        /// the test reads afterwards is about the tyres; the grid axes are taken from the pose the car
        /// was set down on for the same reason. <see cref="BuildTrackAndCarOnGrid"/> is left to the
        /// tests that are about the circuit.
        /// </summary>
        private void BuildOpenPlaneForTurn()
        {
            _turnPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _turnPlane.name = "HandlingMetricsOpenPlane";
            _turnPlane.transform.localScale = new Vector3(TurnPlaneScale, 1f, TurnPlaneScale);

            var grid = new Pose(
                new Vector3(0f, TurnPlaneSpawnHeight, 0f),
                Quaternion.LookRotation(Vector3.forward, Vector3.up));

            _carObject = VehicleController.BuildCar(null);
            _carObject.transform.SetPositionAndRotation(grid.position, grid.rotation);

            _car = _carObject.GetComponent<VehicleController>();
            Assert.IsNotNull(_car.PhysicsBody, "BuildCar must deliver an assembled physics body");
            _car.PhysicsBody.position = grid.position;
            _car.PhysicsBody.rotation = grid.rotation;
            _car.ArmSpawnPoint();

            _gridPosition = grid.position;
            _gridForward = Flatten(grid.rotation * Vector3.forward);
            _gridRight = Flatten(grid.rotation * Vector3.right);

            // Fitted with the stage rather than at the window, so the counter has caught the chassis
            // meeting something on the approach too — StartWindow then decides what is judged.
            _turnContacts = _carObject.AddComponent<NonGroundContactCounter>();
            _turnContacts.Arm(_turnPlane);

            _car.SetTestControls(0f, 0f, 0f);
        }

        /// <summary>Tear the wall-less stage down once the test has finished with it, which it has at
        /// that point even when the car is left rolling: nothing downstream of this fixture reads the
        /// plane, and a 500 m collider sitting on y = 0 under the next stage is a stage in itself.</summary>
        private void DestroyOpenPlane()
        {
            if (_turnPlane == null) return;

            Object.Destroy(_turnPlane);
            _turnPlane = null;
            _turnContacts = null;
        }

        /// <summary>
        /// Bring the car back to the pose it was armed with, at rest, and let it find its ride
        /// height again: <see cref="VehicleController.ResetToSpawn"/> zeroes the velocities and the
        /// rack, and the settle covers the one fixed step the controller needs to notice it is
        /// standing still again before its speed is read a second time.
        /// </summary>
        private IEnumerator ResetCarOnGrid()
        {
            _car.SetTestControls(0f, 0f, 0f);
            _car.ResetToSpawn();
            yield return FixedSteps(ResetSettleSeconds);
        }

        // ---- fixed-time stepping -------------------------------------------------------------------

        /// <summary>
        /// Hold the run for <paramref name="seconds"/> of engine time, counted in fixed steps of
        /// <see cref="Time.fixedDeltaTime"/> — the same clock the physics of the car runs on, so
        /// the windows measured here do not move with the frame rate of the machine running them.
        /// </summary>
        private static IEnumerator FixedSteps(float seconds)
        {
            int steps = StepsFor(seconds);
            for (int step = 0; step < steps; step++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        /// <summary>How many fixed steps of real time <paramref name="seconds"/> is worth.</summary>
        private static int StepsFor(float seconds)
        {
            return Mathf.Max(1, Mathf.CeilToInt(seconds / Mathf.Max(0.005f, Time.fixedDeltaTime)));
        }

        private static float FixedDelta()
        {
            return Mathf.Max(0.005f, Time.fixedDeltaTime);
        }

        // ---- the metrics, taken off the body the way a driver would feel them -----------------------

        /// <summary>
        /// The angle between where the nose points and where the car is actually going, both
        /// flattened onto the road so pitch and roll are not read as slip. Reports false for a car
        /// that is not travelling: at a standstill the direction of travel is whatever way it last
        /// happened to be sliding, which is a number about nothing.
        /// </summary>
        private bool TrySideslipDegrees(out float sideslipDegrees)
        {
            sideslipDegrees = 0f;

            Vector3 travelled = Flatten(_car.PhysicsBody.linearVelocity);
            if (travelled.sqrMagnitude < 1e-6f || travelled.magnitude < SideslipMinimumSpeedMs)
            {
                return false;
            }

            Vector3 nose = Flatten(_car.transform.forward);
            if (nose.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            sideslipDegrees = Vector3.Angle(nose, travelled);
            return true;
        }

        /// <summary>
        /// Yaw rate in deg/s about the world up axis, which is the yaw of a car that has stayed
        /// level — the thing being watched for here. It is read off the body rather than off a
        /// difference of yaw angles, so it needs no unwrapping across the wrap.
        /// </summary>
        private float YawRateDegreesPerSecond()
        {
            return Mathf.Abs(_car.PhysicsBody.angularVelocity.y) * Mathf.Rad2Deg;
        }

        /// <summary>How far the car sits across the road from the centreline it was set down on.</summary>
        private float LateralOffsetM()
        {
            return Vector3.Dot(_car.transform.position - _gridPosition, _gridRight);
        }

        /// <summary>How far along the straight the car has come, measured from the grid.</summary>
        private float DistanceAlongGridM()
        {
            return Vector3.Dot(_car.transform.position - _gridPosition, _gridForward);
        }

        /// <summary>The rack rate the controller is working to, for the log lines to quote.</summary>
        private float SteeringRateDegPerSecond()
        {
            return _car.Tuning != null ? _car.Tuning.SteerRate : float.NaN;
        }

        /// <summary>
        /// The lock angle <see cref="VehicleController"/> should be holding at
        /// <paramref name="speedMs"/> given its speed-sensitive ladder — a second opinion on the
        /// measured angle, so a log line that disagrees with the tuning says which of the two moved.
        /// </summary>
        private float PredictedLockAngleDeg(float speedMs)
        {
            VehicleTuning tuning = _car.Tuning;
            if (tuning == null)
            {
                return float.NaN;
            }

            float rolled = Mathf.InverseLerp(0f, Mathf.Max(1f, tuning.HighSpeedSteerThreshold), speedMs);
            return Mathf.Lerp(tuning.MaxSteerAngle, tuning.HighSpeedSteerAngle, rolled);
        }

        private static Vector3 Flatten(Vector3 vector)
        {
            return Vector3.ProjectOnPlane(vector, Vector3.up);
        }

        /// <summary>The index of the first sample at or after <paramref name="seconds"/>, or -1.</summary>
        private static int FirstAtOrAfter(List<float> ascendingSeconds, float seconds)
        {
            for (int index = 0; index < ascendingSeconds.Count; index++)
            {
                if (ascendingSeconds[index] >= seconds - 1e-5f)
                {
                    return index;
                }
            }

            return -1;
        }

        // ---- driving ---------------------------------------------------------------------------------

        /// <summary>
        /// Bring the car up to <paramref name="targetSpeedMs"/> down the straight with the throttle
        /// and nothing else: <see cref="ApproachThrottle"/> is the only input, held until the speed
        /// the tyres have actually made reaches the target, and closed for
        /// <see cref="ApproachSettleSeconds"/> afterwards so that the speed measured downstream is
        /// the speed the approach delivered rather than one still climbing. What the car made, how
        /// long it took and where down the straight it happened are left in
        /// <paramref name="report"/>, so every measurement travels with its account.
        ///
        /// Nothing is written to the rigid body anywhere in this fixture, which on the untextured
        /// road the builder lays means an approach can fall short of a target or run wide into a
        /// wall — the report says which of the two it did, and the tests gate on the speed reported
        /// rather than on a speed that was handed to the body.
        /// </summary>
        private IEnumerator AccelerateToSpeed(float targetSpeedMs, ApproachReport report)
        {
            _car.SetTestControls(ApproachThrottle, 0f, 0f);

            int budget = StepsFor(ApproachTimeoutSeconds);
            int held = 0;
            float stopAtSpeedMs = targetSpeedMs - ApproachStopToleranceMs;
            while (held < budget && _car.CurrentSpeedMs < stopAtSpeedMs)
            {
                yield return new WaitForFixedUpdate();
                held++;
            }

            report.SecondsUnderThrottle = held * FixedDelta();
            report.ReachedTarget = _car.CurrentSpeedMs >= stopAtSpeedMs;

            _car.SetTestControls(0f, 0f, 0f);
            yield return FixedSteps(ApproachSettleSeconds);

            report.FinalSpeedMs = _car.CurrentSpeedMs;
            report.MetresAlongStraight = DistanceAlongGridM();
            report.LateralOffsetM = LateralOffsetM();
        }

        // ---- the four measurements -------------------------------------------------------------

        /// <summary>
        /// Criterion 1 — the rack has to ramp rather than snap. The car rolls along on light throttle,
        /// full lock is asked for in one go exactly as a keyboard is held, and the rack is sampled
        /// every fixed step afterwards. What is timed is the first step at which it passes
        /// <see cref="NinetyFraction"/> of the angle it ends up holding: a rack that arrives at full
        /// lock in a single step puts the nose round the moment the player commits, which is a slide
        /// before the corner has been read, and nothing else here would notice it.
        /// </summary>
        [UnityTest]
        public IEnumerator SteerInput_RampsNotInstant()
        {
            BuildTrackAndCarOnGrid();
            yield return FixedSteps(SettleSeconds);

            _car.SetTestControls(RestThrottle, 0f, 0f);
            yield return FixedSteps(RestSeconds);

            float entrySpeedMs = _car.CurrentSpeedMs;
            var seconds = new List<float>();
            var angles = new List<float>();

            _car.SetTestControls(RestThrottle, 0f, FullSteer);

            int steps = StepsFor(RampWindowSeconds);
            for (int step = 0; step < steps; step++)
            {
                yield return new WaitForFixedUpdate();
                seconds.Add((step + 1) * FixedDelta());
                angles.Add(Mathf.Abs(_car.CurrentSteerAngle));
            }

            float settledAngle = angles[steps - 1];
            float ninetySeconds = float.MaxValue;
            for (int sample = Mathf.Max(0, FirstAtOrAfter(seconds, RampSampleSeconds)); sample < steps; sample++)
            {
                if (angles[sample] >= NinetyFraction * settledAngle)
                {
                    ninetySeconds = seconds[sample];
                    break;
                }
            }

            string ninetyText = ninetySeconds > RampWindowSeconds
                ? $"never inside {RampWindowSeconds:F2} s"
                : $"at t={ninetySeconds:F3} s";
            Debug.Log($"[Handling] steer ramp: 90% of the {settledAngle:F1} deg the rack settles at came " +
                $"{ninetyText} off {entrySpeedMs:F1} m/s made on throttle alone; rack rate " +
                $"{SteeringRateDegPerSecond():F0} deg/s, ladder at " +
                $"{PredictedLockAngleDeg(_car.CurrentSpeedMs):F1} deg for {_car.CurrentSpeedMs:F1} m/s");

            Assert.GreaterOrEqual(settledAngle, SettledAngleFloorDegrees,
                $"the rack never wound on past {settledAngle:F1} deg, so there is no ramp here to time");
            Assert.GreaterOrEqual(ninetySeconds, MinimumNinetyPercentSeconds,
                $"full lock was there by t={ninetySeconds:F3} s, quicker than the " +
                $"{MinimumNinetyPercentSeconds:F2} s a driver needs between asking for a direction and " +
                "finding out what it did");

            yield return ResetCarOnGrid();
        }

        /// <summary>
        /// Criterion 2 — the lock that is left once the speed is up. Both halves are reached on the
        /// throttle, full lock is then wound on and held against <see cref="HoldThrottle"/> for
        /// <see cref="SteerHoldSeconds"/>, and the angle the rack is holding at the end of that is
        /// the settled lock at whatever speed the body is actually moving at. The speed belongs in
        /// the log because the ladder is a function of it, and the angle the tuning predicts at that
        /// same speed is logged beside the measurement to say which of the two moved.
        /// </summary>
        [UnityTest]
        public IEnumerator SteerAuthority_DropsWithSpeed()
        {
            BuildTrackAndCarOnGrid();
            yield return FixedSteps(SettleSeconds);

            var lowApproach = new ApproachReport();
            yield return WindOnLockAtSpeed(LowLockSpeedMs, lowApproach);
            float lowSpeedAngle = _settledLockAngleDegrees;
            float lowSpeedMs = _settledLockSpeedMs;
            Assert.LessOrEqual(Mathf.Abs(lowApproach.FinalSpeedMs - LowLockSpeedMs), SpeedToleranceMs,
                $"the low half never got near {LowLockSpeedMs:F0} m/s on the throttle: " +
                $"{lowApproach.FinalSpeedMs:F1} m/s after {lowApproach.SecondsUnderThrottle:F2} s, " +
                $"{lowApproach.MetresAlongStraight:F0} m down the straight");

            yield return ResetCarOnGrid();

            var highApproach = new ApproachReport();
            yield return WindOnLockAtSpeed(HighLockSpeedMs, highApproach);
            float highSpeedAngle = _settledLockAngleDegrees;
            float highSpeedMs = _settledLockSpeedMs;
            Assert.LessOrEqual(Mathf.Abs(highApproach.FinalSpeedMs - HighLockSpeedMs), SpeedToleranceMs,
                $"the high half never got near {HighLockSpeedMs:F0} m/s on the throttle: " +
                $"{highApproach.FinalSpeedMs:F1} m/s after {highApproach.SecondsUnderThrottle:F2} s, " +
                $"{highApproach.MetresAlongStraight:F0} m down the straight");

            float ratio = highSpeedAngle / Mathf.Max(0.001f, lowSpeedAngle);
            Debug.Log($"[Handling] steer authority: {lowSpeedAngle:F1} deg settled at {lowSpeedMs:F1} m/s " +
                $"against {highSpeedAngle:F1} deg at {highSpeedMs:F1} m/s = ratio {ratio:F2}; ladder says " +
                $"{PredictedLockAngleDeg(lowSpeedMs):F1} vs {PredictedLockAngleDeg(highSpeedMs):F1} deg; " +
                $"approaches {lowApproach.FinalSpeedMs:F1}/{highApproach.FinalSpeedMs:F1} m/s, " +
                $"{highApproach.MetresAlongStraight:F0} m down the straight, {highApproach.LateralOffsetM:F1} m off line");

            yield return ResetCarOnGrid();

            Assert.GreaterOrEqual(lowSpeedAngle, SettledAngleFloorDegrees,
                $"only {lowSpeedAngle:F1} deg settled at {lowSpeedMs:F1} m/s — too little lock for a ratio " +
                "against it to mean anything");
            Assert.LessOrEqual(ratio, MaxHighToLowLockRatio,
                $"{highSpeedAngle:F1} deg left at {highSpeedMs:F1} m/s against {lowSpeedAngle:F1} deg at " +
                $"{lowSpeedMs:F1} m/s is ratio {ratio:F2}: the ladder is not pulling the lock back as far as " +
                $"the {MaxHighToLowLockRatio:F2} this car is meant to feel like");
        }

        /// <summary>
        /// Criterion 3 — the sustained keyboard turn, which is the mess a player actually gets
        /// themselves into: lock and half throttle held from <see cref="TurnEntrySpeedMs"/> m/s for
        /// <see cref="TurnSeconds"/> s, judging the widest sideslip anywhere in the window and the
        /// contact counter saying that nothing but the tarmac was touched while it was measured.
        /// Samples the car is too slow for are dropped rather than read as driving straight.
        ///
        /// The stage is the wall-less plane of <see cref="BuildOpenPlaneForTurn"/> rather than the
        /// circuit, because the circuit has no room for this manoeuvre and the numbers it gave back
        /// were the wall's. What the diagnostics fixture logged (<c>[Diag]</c> lines,
        /// <c>HandlingDiagnosticsPlayTests</c>) on the track: the car reached <c>Wall_Outer_004</c> at
        /// t = 0.8 s of the turn and came down from 22 m/s to 6.6 m/s in the contact, and the
        /// 85-89 deg of "sideslip" this test used to report was a body swinging round a wall, not a car
        /// sliding. The identical input on a bare plane peaked at 4.5 deg with no contacts at all. The
        /// arithmetic agrees with the log: full lock at <see cref="TurnEntrySpeedMs"/> m/s holds the car
        /// to a radius of some 30 m, so <see cref="TurnSeconds"/> of it wants roughly 40 m either side
        /// of the grid, and the walls stand <see cref="TrackBuilder.WallOffset"/> from the centreline.
        /// A turn this hard this fast has to be judged where it can actually be driven, so the wall is
        /// out of the stage and the contact counter is what keeps "no wall" honest.
        /// </summary>
        [UnityTest]
        public IEnumerator SustainedKeyboardTurn_DoesNotSpin()
        {
            BuildOpenPlaneForTurn();
            yield return FixedSteps(SettleSeconds);

            var approach = new ApproachReport();
            yield return AccelerateToSpeed(TurnEntrySpeedMs, approach);
            Assert.LessOrEqual(Mathf.Abs(approach.FinalSpeedMs - TurnEntrySpeedMs), SpeedToleranceMs,
                $"never made the {TurnEntrySpeedMs:F0} m/s the turn is entered at: {approach.FinalSpeedMs:F1} m/s " +
                $"after {approach.SecondsUnderThrottle:F2} s of throttle " +
                $"{(approach.ReachedTarget ? "then bled off" : "still short when the timeout ran out")}, " +
                $"{approach.MetresAlongStraight:F0} m along the plane");

            _turnContacts.StartWindow();
            yield return HoldTurnForMetrics(TurnThrottle, FullSteer, false, TurnSeconds);

            Debug.Log($"[Handling] sustained turn: worst sideslip {_worstSideslipDegrees:F1} deg over " +
                $"{_sideslipSamples}/{_sideslipWindowSamples} samples off {approach.FinalSpeedMs:F1} m/s, peak yaw " +
                $"{_peakYawRateDegreesPerSecond:F0} deg/s on an open plane with nothing to hit, " +
                $"{approach.MetresAlongStraight:F0} m along and {approach.LateralOffsetM:F1} m off the line the " +
                $"grid was drawn on; {_turnContacts.Describe()}");

            Assert.Greater(_sideslipSamples, 0,
                "the car was never travelling fast enough to have a sideslip worth the name");
            Assert.Less(_worstSideslipDegrees, MaximumSideslipDegrees,
                $"the nose came {_worstSideslipDegrees:F1} deg off where the car was going — held for " +
                $"{TurnSeconds:F1} s, that is a spin rather than a turn");
            Assert.AreEqual(0, _turnContacts.NonGroundContacts,
                $"the body came into something other than the tarmac during the turn — " +
                $"{_turnContacts.Describe()}. There is no wall on this stage for a sideslip to be blamed on, " +
                "which is the whole point of it: a slip angle taken through a contact is a crash report " +
                "rather than a reading of the tyres, as the Wall_Outer_004 line of the summary above says.");

            yield return ResetCarOnGrid();
            DestroyOpenPlane();
        }

        /// <summary>
        /// Criterion 4 — the handbrake still rotates the car. One run is driven twice from the same
        /// speed, with the same steering and the same throttle, once with the rear axle locked and
        /// once without, and the widest sideslip of each is compared against the other: the handbrake
        /// earns its binding by putting the rear end out, and if it cannot manage that here it does
        /// nothing beyond slowing the car on the way to wherever it was already going.
        /// </summary>
        [UnityTest]
        public IEnumerator Handbrake_StillRotatesCar()
        {
            BuildTrackAndCarOnGrid();
            yield return FixedSteps(SettleSeconds);

            var withoutApproach = new ApproachReport();
            yield return AccelerateToSpeed(SlideEntrySpeedMs, withoutApproach);
            yield return HoldTurnForMetrics(SlideThrottle, SlideSteer, false, SlideSeconds);
            float withoutHandbrake = _worstSideslipDegrees;
            float withoutSpeedMs = withoutApproach.FinalSpeedMs;
            float withoutYaw = _peakYawRateDegreesPerSecond;

            yield return ResetCarOnGrid();

            var withApproach = new ApproachReport();
            yield return AccelerateToSpeed(SlideEntrySpeedMs, withApproach);
            yield return HoldTurnForMetrics(SlideThrottle, SlideSteer, true, SlideSeconds);
            float withHandbrake = _worstSideslipDegrees;

            Debug.Log($"[Handling] handbrake: {withoutHandbrake:F1} deg of sideslip entered at " +
                $"{withoutSpeedMs:F1} m/s without it against {withHandbrake:F1} deg entered at " +
                $"{withApproach.FinalSpeedMs:F1} m/s with it, at {SlideSteer:F1} steering and " +
                $"{SlideThrottle:F1} throttle for {SlideSeconds:F1} s; peak yaw {withoutYaw:F0} then " +
                $"{_peakYawRateDegreesPerSecond:F0} deg/s, {SlideSeconds:F1} s of throttle-only apart");

            yield return ResetCarOnGrid();

            Assert.Greater(withHandbrake, withoutHandbrake,
                $"locking the rear axle for {SlideSeconds:F1} s at {SlideSteer:F1} steering gave " +
                $"{withHandbrake:F1} deg of sideslip against {withoutHandbrake:F1} deg without it, so the " +
                "handbrake never gets the back out");
        }

        // ---- what the four measurements watch --------------------------------------------------

        /// <summary>Readings left behind by <see cref="WindOnLockAtSpeed"/> and
        /// <see cref="HoldTurnForMetrics"/>, which is how a routine that has to be a coroutine hands
        /// more than one number back to the test that ran it.</summary>
        private float _settledLockAngleDegrees;
        private float _settledLockSpeedMs;
        private float _worstSideslipDegrees;
        private float _peakYawRateDegreesPerSecond;
        private int _sideslipSamples;
        private int _sideslipWindowSamples;

        /// <summary>
        /// Reach <paramref name="targetSpeedMs"/> on the throttle, then wind full lock on against
        /// <see cref="HoldThrottle"/> and hold it for <see cref="SteerHoldSeconds"/>, leaving the
        /// angle the rack has come to rest at and the speed it was resting at in
        /// <see cref="_settledLockAngleDegrees"/> and <see cref="_settledLockSpeedMs"/>. The throttle
        /// stays open because a lock measured as the speed slid away underneath it is nobody's
        /// settled lock, and the speed is kept alongside it because the ladder is a function of it.
        /// </summary>
        private IEnumerator WindOnLockAtSpeed(float targetSpeedMs, ApproachReport report)
        {
            yield return AccelerateToSpeed(targetSpeedMs, report);

            _car.SetTestControls(HoldThrottle, 0f, FullSteer);
            yield return FixedSteps(SteerHoldSeconds);

            _settledLockAngleDegrees = Mathf.Abs(_car.CurrentSteerAngle);
            _settledLockSpeedMs = _car.CurrentSpeedMs;
        }

        /// <summary>
        /// Hold <paramref name="steer"/> against <paramref name="throttle"/> for
        /// <paramref name="seconds"/> of fixed time, handbrake on or off, and watch the body come
        /// round: the widest sideslip and the peak yaw rate over the window are left in
        /// <see cref="_worstSideslipDegrees"/> and <see cref="_peakYawRateDegreesPerSecond"/>. A
        /// sample too slow to have a direction of travel is dropped rather than counted as driving
        /// straight, and <see cref="_sideslipSamples"/> says how much of the window was measured.
        /// </summary>
        private IEnumerator HoldTurnForMetrics(float throttle, float steer, bool handbrake, float seconds)
        {
            _worstSideslipDegrees = 0f;
            _peakYawRateDegreesPerSecond = 0f;
            _sideslipSamples = 0;

            _car.SetTestControls(throttle, 0f, steer, handbrake);

            _sideslipWindowSamples = StepsFor(seconds);
            for (int step = 0; step < _sideslipWindowSamples; step++)
            {
                yield return new WaitForFixedUpdate();

                _peakYawRateDegreesPerSecond = Mathf.Max(_peakYawRateDegreesPerSecond, YawRateDegreesPerSecond());
                if (TrySideslipDegrees(out float sideslip))
                {
                    _sideslipSamples++;
                    _worstSideslipDegrees = Mathf.Max(_worstSideslipDegrees, sideslip);
                }
            }

            _car.SetTestControls(0f, 0f, 0f);
        }

        /// <summary>
        /// What an <see cref="AccelerateToSpeed"/> run actually did, so that a number measured after
        /// an approach travels with the account of how its speed was made — all of it throttle and
        /// time, none of it written to the rigid body.
        /// </summary>
        private sealed class ApproachReport
        {
            // Fixed time the throttle was held and whether it got there, with the speed, the distance
            // along the straight and the offset across it the car had when the throttle was closed.
            public float SecondsUnderThrottle;
            public bool ReachedTarget;
            public float FinalSpeedMs;
            public float MetresAlongStraight;
            public float LateralOffsetM;
        }
    }

    /// <summary>
    /// A tiny collision counter for <see cref="HandlingMetricsPlayTests.SustainedKeyboardTurn_DoesNotSpin"/>:
    /// it counts what the car comes into over the window it is armed for, sorted by whether it was the
    /// ground plane the stage is laid on or something else. Enter and stay are counted together, so a body
    /// scraped along something is not read as one tap. A <see cref="WheelCollider"/> appears in neither
    /// count — Unity reports wheel contacts only with <c>provideEvents</c> on — so what lands here is the
    /// chassis <see cref="BoxCollider"/> itself: the thing <c>Wall_Outer_004</c> was hit with on the
    /// circuit, and the thing that arrives on the tarmac when a suspension has been run out of travel.
    /// Contacts with the ground are logged for the record and no gate turns on them; anything else fails.
    /// </summary>
    public sealed class NonGroundContactCounter : MonoBehaviour
    {
        private Collider _ground;

        /// <summary>Contacts with anything that is neither the ground plane nor a collider the car
        /// carries itself. <see cref="StartWindow"/> zeroes both counts.</summary>
        public int NonGroundContacts { get; private set; }

        /// <summary>The same for the ground plane, which the car is expected to be standing on: a chassis
        /// that bottoms out is worth reading in the log, but it is not a foreign collider.</summary>
        public int GroundContacts { get; private set; }

        /// <summary>Name of the first thing hit outside the ground, "none" until one is hit.</summary>
        public string FirstNonGroundCollider { get; private set; } = "none";

        /// <summary>When that first one landed, "none" until there is one to date.</summary>
        public float FirstNonGroundTimeSeconds { get; private set; } = float.NaN;

        /// <summary>Name the surface the car is allowed to touch; every other collider is a contact.</summary>
        public void Arm(GameObject ground)
        {
            _ground = ground != null ? ground.GetComponent<Collider>() : null;
        }

        /// <summary>Forget whatever the approach and the settle did: only the window between this and the
        /// assertions is what the test is judging.</summary>
        public void StartWindow()
        {
            NonGroundContacts = 0;
            GroundContacts = 0;
            FirstNonGroundCollider = "none";
            FirstNonGroundTimeSeconds = float.NaN;
        }

        private void OnCollisionEnter(Collision collision) { Note(collision); }

        private void OnCollisionStay(Collision collision) { Note(collision); }

        /// <summary>The one line for the log and for the assertion message: what was touched, and when.</summary>
        public string Describe()
        {
            if (NonGroundContacts == 0 && GroundContacts == 0)
            {
                return "no contacts with anything at all";
            }

            string first = float.IsNaN(FirstNonGroundTimeSeconds)
                ? "nothing outside the ground plane"
                : $"{FirstNonGroundCollider} at t={FirstNonGroundTimeSeconds:F2} s";
            return $"{NonGroundContacts} contacts outside the ground plane (first: {first}), " +
                $"{GroundContacts} with the ground plane";
        }

        /// <summary>One contact, if it was with something outside the car — the four corners and the
        /// chassis being the car rather than anything it can collide with.</summary>
        private void Note(Collision collision)
        {
            Collider other = collision.collider;
            if (other == null) return;

            if (_ground != null &&
                (other == _ground || other.transform.IsChildOf(_ground.transform)))
            {
                GroundContacts++;
                return;
            }

            Transform hit = other.transform;
            if (hit == transform || hit.IsChildOf(transform) || transform.IsChildOf(hit)) return;

            NonGroundContacts++;
            if (!float.IsNaN(FirstNonGroundTimeSeconds)) return;

            FirstNonGroundTimeSeconds = Time.time;
            FirstNonGroundCollider = other.name;
        }
    }
}



