using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// The race-side owner of a session: it drives the <see cref="LapCounter"/>, listens
    /// to the track's checkpoints on the car's behalf, and keeps a car that has left the
    /// living room on the road again (PLAN.md Stage 3, acceptance criteria 5 and 7).
    ///
    /// Two jobs beyond the bookkeeping:
    ///
    /// <b>Reset follows progress.</b> Every checkpoint the counter accepts moves the pose
    /// the built-in R key resets to (<see cref="VehicleController.ArmSpawnPoint"/>), so a
    /// spin in the last corner puts the car back at the last gate it legally passed rather
    /// than back on the grid. A backwards sweep, a skipped gate or a re-cross of one
    /// already banked leaves the reset target where it was.
    ///
    /// <b>Recovery is automatic.</b> A car lying in the gravel wheels-up, or one that has
    /// fallen through the world, cannot press R, so the session presses it: at once when
    /// the body drops under <see cref="FallBelowY"/>, and after
    /// <see cref="FlipRecoverySeconds"/> of continuous upside-down time — long enough to
    /// let a two-wheel rock come back down on its own.
    ///
    /// <b>Progress is measured, not counted.</b> Alongside the counter the session keeps
    /// <see cref="PlayerProgress"/>, an odometer over the car's own position, because the two answer
    /// different questions and only one of them can be set against an opponent: the counter says which
    /// gates have been swept, the odometer says how much road has been covered, and a place in a field is
    /// the second. It is read on the physics step, and re-based rather than advanced whenever the car is
    /// reset — a car that has been picked up and put down has covered no road, and nobody should be able to
    /// buy a lap with the R key.
    ///
    /// The counter stays a plain C# object (see <see cref="LapCounter"/>): this component
    /// only feeds it the clock and the crossings, which is why the maths has tests.
    ///
    /// <b>Crossings belong to a car.</b> The checkpoints lie across the whole road, so with a field
    /// on the circuit every car reports a crossing of every gate. This session subscribes to the
    /// crossing that names the body (<see cref="TrackCheckpoint.CrossedBy"/>) and drops everything
    /// that is not its own car's; an opponent's lap is counted by that opponent's
    /// <see cref="AiDriver"/>, and never lands here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaceSession : MonoBehaviour
    {
        /// <summary>Body height under which the car counts as lost and is reset at once.</summary>
        public const float FallBelowY = -5f;

        /// <summary>
        /// Dot(car up, world up) under which the car counts as upside down. Level is 1,
        /// on its roof it is -1; 0.2 is already past the angle a driver recovers from
        /// unaided and well short of flagging a mild lean.
        /// </summary>
        public const float FlippedUpDot = 0.2f;

        /// <summary>How long the car may stay upside down before the session resets it.</summary>
        public const float FlipRecoverySeconds = 3f;

        private TrackBuilder _track;
        private VehicleController _car;
        private LapCounter _counter;

        /// <summary>
        /// The AI field this session is racing against, for the HUD to rank the player against.
        /// Empty until <see cref="SetOpponents"/> says otherwise, and never null, so a session that
        /// is still assembling (or a time trial, which is what this was until there were opponents)
        /// simply reports a field of one.
        /// </summary>
        private IReadOnlyList<AiDriver> _opponents = Array.Empty<AiDriver>();

        /// <summary>Checkpoints this component subscribed to, so OnDestroy can back out.</summary>
        private TrackCheckpoint[] _subscribed = Array.Empty<TrackCheckpoint>();

        /// <summary>Seconds the car has been continuously upside down.</summary>
        private float _flippedFor;

        /// <summary>
        /// The player's own place round the circuit, in metres come: an odometer over the body's position,
        /// measured the same way each opponent measures itself so the two can be put in order at all. It is
        /// deliberately not built from <see cref="_counter"/> — a counter says which gates have been swept,
        /// and the last version of this figure tried to get a distance out of it and jumped by a whole lap
        /// at the line.
        /// </summary>
        private readonly TrackProgress _playerProgress = new TrackProgress();

        /// <summary>
        /// The car whose <see cref="VehicleController.SpawnReset"/> this session is listening to, which is
        /// not necessarily the one it is timing: <see cref="Init"/> can be handed a different car, and a
        /// subscription has to come off the old one before the new one goes on.
        /// </summary>
        private VehicleController _resetSource;

        /// <summary>The lap bookkeeper; null until <see cref="Init"/> has run.</summary>
        public LapCounter Counter => _counter;

        /// <summary>The car whose crossings and predicament this session follows.</summary>
        public VehicleController Car => _car;

        /// <summary>The track whose checkpoints feed the counter.</summary>
        public TrackBuilder Track => _track;

        /// <summary>How long the car has been upside down; zero the moment it is not.</summary>
        public float FlippedFor => _flippedFor;

        /// <summary>
        /// How far round the circuit the player's car has come, in metres, from the grid slot it started
        /// on — the figure the HUD ranks against every opponent's own. Never null: it is a plain object the
        /// session owns, so a HUD drawing before <see cref="Init"/> reads an odometer at nought rather than
        /// finding nothing to read.
        /// </summary>
        public TrackProgress PlayerProgress => _playerProgress;

        /// <summary>
        /// The opponents of this session's car, in the order they were spawned. Never null; empty
        /// for a session nobody has given a field to.
        /// </summary>
        public IReadOnlyList<AiDriver> Opponents => _opponents;

        /// <summary>RaceFlow id of the player's car.</summary>
        public const string PlayerId = "Player";

        /// <summary>Explicit test hooks, null in the game. A test that sets one must reset it to null in its TearDown.</summary>
        public static float? CountdownOverrideSeconds;
        public static int? LapsOverride;

        private RaceFlow _flow;

        /// <summary>Countdown, race clock, lap total, finishes and results for this race.</summary>
        public RaceFlow Flow => _flow;

        /// <summary>
        /// Say who the player is racing, after <see cref="AiField"/> has built them — the session owns
        /// the player's own timing and knows nothing about how the field was made, it is handed the
        /// result so the HUD can rank the two against each other.
        /// </summary>
        /// <param name="opponents">
        /// The drivers spawned for this session. Null is taken for no opponents rather than a fault:
        /// a bootstrap that could not build a field should still leave a race that runs.
        /// </param>
        public void SetOpponents(IReadOnlyList<AiDriver> opponents)
        {
            _opponents = opponents ?? Array.Empty<AiDriver>();

            for (int i = 0; i < _opponents.Count; i++)
            {
                AiDriver driver = _opponents[i];
                if (driver == null || driver.Counter == null || _flow == null) continue;
                string id = driver.Name;
                LapCounter laps = driver.Counter;
                _flow.Register(id, false);
                laps.LapCompleted += _ => _flow.OnLapCompleted(id, laps.CompletedLaps);
            }

            ApplyDriveGates();
        }

        /// <summary>
        /// Start a session over a built track with one car — called by
        /// <see cref="RaceBootstrap"/> once both exist.
        /// </summary>
        /// <param name="track">
        /// Builder whose <see cref="TrackBuilder.Checkpoints"/> are already in the scene.
        /// The counter is sized to that array, so the track has to be built first.
        /// </param>
        /// <param name="car">The car to time, and to fish out of the ditch.</param>
        public void Init(TrackBuilder track, VehicleController car)
        {
            if (track == null)
            {
                throw new ArgumentNullException(nameof(track), "RaceSession needs a track to time.");
            }

            if (car == null)
            {
                throw new ArgumentNullException(nameof(car), "RaceSession needs a car to follow.");
            }

            _track = track;
            _car = car;
            _counter = new LapCounter(track.Checkpoints.Length);

            _flow = new RaceFlow(LapsOverride ?? RaceFlow.DefaultLaps,
                CountdownOverrideSeconds ?? RaceFlow.DefaultCountdownSeconds);
            _flow.Register(PlayerId, true);
            LapCounter playerLaps = _counter;
            playerLaps.LapCompleted += _ => _flow.OnLapCompleted(PlayerId, playerLaps.CompletedLaps);

            // A counted lap is also the track record candidate: banked straight away so a
            // crash on the next lap cannot lose it, and read back by the HUD's BEST line.
            string trackId = TrackBuilder.TrackId;
            playerLaps.LapCompleted += lapSeconds => BestLapStore.Submit(trackId, lapSeconds);

            ApplyDriveGates();

            // The odometer is based on wherever the car has been stood — the grid, in a race being
            // assembled — so a place in the field is measured from the start of the race rather than from
            // the finish line. The resets are listened for in the same breath, because a car that has been
            // picked up and put down has not travelled the road in between.
            _playerProgress.Reset(CarPosition());
            WatchResets(car);

            var subscribed = new TrackCheckpoint[track.Checkpoints.Length];
            for (int i = 0; i < track.Checkpoints.Length; i++)
            {
                TrackCheckpoint checkpoint = track.Checkpoints[i];
                if (checkpoint == null)
                {
                    continue; // Half-built track; the counter is sized for it anyway.
                }

                // The event hands over the checkpoint, the direction and the body, which is the
                // three things this has to have: the counter wants the first two and only the body
                // says whether the crossing is even the car being timed. With a field of cars on the
                // circuit every one of them crosses every gate, and a session that subscribed to
                // <see cref="TrackCheckpoint.Crossed"/> would credit the player with the laps of
                // whoever happened past — so it listens to the event that names the body and drops
                // every crossing that is not its own car's.
                checkpoint.CrossedBy += OnCheckpointCrossed;
                subscribed[i] = checkpoint;
            }

            _subscribed = subscribed;
        }

        /// <summary>
        /// A gate swept by this session's car. Only a crossing the counter accepts — forward, and the
        /// next one in sequence — is allowed to move the reset pose. A gate swept by anybody else is
        /// not even looked at: their progress is their own, counted by their own
        /// <see cref="AiDriver"/>.
        /// </summary>
        private void OnCheckpointCrossed(TrackCheckpoint checkpoint, bool forward, Rigidbody body)
        {
            if (_counter == null || checkpoint == null)
            {
                return;
            }

            Rigidbody mine = _car != null ? _car.PhysicsBody : null;
            if (mine == null || body != mine)
            {
                return; // An opponent crossing the same gate; their counters, not this one's.
            }

            int before = _counter.LastValidCheckpoint;
            _counter.OnCheckpoint(checkpoint.index, forward);

            if (_counter.LastValidCheckpoint != before)
            {
                _car?.ArmSpawnPoint();
            }
        }

        private void Update()
        {
            if (_car == null)
            {
                return;
            }

            // Time.timeScale is already 0 while paused, so the clock would stall anyway;
            // the guard states the intent and keeps the lap timer honest if a pause is
            // ever implemented without touching the scale.
            if (_counter != null && !GamePause.IsPaused)
            {
                _counter.Tick(Time.deltaTime);
            }

            UpdateRecovery(Time.deltaTime);
        }

        /// <summary>
        /// One physics step of the player's odometer. The physics step is the only clock this can be read
        /// from: the metres a car has come have to be sampled at the rate the car moves, or a frame rate
        /// that halved for a second would read as a car that had stopped. Pausing already stops it by
        /// stopping physics — <see cref="GamePause"/> runs the clock at zero, so no step is taken and no
        /// metres are claimed behind the menu — and a car at rest adds nought whatever the rate.
        /// </summary>
        private void FixedUpdate()
        {
            if (_car == null)
            {
                return; // Nothing to measure until Init has been handed a car.
            }

            _playerProgress.Update(CarPosition());

            // The race clock runs on the physics step, so it stops with the pause (timeScale 0) and
            // follows any time scale exactly as the cars do.
            if (_flow != null)
            {
                _flow.Tick(Time.fixedDeltaTime);
                ApplyDriveGates();
            }
        }

        /// <summary>Lock every car the flow says may not drive (countdown, or finished).</summary>
        private void ApplyDriveGates()
        {
            if (_flow == null) return;
            if (_car != null) _car.DriveLocked = !_flow.CanDrive(PlayerId);
            for (int i = 0; i < _opponents.Count; i++)
            {
                AiDriver driver = _opponents[i];
                if (driver != null && driver.Car != null) driver.Car.DriveLocked = !_flow.CanDrive(driver.Name);
            }
        }

        /// <summary>
        /// The car has just been put back on the pose it was armed with, by the R key or by
        /// <see cref="Recover"/>. The metres between where it was and where it has been put are metres it
        /// was carried, so the odometer's baseline follows the body to the reset pose and its value is left
        /// exactly where it was: a reset neither earns a driver progress nor takes the progress it had
        /// already earned away from it, and it cannot be farmed for a lap by resetting over the line.
        /// </summary>
        private void OnSpawnReset()
        {
            _playerProgress.Rebase(CarPosition());
        }

        /// <summary>
        /// Start (or stop) listening to a car's resets, backing out of whatever was being listened to
        /// first. Subscribing twice to the same car would be two listeners on one event; subscribing to
        /// the new car and leaving the old one behind would be a session rebasing its odometer on somebody
        /// else's reset.
        /// </summary>
        private void WatchResets(VehicleController car)
        {
            if (_resetSource == car)
            {
                return; // Already watching exactly this car; Init can be called again over the same one.
            }

            if (_resetSource != null)
            {
                _resetSource.SpawnReset -= OnSpawnReset;
            }

            _resetSource = car;

            if (car != null)
            {
                car.SpawnReset += OnSpawnReset;
            }
        }

        /// <summary>
        /// Where the car's physics body is, which is the position the whole race is measured from — the
        /// checkpoints, the recovery and the odometer all read the body rather than the transform, because
        /// the body is what physics moves. The transform is the fallback for a body that is not there.
        /// </summary>
        private Vector3 CarPosition()
        {
            if (_car == null)
            {
                return Vector3.zero; // Nothing to measure, and nothing that reads this either.
            }

            Rigidbody body = _car.PhysicsBody;
            return body != null ? body.position : _car.transform.position;
        }

        /// <summary>
        /// Falling out of the world resets at once — there is nothing to come back from.
        /// Being upside down waits, because a car on two wheels often sorts itself out.
        /// <paramref name="dt"/> is scaled time, so the wait stops while the game is
        /// paused rather than quietly running out behind the menu.
        /// </summary>
        private void UpdateRecovery(float dt)
        {
            Rigidbody body = _car.PhysicsBody;
            Vector3 position = body != null ? body.position : _car.transform.position;

            if (position.y < FallBelowY)
            {
                Recover("the car dropped below the world");
                return;
            }

            if (Vector3.Dot(_car.transform.up, Vector3.up) >= FlippedUpDot)
            {
                _flippedFor = 0f;
                return;
            }

            _flippedFor += dt;
            if (_flippedFor >= FlipRecoverySeconds)
            {
                Recover($"upside down for {FlipRecoverySeconds:F1} s");
            }
        }

        /// <summary>
        /// Put the car back on the pose it was last armed with — the grid until a gate is
        /// banked, the last gate from then on (criterion 7: one frame, velocity zeroed,
        /// which is what <see cref="VehicleController.ResetToSpawn"/> does).
        /// </summary>
        private void Recover(string reason)
        {
            _flippedFor = 0f;
            _car.ResetToSpawn();
            Debug.Log($"[RaceSession] auto-reset: {reason}.");
        }

        /// <summary>
        /// The checkpoints normally go away with the track, but a session torn down under
        /// a track that survived must not leave a dead counter being fed by gates nobody
        /// is timing any more.
        /// </summary>
        private void OnDestroy()
        {
            // The car outlives a session torn down under a track that survived, and it would go on
            // announcing resets to a listener that is on its way out. Backing out of the whole thing the
            // same way Init went in also covers a session handed no car at all.
            WatchResets(null);

            if (_counter == null)
            {
                return;
            }

            foreach (TrackCheckpoint checkpoint in _subscribed)
            {
                if (checkpoint != null)
                {
                    checkpoint.CrossedBy -= OnCheckpointCrossed;
                }
            }

            _subscribed = Array.Empty<TrackCheckpoint>();
        }
    }
}
