using System.Collections;
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
    /// The Stage 3 runtime pieces assembled by hand in the test runner's own scene: the
    /// primitive circuit, the car dropped onto its spawn pose, and the session that watches
    /// both. No scene file is loaded, so nothing here depends on the build settings —
    /// <c>Track_Proto</c> is not in them, which is also why
    /// <see cref="RaceBootstrap"/> keys off the scene name rather than being present in the
    /// hierarchy (and why the bootstrap itself is not exercised here: it would refuse to run
    /// in this scene, correctly).
    ///
    /// What this pins down is only what play mode can show: that the built circuit has a
    /// surface the car actually stops on rather than falling through, that checkpoints
    /// report the crossings a <see cref="LapCounter"/> is fed, and that a car which leaves
    /// the world is returned to the pose it was armed with — the last valid checkpoint, not
    /// the grid.
    ///
    /// No <c>UnityEngine.InputSystem</c> reads anywhere: the car is moved by writing its
    /// rigid body, so the tests do not depend on which devices happen to be plugged into
    /// the machine running them.
    /// </summary>
    [TestFixture]
    public sealed class RaceBootstrapPlayTests
    {
        /// <summary>Time the car is left alone to find its ride height.</summary>
        private const float SettleSeconds = 1f;

        /// <summary>Forward speed the crossing test drives the line at, m/s.</summary>
        private const float CrossingSpeedMs = 22f;

        /// <summary>How long to wait for the trigger to report a crossing before failing.</summary>
        private const float CrossingTimeoutSeconds = 4f;

        /// <summary>Height the auto-reset test parks the car at, well under the fall threshold.</summary>
        private const float OutOfWorldY = -20f;

        /// <summary>How close the car has to be to its armed pose for a reset to count.</summary>
        private const float ResetToleranceM = 1.5f;

        /// <summary>Grid slot is this far short of the line; the re-arm check expects more than half of it.</summary>
        private const float GridToLineToleranceM = 1f;

        // ---- stage 4: throttle, steering, gating and the pause panel ---------------------

        /// <summary>Real play time each driving test holds its test controls on for.</summary>
        private const float DriveSeconds = 2f;

        /// <summary>Speed the gates are swept through at in the checkpoint tests, m/s.</summary>
        private const float CheckpointDriveSpeedMs = 10f;

        /// <summary>
        /// How far short of a gate the car is set down before it is driven through: the
        /// trigger is 2 m deep and the chassis 3.7 m long, so three metres back keeps every
        /// collider of the car clear of the volume until the sweep begins.
        /// </summary>
        private const float CheckpointApproachM = 3f;

        /// <summary>Longest a single gate sweep runs before the test is left to fail, in seconds.</summary>
        private const float CheckpointTimeoutSeconds = 1.2f;

        /// <summary>Yaw the car has to build for the front end to count as having turned.</summary>
        private const float MinimumTurnDegrees = 10f;

        /// <summary>How far the car may travel, in metres, while the clock is stopped.</summary>
        private const float FrozenToleranceM = 0.05f;

        /// <summary>Tolerance <see cref="Time.timeScale"/> is compared at; it is never fractional here.</summary>
        private const float TimeScaleTolerance = 1e-4f;

        private GameObject _trackRoot;
        private TrackBuilder _track;
        private GameObject _carObject;
        private VehicleController _car;
        private GameObject _sessionRoot;
        private RaceSession _session;

        [SetUp]
        public void SetUp()
        {
            // Everything here is timed by the engine's own clock — WaitForSeconds, the
            // fixed steps that move the car, the three-frame grace the session takes to
            // notice it has fallen. A fixture that stopped the clock (or paused through
            // GamePause, which is a static) would otherwise have these tests wait for
            // seconds that never arrive, and report a timeout instead of the truth.
            Time.timeScale = 1f;

            // These tests drive the car from the first frame, so they explicitly skip the race countdown.
            RaceSession.CountdownOverrideSeconds = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            RaceSession.CountdownOverrideSeconds = null;
            // Deferred destruction is what we want: the builder frees the materials it
            // created in its own OnDestroy, and everything is gone before the next test
            // builds its own copy of the circuit on top of this one.
            if (_sessionRoot != null) Object.Destroy(_sessionRoot);
            if (_carObject != null) Object.Destroy(_carObject);
            if (_trackRoot != null) Object.Destroy(_trackRoot);

            _sessionRoot = null;
            _session = null;
            _carObject = null;
            _car = null;
            _trackRoot = null;
            _track = null;
        }

        /// <summary>
        /// Circuit plus car on the grid, exactly as <see cref="RaceBootstrap"/> makes them:
        /// build first, then place transform and rigid body, then arm the reset pose.
        /// </summary>
        private void BuildTrackAndCarOnGrid()
        {
            _trackRoot = new GameObject("TrackHarness");
            _track = _trackRoot.AddComponent<TrackBuilder>();

            // AddComponent ran Awake, which builds on awake by default; the explicit call
            // is the same fallback the bootstrap uses.
            if (!_track.HasBuilt)
            {
                _track.Build();
            }

            Assert.IsTrue(_track.HasBuilt, "builder did not build when asked");
            Assert.AreEqual(TrackLayout.DefaultCheckpointCount, _track.Checkpoints.Length,
                "the prototype circuit is the line plus three checkpoints");

            _carObject = VehicleController.BuildCar(null);
            Pose spawn = _track.Spawn;
            _carObject.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

            _car = _carObject.GetComponent<VehicleController>();
            Assert.IsNotNull(_car.PhysicsBody, "BuildCar must deliver an assembled physics body");
            _car.PhysicsBody.position = spawn.position;
            _car.PhysicsBody.rotation = spawn.rotation;
            _car.ArmSpawnPoint();
        }

        private void StartSession()
        {
            _sessionRoot = new GameObject("SessionHarness");
            _session = _sessionRoot.AddComponent<RaceSession>();
            _session.Init(_track, _car);

            Assert.IsNotNull(_session.Counter, "Init must produce a counter");
            Assert.AreEqual(_track.Checkpoints.Length, _session.Counter.CheckpointCount,
                "the counter is sized to the checkpoints on the track");
        }

        /// <summary>
        /// Hold the car's forward speed while leaving its vertical velocity alone — write
        /// the whole vector and the car stops falling, so it never reaches its ride height
        /// and the suspension tells a different story to the one being tested.
        /// </summary>
        private void DriveForward(float speedMs)
        {
            Rigidbody body = _car.PhysicsBody;
            Vector3 forward = _car.transform.forward;
            Vector3 velocity = body.linearVelocity;
            velocity.x = forward.x * speedMs;
            velocity.z = forward.z * speedMs;
            body.linearVelocity = velocity;
        }

        /// <summary>Drop the car under the floor of the world; transform and body together.</summary>
        private void TeleportOutOfWorld()
        {
            Vector3 below = _car.transform.position;
            below.y = OutOfWorldY;

            _car.transform.position = below;
            _car.PhysicsBody.position = below;
            _car.PhysicsBody.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Set the car down on the racing line just short of <paramref name="checkpoint"/>,
        /// facing the driving direction, and sweep it through the trigger at a fixed speed
        /// — forwards, or backwards, which is the whole difference the
        /// <see cref="LapCounter"/> cares about. Requires a session to have been started:
        /// the sweep ends as soon as that session's counter reacts to it.
        ///
        /// Body and transform are written together and the velocity is re-asserted every
        /// fixed step, so neither rolling drag nor the engine braking the controller applies
        /// to a car with no throttle can coast it short of the volume. A sweep that never
        /// registers simply runs out <see cref="CheckpointTimeoutSeconds"/> of fixed time
        /// and leaves the calling test to fail on the counter, which is the thing under
        /// test, rather than on a timeout.
        ///
        /// A teleported car never reports <c>OnTriggerExit</c> for the gate it vanished
        /// from, and <see cref="TrackCheckpoint"/> clears the latch it keeps per body
        /// whenever it is switched off for exactly that reason — so the gate about to be
        /// approached is switched off and on again first, which is the only honest way to
        /// be sure the crossing made now is the one that gets reported.
        /// </summary>
        private IEnumerator DriveThroughCheckpoint(TrackCheckpoint checkpoint, bool forwards)
        {
            Transform gate = checkpoint.transform;
            Vector3 velocity = (forwards ? gate.forward : -gate.forward) * CheckpointDriveSpeedMs;

            GameObject volume = checkpoint.gameObject;
            volume.SetActive(false);
            volume.SetActive(true);

            // The centreline lies at y = 0, so the car is set down at the height it is
            // built to be dropped from rather than sunk into the road or dropped onto it.
            Vector3 entry = gate.position - velocity.normalized * CheckpointApproachM;
            entry.y = gate.position.y + TrackLayout.SpawnHeight;

            Rigidbody body = _car.PhysicsBody;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = entry;
            body.rotation = gate.rotation;
            _car.transform.SetPositionAndRotation(entry, gate.rotation);
            Physics.SyncTransforms();

            LapCounter counter = _session.Counter;
            int laps = counter.CompletedLaps;
            int last = counter.LastValidCheckpoint;
            bool running = counter.IsRunning;

            int steps = Mathf.CeilToInt(CheckpointTimeoutSeconds / Mathf.Max(0.005f, Time.fixedDeltaTime));
            for (int step = 0; step < steps; step++)
            {
                body.linearVelocity = velocity;
                yield return new WaitForFixedUpdate();

                if (counter.IsRunning != running
                    || counter.LastValidCheckpoint != last
                    || counter.CompletedLaps != laps)
                {
                    yield break; // The gate reported; line the car up for the next one.
                }
            }
        }

        /// <summary>
        /// The circuit is a driving surface, not a suggestion: a car released on the spawn
        /// pose has to end up resting on it. The lower bound is the one that matters — the
        /// ground plane is the only collider under the road, so a builder that lost it would
        /// show up here as a car on its way to the bottom of the world.
        /// </summary>
        [UnityTest]
        public IEnumerator CarReleasedOnSpawnPose_RestOnTheRoad()
        {
            BuildTrackAndCarOnGrid();

            Pose spawn = _track.Spawn;
            Assert.AreEqual(spawn.position.x, _car.transform.position.x, 1e-2f, "not on the grid");
            Assert.AreEqual(spawn.position.z, _car.transform.position.z, 1e-2f, "not on the grid");

            yield return new WaitForSeconds(SettleSeconds);

            Vector3 position = _car.transform.position;
            Assert.IsFalse(float.IsNaN(position.y), "car position went NaN");
            Assert.Greater(position.y, 0f, "the car fell through the road surface at y = 0");
            Assert.Less(position.y, 2f, "the car was thrown clear above the road");
            Assert.Less(_car.PhysicsBody.linearVelocity.magnitude, 5f,
                "the car is still moving after settling on the road");
        }

        /// <summary>
        /// Criterion 7's automatic half: a car that leaves the world comes back by itself,
        /// on the pose it was armed with and with its velocity gone.
        /// </summary>
        [UnityTest]
        public IEnumerator RaceSession_CarFallsOutOfTheWorld_GetsPutBackOnItsSpawnPose()
        {
            BuildTrackAndCarOnGrid();
            StartSession();

            yield return new WaitForSeconds(0.5f); // Resting on the road, so the reset is obvious.

            Vector3 spawn = _track.Spawn.position;
            TeleportOutOfWorld();

            // Three frames: one for the session to notice, two more so physics has caught
            // the transform up and the car has barely started falling from the reset pose.
            yield return null;
            yield return null;
            yield return null;

            float distance = Vector3.Distance(_car.transform.position, spawn);
            Assert.Less(distance, ResetToleranceM,
                $"session did not reset the car to its spawn pose (off by {distance:F2} m)");
            Assert.Less(_car.PhysicsBody.linearVelocity.magnitude, 5f,
                "reset left the car moving");
        }

        /// <summary>
        /// The wiring between a trigger volume and the lap maths: driving the line forwards
        /// starts the counter — and moves the pose a reset returns to, which is the part
        /// that makes a mid-lap recovery a recovery rather than a lap penalty.
        ///
        /// The <see cref="TrackCheckpoint.Crossed"/> event is raised by physics, so the
        /// crossing is simulated by driving through the trigger rather than by invoking the
        /// event by hand; that is also the only way to catch a checkpoint that stopped
        /// reporting, which is exactly what the latch in it can break.
        /// </summary>
        [UnityTest]
        public IEnumerator RaceSession_CrossingTheLine_StartsTheCounterAndMovesTheResetPose()
        {
            BuildTrackAndCarOnGrid();
            StartSession();

            float gridZ = _track.Spawn.position.z;
            Assert.IsFalse(_session.Counter.IsRunning, "the lap starts before the line is crossed");
            Assert.AreEqual(LapCounter.NoCheckpoint, _session.Counter.LastValidCheckpoint);

            DriveForward(CrossingSpeedMs);

            float remaining = CrossingTimeoutSeconds;
            while (!_session.Counter.IsRunning && remaining > 0f)
            {
                yield return null;
                remaining -= Time.deltaTime;
                DriveForward(CrossingSpeedMs); // Rolling drag would otherwise coast it short.
            }

            Assert.IsTrue(_session.Counter.IsRunning, "the line never reported a forward crossing");
            Assert.AreEqual(0, _session.Counter.LastValidCheckpoint, "index 0 is the line");
            Assert.AreEqual(1, _session.Counter.CurrentLap, "crossing the line opens lap 1");
            Assert.AreEqual(0, _session.Counter.CompletedLaps, "the first sweep of the line scores nothing");

            TeleportOutOfWorld();
            yield return null;
            yield return null;

            Assert.Greater(_car.transform.position.z, gridZ + GridToLineToleranceM,
                "the reset went back to the grid instead of the checkpoint the car had banked");
        }

        /// <summary>
        /// The throttle is a force at the tyres rather than a rewrite of the velocity: two
        /// seconds of full throttle with nothing else applied has to leave the grid under
        /// the car's own power. Both halves matter — spinning wheels or a car sliding
        /// sideways would show speed without distance along the axis it was pointed at,
        /// and a car that only moves because something wrote its rigid body would show the
        /// reverse. Nothing here touches the keyboard, so the run does not depend on which
        /// devices happen to be plugged into the machine running it.
        /// </summary>
        [UnityTest]
        public IEnumerator Throttle_MovesCarForward()
        {
            BuildTrackAndCarOnGrid();
            yield return new WaitForSeconds(SettleSeconds);

            Vector3 start = _car.transform.position;
            Vector3 forward = _car.transform.forward;

            _car.SetTestControls(1f, 0f, 0f);
            yield return new WaitForSeconds(DriveSeconds);

            Assert.Greater(_car.CurrentSpeedMs, 5f,
                $"two seconds at full throttle only got the car to {_car.CurrentSpeedMs:F2} m/s");

            float travelled = Vector3.Dot(_car.transform.position - start, forward);
            Assert.Greater(travelled, 5f,
                $"the car only moved {travelled:F2} m along the axis it started out pointing at");

            Vector3 position = _car.transform.position;
            Assert.Greater(position.y, 0f, "full throttle put the car through the road");
            Assert.Less(position.y, 2f, "full throttle launched the car off the road");
        }

        /// <summary>
        /// Steering that only works for the editor's referee is no steering at all: with
        /// the throttle open and the wheel held to full lock, the nose has to come off the
        /// racing line by a visible angle inside two seconds. The widest yaw reached during
        /// the run is measured rather than the pose at the end — the road is 12 m wide with
        /// its inside wall 6.5 m off the centreline, so a car turning this hard for this
        /// long finishes the run against it, and a bounce off the wall is no evidence that
        /// the front wheels never turned.
        /// </summary>
        [UnityTest]
        public IEnumerator Steering_TurnsCar()
        {
            BuildTrackAndCarOnGrid();
            yield return new WaitForSeconds(SettleSeconds);

            float initialYaw = _car.transform.rotation.eulerAngles.y;
            float widest = 0f;

            _car.SetTestControls(0.6f, 0f, 1f);

            float elapsed = 0f;
            while (elapsed < DriveSeconds)
            {
                yield return null;
                elapsed += Time.deltaTime;

                float yaw = _car.transform.rotation.eulerAngles.y;
                widest = Mathf.Max(widest, Mathf.Abs(Mathf.DeltaAngle(initialYaw, yaw)));
            }

            Assert.Greater(widest, MinimumTurnDegrees,
                $"full lock for two seconds never turned the car further than {widest:F1} degrees");
        }

        /// <summary>
        /// Criterion 5 end to end: every gate of the built circuit is crossed by driving
        /// through its trigger — never by invoking anything by hand — in the order the
        /// <see cref="LapCounter"/> asks for, and the line scores the lap on the second
        /// visit. The car is set down ahead of each gate instead of driving the 777 m
        /// between them: the shape of that drive is <see cref="TrackLayout"/>'s business
        /// and is already pinned in EditMode, while what can only be shown in play mode is
        /// the wiring between a trigger volume, the direction it reports, and the counter.
        /// </summary>
        [UnityTest]
        public IEnumerator Checkpoints_InOrderForward_CountALap()
        {
            BuildTrackAndCarOnGrid();
            StartSession();
            yield return new WaitForSeconds(SettleSeconds);

            Assert.AreEqual(0, _session.Counter.CompletedLaps, "a lap was scored out of nothing");

            yield return DriveThroughCheckpoint(_track.Checkpoints[0], forwards: true);
            Assert.IsTrue(_session.Counter.IsRunning, "the line never started the counter");
            Assert.AreEqual(0, _session.Counter.CompletedLaps, "the first sweep of the line scored a lap");

            for (int index = 1; index < _track.Checkpoints.Length; index++)
            {
                yield return DriveThroughCheckpoint(_track.Checkpoints[index], forwards: true);
                Assert.AreEqual(index, _session.Counter.LastValidCheckpoint,
                    $"gate {index} was not banked after gate {index - 1}");
                Assert.AreEqual(0, _session.Counter.CompletedLaps,
                    $"gate {index} completed a lap before the line was reached");
            }

            // Every gate banked: this crossing is the lap.
            yield return DriveThroughCheckpoint(_track.Checkpoints[0], forwards: true);

            Assert.AreEqual(1, _session.Counter.CompletedLaps,
                "every gate in order and back over the line did not score a lap");
            Assert.AreEqual(2, _session.Counter.CurrentLap, "the completed lap did not open the next one");
            Assert.Greater(_session.Counter.LastLapTime, 0f, "the completed lap was never timed");
        }

        /// <summary>
        /// The other half of criterion 5: a reversed sweep of the lap must not score. It
        /// is the half that indices alone cannot answer, because the second half of a
        /// reversed lap lands exactly on the gate the counter is waiting for — here the
        /// counter sits on the line waiting for gate 1, and gate 1 is then crossed going
        /// the wrong way. Only the direction the trigger reports tells that apart from a
        /// car that really has made progress, which is why the sweep is driven through
        /// the volumes rather than fed to the counter.
        /// </summary>
        [UnityTest]
        public IEnumerator Checkpoints_Backwards_DoNotCount()
        {
            BuildTrackAndCarOnGrid();
            StartSession();
            yield return new WaitForSeconds(SettleSeconds);

            // Started properly first: a counter that has never seen the line would refuse
            // the reversed lap for the wrong reason.
            yield return DriveThroughCheckpoint(_track.Checkpoints[0], forwards: true);
            Assert.IsTrue(_session.Counter.IsRunning, "the session never started");
            Assert.AreEqual(0, _session.Counter.LastValidCheckpoint, "the line was never banked");

            for (int index = _track.Checkpoints.Length - 1; index >= 1; index--)
            {
                yield return DriveThroughCheckpoint(_track.Checkpoints[index], forwards: false);
                Assert.AreEqual(0, _session.Counter.LastValidCheckpoint,
                    $"crossing gate {index} backwards moved the counter on from the line");
                Assert.AreEqual(0, _session.Counter.CompletedLaps,
                    $"crossing gate {index} backwards scored something");
            }

            yield return DriveThroughCheckpoint(_track.Checkpoints[0], forwards: false);

            Assert.AreEqual(0, _session.Counter.CompletedLaps, "a reversed lap round the circuit scored");
            Assert.IsTrue(_session.Counter.IsRunning, "a reversed crossing of the line stopped the session");
            Assert.AreEqual(1, _session.Counter.CurrentLap, "the reversed lap advanced the lap in progress");
        }

        /// <summary>
        /// The pause is <c>Time.timeScale</c> = 0 (PLAN.md Stage 4), which stops the car's
        /// own FixedUpdate, the session's lap clock and every scaled waiter in the game —
        /// so this test can only advance with plain frame yields while it is holding the
        /// pause, and it hands the clock back in a <c>finally</c> rather than in
        /// <see cref="TearDown"/>: the fixture's tear down knows nothing about the pause,
        /// and a static left at zero would leave every later test running at a standstill.
        /// </summary>
        [UnityTest]
        public IEnumerator Pause_StopsTimeAndResumes()
        {
            BuildTrackAndCarOnGrid();
            yield return new WaitForSeconds(SettleSeconds);

            try
            {
                _car.SetTestControls(1f, 0f, 0f);
                yield return new WaitForSeconds(0.4f);
                Assert.Greater(_car.CurrentSpeedMs, 0.5f, "the car was not rolling before it was paused");

                GamePause.SetPaused(true);
                Assert.IsTrue(GamePause.IsPaused, "SetPaused(true) did not report a pause");
                Assert.AreEqual(0f, Time.timeScale, TimeScaleTolerance, "pausing left the clock running");

                // Frame yields only: a WaitForSeconds would be waiting on a clock that is
                // deliberately stopped, and the test would time out instead of failing.
                Vector3 frozen = _car.PhysicsBody.position;
                yield return null;
                yield return null;
                yield return null;

                Assert.IsTrue(GamePause.IsPaused, "the pause did not hold across frames");
                Assert.AreEqual(0f, Time.timeScale, TimeScaleTolerance, "the clock restarted by itself");
                Assert.Less(Vector3.Distance(_car.PhysicsBody.position, frozen), FrozenToleranceM,
                    "the car kept driving with the clock stopped");

                GamePause.TogglePause();
                Assert.IsFalse(GamePause.IsPaused, "TogglePause did not let the clock go");
                Assert.AreEqual(1f, Time.timeScale, TimeScaleTolerance, "the clock stayed stopped after resuming");

                Vector3 released = _car.PhysicsBody.position;
                yield return new WaitForSeconds(0.5f);
                Assert.Greater(Vector3.Distance(_car.PhysicsBody.position, released), 1f,
                    "the car did not start moving again once the clock was released");
                Assert.Greater(_car.CurrentSpeedMs, 1f, "the controller never started driving the car again");
            }
            finally
            {
                GamePause.SetPaused(false);
                Time.timeScale = 1f;
            }
        }

        // ---- stage 4: restarting the loaded scene -------------------------------------

        /// <summary>
        /// Frames given to a scene load plus the <see cref="RaceBootstrap"/> that rides on
        /// it. The load happens at the end of the frame it is asked for and the bootstrap
        /// assembles synchronously from <c>sceneLoaded</c>, so this is generous rather than a
        /// race of its own.
        /// </summary>
        private const int SceneLoadFrames = 10;

        /// <summary>How many times this test drives the pause menu's Restart button.</summary>
        private const int RestartRounds = 2;

        /// <summary>
        /// The freeze players saw: Esc -&gt; Restart unloaded <c>Track_Proto</c> and nothing
        /// put it back, because a <c>RuntimeInitializeOnLoadMethod</c> runs once per
        /// application and not once per scene, so <see cref="RaceBootstrap.Boot"/> never
        /// heard about the second load. This is that bug's end-to-end test: the real scene
        /// from the build settings, loaded for real, assembled by the bootstrap, then
        /// restarted twice through the same code the button calls.
        ///
        /// Deliberately self-contained — it builds nothing itself, and the fixture's
        /// SetUp/TearDown only know about the harness objects of the other tests, which a
        /// single-mode scene load has already taken down along with the runner's scene.
        /// Everything this test brings into the scene goes back out in the
        /// <c>finally</c>, together with the clock and the pause, so a later test of this
        /// fixture starts in an empty scene rather than on a live circuit.
        /// </summary>
        [UnityTest]
        public IEnumerator Restart_FromPause_RebuildsRaceTwice()
        {
            try
            {
                GamePause.SetPaused(false);
                Time.timeScale = 1f;

                SceneManager.LoadScene(RaceBootstrap.TrackSceneName);
                yield return WaitForFrames(SceneLoadFrames);

                Assert.AreEqual(RaceBootstrap.TrackSceneName, SceneManager.GetActiveScene().name,
                    "the track scene never became the active scene");
                AssertRaceAssembled("the load of the scene");

                for (int round = 1; round <= RestartRounds; round++)
                {
                    GamePause.SetPaused(true);
                    Assert.IsTrue(GamePause.IsPaused, $"round {round}: SetPaused(true) did not pause");

                    // The button's own path, clock included: it has to come back running.
                    GamePause.RestartActiveScene();
                    yield return WaitForFrames(SceneLoadFrames);

                    Assert.AreEqual(RaceBootstrap.TrackSceneName, SceneManager.GetActiveScene().name,
                        $"round {round}: the restart did not land on the track scene");
                    Assert.AreEqual(1f, Time.timeScale, TimeScaleTolerance,
                        $"round {round}: the restart came back with the clock stopped");
                    Assert.IsFalse(GamePause.IsPaused,
                        $"round {round}: the restart came back paused, so the panel would be up over a new race");
                    AssertRaceAssembled($"restart round {round}");
                }
            }
            finally
            {
                GamePause.SetPaused(false);
                Time.timeScale = 1f;
                DestroyActiveSceneObjects();
            }
        }

        /// <summary>
        /// Everything a race needs, in the numbers a race needs: exactly one session, a track
        /// holding the default four checkpoints, exactly one car the player drives, a chase camera
        /// aimed at that car, and one HUD and one pause menu each. <paramref name="when"/> names the
        /// load the assertion belongs to, because "it quietly stopped rebuilding" is the entire failure
        /// mode this test is after.
        /// </summary>
        private void AssertRaceAssembled(string when)
        {
            RaceSession[] sessions =
                Object.FindObjectsByType<RaceSession>(FindObjectsSortMode.None);
            Assert.AreEqual(1, sessions.Length, $"{when}: expected exactly one RaceSession");
            Assert.IsNotNull(sessions[0].Car, $"{when}: the session is not driving a car");

            TrackBuilder[] tracks =
                Object.FindObjectsByType<TrackBuilder>(FindObjectsSortMode.None);
            Assert.AreEqual(1, tracks.Length, $"{when}: expected exactly one built track");
            Assert.AreEqual(TrackLayout.DefaultCheckpointCount, tracks[0].Checkpoints.Length,
                $"{when}: the track did not come with its checkpoints");

            // A field comes with the race now, so "exactly one car" means exactly one car the player
            // drives: the opponents are somebody else's business, and the camera is checked against the
            // session's own car rather than against whichever one the object lookup returned first.
            VehicleController[] cars =
                Object.FindObjectsByType<VehicleController>(FindObjectsSortMode.None);
            int playerCars = 0;
            foreach (VehicleController car in cars)
            {
                if (car != null && car.HumanControlled)
                {
                    playerCars++;
                }
            }

            Assert.AreEqual(1, playerCars, $"{when}: expected exactly one human-driven car");

            ChaseCamera[] chases =
                Object.FindObjectsByType<ChaseCamera>(FindObjectsSortMode.None);
            Assert.AreEqual(1, chases.Length, $"{when}: expected exactly one chase camera");
            Assert.IsNotNull(chases[0].GetComponent<Camera>(),
                $"{when}: the chase rig is not on a Camera, so nothing renders through it");
            Assert.AreSame(sessions[0].Car.transform, chases[0].target,
                $"{when}: the chase camera is not aimed at the player's car");

            Assert.AreEqual(1, Object.FindObjectsByType<RaceHud>(FindObjectsSortMode.None).Length,
                $"{when}: expected exactly one HUD");
            Assert.AreEqual(1, Object.FindObjectsByType<GamePause>(FindObjectsSortMode.None).Length,
                $"{when}: expected exactly one pause menu");
        }

        /// <summary>
        /// Wait out whole frames — never scaled time, which the pause under test stops.
        /// </summary>
        private static IEnumerator WaitForFrames(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                yield return null;
            }
        }

        /// <summary>
        /// Clear the scene this test loaded, roots only, so the rest of the fixture does not
        /// inherit a live circuit under the harness it builds for itself. Objects outside that
        /// scene — the test runner's own host among them, in DontDestroyOnLoad — are left
        /// alone, which is why this filters on the scene rather than sweeping everything.
        /// </summary>
        private static void DestroyActiveSceneObjects()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != RaceBootstrap.TrackSceneName)
            {
                // The test never got as far as loading the track, so the scene that is
                // active belongs to whoever ran before it — and clearing that would take the
                // test runner down with it.
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
    }
}
