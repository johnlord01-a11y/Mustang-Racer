using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// One opponent car driven by the computer: pure pursuit of the <see cref="TrackLayout"/>
    /// centreline, a speed target that comes down for the turns before it arrives at them, and its
    /// own <see cref="LapCounter"/> so the laps it crosses are counted by it and never land in the
    /// player's session (PLAN.md Stage 6, first milestone).
    ///
    /// <b>Where am I?</b> the nearest centreline sample — a wrap-aware search around the one it was
    /// nearest last, refined to a distance along the chords touching it so the figure is continuous
    /// rather than stepping at the 2 m sample pitch. That distance and the signed sideways offset off
    /// the centreline are the whole state the driver needs. The figure it feeds the ranking with is a
    /// separate odometer over that distance — <see cref="TrackProgress"/>, which adds the movement up
    /// instead of recomputing a place from the laps counted — so it cannot gain or lose a whole lap
    /// depending on when a lap counter happened to start or where the car was sitting relative to the
    /// line, and it only ever runs as fast as the car does.
    ///
    /// <b>Where am I going?</b> a point on the centreline <see cref="LookaheadBaseM"/> metres
    /// ahead — further the faster it is going, so the lock asked for at 25 m/s is the same small
    /// angle asked for at 10 — pushed out to this car's own lane so a field of them does not
    /// single-file through a corner. The steer is the signed angle to that point over the wheels' own
    /// maximum angle, clamped: a fraction of the steering wheel in Unity's own units, on a rack that
    /// <see cref="VehicleController"/> rate-limits, so nothing snaps.
    ///
    /// <b>How fast?</b> <see cref="StraightSpeedMs"/> where the centreline is straight and less
    /// wherever it is curved — <c>sqrt(grip × radius)</c> off the curvature read over a few metres of
    /// arc, worst case over the next <see cref="BrakingHorizonM"/> metres so the corner's speed is
    /// asked for before the corner and not in it. Throttle and brake are a P controller on that
    /// target, and the brake is never touched below a m/s of forward speed: at that speed the brake
    /// key <i>is</i> the reverse, and a driver that brushed against it every time it stalled would be
    /// backing out of the grid for the rest of the race.
    ///
    /// <b>What if it goes wrong?</b> the two mistakes a person makes. Nosed into a barrier, or lying
    /// in the infield: reverse out of it with the steering wound the other way for a second and a
    /// half. Twice without coming back to running speed and the car goes back on the centreline at the
    /// distance it had already reached — its own body, its own pose, its own velocity, which is the
    /// one thing this component owns outright. It never reaches past <see cref="_car"/>: the player's
    /// car, camera and session are nobody's business but their own.
    ///
    /// Controls come in through <see cref="VehicleController.SetTestControls"/> — the same four
    /// analogue values the devices would have produced, from which
    /// <see cref="VehicleController"/> works out throttle, brake and reverse exactly as it does for a
    /// person. The laps it crosses are counted the way the session counts the player's, through
    /// <see cref="TrackCheckpoint.CrossedBy"/> into a <see cref="LapCounter"/>, dropping every
    /// crossing that is not this car's rigid body: the gates lie across the whole road, so every car
    /// on the circuit reports a crossing of every one of them. Those laps are its own bookkeeping;
    /// where it stands in the field is what <see cref="TrackProgress"/> measures, and the two are kept
    /// apart because a lap is a rule about gates and progress is a measurement of road.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AiDriver : MonoBehaviour
    {
        // ---- the driving numbers -------------------------------------------------------

        /// <summary>Speed aimed for where the centreline is straight, m/s.</summary>
        public const float StraightSpeedMs = 26f;

        /// <summary>
        /// Lateral acceleration the corner speed is worked out from, m/s². Against this circuit's 60 m
        /// turns it asks for 22 m/s through a corner — under the speed the car is good for, which is
        /// the point: an opponent taken at the limit cannot be caught, only not passed.
        /// </summary>
        public const float CornerLateralAccelMs2 = 9f;

        /// <summary>Margin on that figure, so a corner taken at it is one the car survives.</summary>
        public const float CornerSpeedScale = 0.95f;

        /// <summary>Look-ahead down the centreline standing still, m.</summary>
        public const float LookaheadBaseM = 6f;

        /// <summary>Extra look-ahead per m/s of speed, m per m/s.</summary>
        public const float LookaheadPerMs = 0.6f;

        /// <summary>Floor on it: closer than this and the car weaves after its own nose.</summary>
        public const float LookaheadMinM = 8f;

        /// <summary>Ceiling on it: further out and a corner inside the look-ahead is taken flat.</summary>
        public const float LookaheadMaxM = 25f;

        /// <summary>Arc length the heading is differenced over to read the curvature, m.</summary>
        public const float CurvatureProbeM = 4f;

        /// <summary>
        /// How far ahead the curvature is scanned for the speed target, m. A braking distance rather
        /// than a sight line: the car has to be told to slow down early enough for the brakes to
        /// manage it.
        /// </summary>
        public const float BrakingHorizonM = 40f;

        /// <summary>
        /// Step of that scan, m. The layout is two straights and two constant-radius turns, so a step
        /// this coarse still reads every corner several times over.
        /// </summary>
        public const float BrakingStepM = 8f;

        /// <summary>Curvature below which the centreline counts as straight, rad/m (a 1 km radius).</summary>
        public const float StraightCurvature = 1e-3f;

        /// <summary>Speed error asking for full throttle, m/s short of the target.</summary>
        public const float ThrottleBandMs = 4f;

        /// <summary>Speed error asking for full brake, m/s over the target.</summary>
        public const float BrakeBandMs = 3f;

        /// <summary>
        /// Forward speed (m/s) below which the brake is not touched: at or under
        /// <see cref="VehicleTuning.ReverseEngageSpeedMs"/> the brake key is the reverse, and only the
        /// recovery below reaches for it.
        /// </summary>
        public const float BrakeSpeedFloorMs = 1f;

        /// <summary>Largest lane offset held, so that a lane stays inside the road.</summary>
        public const float MaxLaneOffsetM = TrackLayout.RoadWidth * 0.5f - 2f;

        // ---- the recovery --------------------------------------------------------------

        /// <summary>Forward speed (m/s) under which the car counts as stuck rather than merely slow.</summary>
        public const float StuckSpeedMs = 1f;

        /// <summary>How long stuck may last before the reverse is put in, s.</summary>
        public const float StuckSeconds = 2.5f;

        /// <summary>
        /// Sideways distance from the centreline beyond which the car is off the road. The road is
        /// <see cref="TrackLayout.RoadWidth"/> across with the walls half a metre outside that again,
        /// so this is a distance a car only reaches by getting over a wall.
        /// </summary>
        public const float OffTrackLateralM = TrackLayout.RoadWidth * 0.5f + 3f;

        /// <summary>How long off the road may last before the reverse is put in, s.</summary>
        public const float OffTrackSeconds = 3f;

        /// <summary>How long the reverse is held for, s, after which the attempt is over either way.</summary>
        public const float ReverseSeconds = 1.5f;

        /// <summary>
        /// Reverse manoeuvres tried before the car is given up on and put back on the track by being
        /// moved instead of driven.
        /// </summary>
        public const int RecoveryAttemptsBeforeReset = 2;

        /// <summary>
        /// Speed (m/s) back above which an earlier failed attempt is forgotten: the field is meant to
        /// keep racing, not carry a strike against it for the rest of the session.
        /// </summary>
        public const float RecoveredSpeedMs = 5f;

        /// <summary>Steer angle below which the last frame counts as driven straight ahead.</summary>
        private const float CentredSteer = 0.15f;

        /// <summary>
        /// Sample-index search window either side of the last nearest sample. Twelve samples is 24 m of
        /// track, and the fastest thing this car does covers a third of that in one frame — enough to
        /// cover a lap of the search between two frames while still being nothing on a plain miss,
        /// which falls back to scanning the whole loop.
        /// </summary>
        private const int SearchWindowSamples = 12;

        /// <summary>Maximum steering angle assumed for a tuning that has not been wired in yet.</summary>
        private const float FallbackMaxSteerDeg = 20f;

        // ---- the state -----------------------------------------------------------------

        /// <summary>The car this driver drives, and the only body it ever writes to.</summary>
        private VehicleController _car;

        /// <summary>Checkpoints subscribed to, so <see cref="OnDestroy"/> can back out of them.</summary>
        private TrackCheckpoint[] _subscribed = System.Array.Empty<TrackCheckpoint>();

        /// <summary>This car's own lap bookkeeper, sized to the track's checkpoints.</summary>
        private LapCounter _counter;

        /// <summary>
        /// This car's own along-track odometer: how many metres it has come round the circuit, measured
        /// from the grid slot it was stood on. Kept apart from <see cref="_counter"/> on purpose — the
        /// counter says which gates have been swept, this says how much road has been covered — and read
        /// out through <see cref="Progress"/>, which is the number the field is ranked by.
        /// </summary>
        private readonly TrackProgress _progress = new TrackProgress();

        /// <summary>Sideways offset held down the road, clamped to <see cref="MaxLaneOffsetM"/>.</summary>
        private float _laneOffset;

        /// <summary>Distance along the centreline of the last frame's nearest point, m.</summary>
        private float _trackDistance;

        /// <summary>Signed sideways offset off the centreline, m, positive being to the right.</summary>
        private float _lateral;

        /// <summary>Index of the last nearest centreline sample; negative until the first fix.</summary>
        private int _sampleIndex = NoSample;

        /// <summary>Steer handed to the car last frame, reused as the direction to reverse against.</summary>
        private float _lastSteer;

        /// <summary>Steer held through the current reverse manoeuvre (the opposite of <see cref="_lastSteer"/>).</summary>
        private float _reverseSteer;

        private float _stuckFor;
        private float _offTrackFor;
        private float _flippedFor;
        private float _recoverFor;

        /// <summary>Reverse manoeuvres tried since the car was last properly running.</summary>
        private int _recoveryAttempts;

        /// <summary>True while the reverse manoeuvre is the only thing being asked of the car.</summary>
        private bool _recovering;

        /// <summary>True once <see cref="Init"/> has been given a car and a track to work with.</summary>
        private bool _ready;

        /// <summary>The material this driver was handed to paint its body with, and must free.</summary>
        private Material _bodyMaterial;

        // ---- what the rest of the race can see -----------------------------------------

        /// <summary><see cref="_sampleIndex"/> before the car has been located on the centreline.</summary>
        private const int NoSample = -1;

        /// <summary>Display name of this opponent, <c>AI_1</c> and up as <see cref="AiField"/> spawns them.</summary>
        public string Name { get; private set; } = "AI";

        /// <summary>The colour this opponent is painted, for a timing sheet or a legend that wants one.</summary>
        public UnityEngine.Color Color { get; private set; } = UnityEngine.Color.white;

        /// <summary>The lap counter this car's own crossings are fed to; null before <see cref="Init"/>.</summary>
        public LapCounter Counter => _counter;

        /// <summary>The car being driven.</summary>
        public VehicleController Car => _car;

        /// <summary>Full laps counted by this car, the same figure the player's HUD reads.</summary>
        public int Laps => _counter != null ? _counter.CompletedLaps : 0;

        /// <summary>1-based lap this car is on; 0 until it has crossed the line.</summary>
        public int CurrentLap => _counter != null ? _counter.CurrentLap : 0;

        /// <summary>Distance along the lap of the nearest centreline point, m.</summary>
        public float TrackDistance => _trackDistance;

        /// <summary>Signed sideways distance off the centreline, m.</summary>
        public float Lateral => _lateral;

        /// <summary>Whether the reverse manoeuvre is what the car is doing right now.</summary>
        public bool IsRecovering => _recovering;

        /// <summary>The lane this car holds, m off the centreline, positive being to the right.</summary>
        public float LaneOffset => _laneOffset;

        /// <summary>
        /// Reverse manoeuvres this car has run since it was built — every <see cref="BeginRecovery"/>
        /// counted, the ones that worked and the ones that did not. This is not
        /// <see cref="_recoveryAttempts"/>, which is forgiven the moment the car is running properly
        /// again because it is the strike count that decides what to try next: this one is a record of
        /// what happened to the car, which is the difference between a lap driven clean and one rescued
        /// twice, and something a timing sheet or a test can only ask for once.
        /// </summary>
        public int RecoveryCount { get; private set; }

        /// <summary>
        /// Times this car has been put back on the centreline by being moved rather than driven — every
        /// <see cref="ResetToTrack"/> counted, whichever predicament reached it (stuck, off the road,
        /// upside down or out of the world).
        /// </summary>
        public int ResetCount { get; private set; }

        /// <summary>
        /// How far round the circuit this car has come, in metres: its own odometer of the road it has
        /// covered, started at the signed distance of the grid slot from the finish line so the cars
        /// behind the line begin behind it. The one number the field is ranked by, and the player's own
        /// standing is <see cref="RaceSession.PlayerProgress"/>, measured the same way.
        ///
        /// It cannot exceed the ground the car has driven, which is the property the ranking depends on
        /// and the one the figure this replaces did not have: built out of laps times the perimeter, it
        /// jumped by a lap of 777 m whenever a lap was counted, and reported a car that had driven 818 m
        /// as having come 1581.
        /// </summary>
        public float Progress => _progress.Value;

        /// <summary>
        /// Start this driver over a built track with the car it owns. Called by
        /// <see cref="AiField"/> straight after it has built the car, in the same call that spawns the
        /// field, so there is no frame in which a driver exists with nothing to drive.
        /// </summary>
        /// <param name="track">
        /// Builder whose <see cref="TrackBuilder.Checkpoints"/> are already in the scene; the counter
        /// is sized to that array, exactly as <see cref="RaceSession.Init"/> sizes the player's.
        /// </param>
        /// <param name="car">The car to drive. Expected to be a remote-controlled one.</param>
        /// <param name="driverName">Display name, which <see cref="AiField"/> also gives to the car itself.</param>
        /// <param name="color">The colour this opponent is painted.</param>
        /// <param name="laneOffset">
        /// Lane to hold, m off the centreline, positive being to the right of the driving direction.
        /// Clamped to <see cref="MaxLaneOffsetM"/>, because a lane that leaves the road is not a lane.
        /// </param>
        /// <param name="bodyMaterial">
        /// Material this driver must free when it goes away, or null when it has no body to paint. The
        /// material is created by <see cref="AiField"/> and has no asset to be unloaded by.
        /// </param>
        public void Init(TrackBuilder track, VehicleController car, string driverName,
            UnityEngine.Color color, float laneOffset, Material bodyMaterial = null)
        {
            if (car == null)
            {
                throw new System.ArgumentNullException(nameof(car), "An AiDriver needs a car to drive.");
            }

            _car = car;
            Name = string.IsNullOrEmpty(driverName) ? "AI" : driverName;
            Color = color;
            _laneOffset = Mathf.Clamp(laneOffset, -MaxLaneOffsetM, MaxLaneOffsetM);
            _bodyMaterial = bodyMaterial;
            _ready = true;

            // The counter is sized to the track the checkpoints came from rather than to a number
            // handed in, so an opponent and the session behind the player can never disagree about
            // how many gates a lap is made of.
            TrackCheckpoint[] checkpoints = track != null ? track.Checkpoints : null;
            _counter = new LapCounter(checkpoints != null && checkpoints.Length > 0
                ? checkpoints.Length
                : TrackLayout.DefaultCheckpointCount);

            // The odometer is based on the grid slot this car has just been stood on, so its place in the
            // field is measured from where the race began rather than from the finish line: a car six
            // metres short of the line starts six metres behind it, which is also what has the field in
            // the right order before anybody has moved.
            _progress.Reset(BodyPosition());

            Subscribe(checkpoints);
        }

        // ---- one frame -----------------------------------------------------------------

        /// <summary>
        /// Locate the car, judge whether it is in a state to be driven at all, and hand in either the
        /// lap it is doing or the reverse that gets it back to one.
        ///
        /// Frame rather than physics step: the perception is a handful of centreline lookups and the
        /// controls it writes persist until changed, so a fixed-step loop would only spend twice the
        /// arithmetic at a hundred hertz to arrive at the same steering wheel.
        /// </summary>
        private void Update()
        {
            if (!_ready || _car == null)
            {
                return;
            }

            // Pausing already stops this on its own — Time.deltaTime is nil and the physics step that
            // would read the controls never runs — but the session states its intent the same way, and
            // a driver that kept winding on the throttle behind the menu would be a surprise.
            if (GamePause.IsPaused)
            {
                return;
            }

            float dt = Time.deltaTime;

            // Held on the grid by the race gate: no stuck/off-track clocks may run, so the countdown
            // can never trigger a recovery or a reset that would move the car.
            if (_car.DriveLocked)
            {
                _stuckFor = 0f;
                _offTrackFor = 0f;
                return;
            }
            Vector3 position = _car.PhysicsBody != null
                ? _car.PhysicsBody.position
                : _car.transform.position;

            // The opponent's own lap clock, kept on the same scaled clock the session keeps the
            // player's on.
            Tick();

            UpdateTrackPosition(position);
            UpdateRecovery(dt, position);

            if (_recovering)
            {
                ApplyRecovery(dt);
                return;
            }

            Drive(position);
        }

        /// <summary>
        /// One physics step of the odometer. The physics step is the only clock this can be read from:
        /// the metres a car has come have to be sampled at the rate the car moves, or a frame rate that
        /// halved for a second would look like a car that had stopped, and the difference between those
        /// two is the difference between a slow lap and a stuck car.
        ///
        /// Nothing else happens here. The perception and the controls stay in <see cref="Update"/>,
        /// which is the right rate for a steering wheel, and pausing already stops this by stopping
        /// physics — <see cref="GamePause"/> runs the clock at zero, so no step is taken and no metres
        /// are claimed while the menu is in front of it.
        /// </summary>
        private void FixedUpdate()
        {
            if (!_ready || _car == null)
            {
                return;
            }

            _progress.Update(BodyPosition());
        }

        /// <summary>
        /// Where this car's physics body is, which is the position everything about it is measured from —
        /// the centreline fix, the recovery, and the odometer — so that none of them can be measuring a
        /// different car. The transform is the fallback for a body that is not there (yet, or any more),
        /// which is where the car visibly is even when physics has stopped answering.
        /// </summary>
        private Vector3 BodyPosition()
        {
            Rigidbody body = _car != null ? _car.PhysicsBody : null;
            return body != null ? body.position : _car.transform.position;
        }

        /// <summary>
        /// Steer towards the look-ahead point and hold the speed the next stretch of road will accept.
        /// </summary>
        private void Drive(Vector3 position)
        {
            float speed = _car.CurrentSpeedMs;

            float lookahead = Mathf.Clamp(
                LookaheadBaseM + LookaheadPerMs * speed, LookaheadMinM, LookaheadMaxM);

            Pose aim = TrackLayout.PoseAt(_trackDistance + lookahead);
            Vector3 target = aim.position + aim.right * _laneOffset - position;
            target.y = 0f;

            float steer = SteerTowards(target);

            float throttle = 0f;
            float brake = 0f;
            float error = SpeedLimitAhead() - _car.ForwardSpeed;

            if (error > 0f)
            {
                throttle = Mathf.Clamp01(error / ThrottleBandMs);
            }
            else if (_car.ForwardSpeed > BrakeSpeedFloorMs)
            {
                // Braked only from above the speed the reverse engages at: at or under it the brake key
                // is the reverse, and a P controller that had not thought that through would put the
                // car on its way back down the straight the moment it was caught napping on the grid.
                brake = Mathf.Clamp01(-error / BrakeBandMs);
            }

            _lastSteer = steer;
            _car.SetTestControls(throttle, brake, steer);
        }

        /// <summary>Squared size of a target offset too small to point a car at, m².</summary>
        private const float AimEpsilonSquared = 1e-8f;

        /// <summary>
        /// The steering fraction that points the chassis at <paramref name="target"/> in the minus one
        /// to one units of a steering wheel: the angle between where the nose is and where the target
        /// is, over the maximum angle the wheels will actually reach. One is therefore full lock,
        /// which is what a target well off to the side asks for, where a correction of two degrees asks
        /// for a tenth of it.
        /// </summary>
        private float SteerTowards(Vector3 target)
        {
            if (target.sqrMagnitude < AimEpsilonSquared)
            {
                return _lastSteer; // The target is under the car; hold what was already asked for.
            }

            Vector3 heading = _car.transform.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < AimEpsilonSquared)
            {
                return _lastSteer; // Vertical is not a heading; the last frame's lock is the honest one.
            }

            float degrees = Vector3.SignedAngle(heading, target, Vector3.up);
            VehicleTuning tuning = _car.Tuning;
            float maxSteer = tuning != null ? Mathf.Max(1f, tuning.MaxSteerAngle) : FallbackMaxSteerDeg;

            return Mathf.Clamp(degrees / maxSteer, -1f, 1f);
        }

        /// <summary>
        /// The slowest point of road between here and <see cref="BrakingHorizonM"/> metres ahead: the
        /// straights ask for <see cref="StraightSpeedMs"/> and a corner asks for whatever its radius can
        /// be taken at, so the figure read a lap of distance <i>before</i> the corner is what gets the
        /// car into it at it rather than out of it.
        /// </summary>
        private float SpeedLimitAhead()
        {
            float limit = StraightSpeedMs;

            for (float ahead = 0f; ahead <= BrakingHorizonM; ahead += BrakingStepM)
            {
                limit = Mathf.Min(limit, LimitAt(_trackDistance + ahead));
            }

            return limit;
        }

        /// <summary>
        /// Speed the centreline can be taken at <paramref name="distance"/> metres round the lap, from
        /// how hard its heading turns there. The heading of a constant-radius arc turns in proportion
        /// to the arc travelled, so one over the other is the curvature and its reciprocal the radius:
        /// no assumption about this particular oval, and a circuit of hand-laid corners would read the
        /// same way.
        /// </summary>
        private static float LimitAt(float distance)
        {
            Vector3 here = TrackLayout.PoseAt(distance).forward;
            Vector3 ahead = TrackLayout.PoseAt(distance + CurvatureProbeM).forward;

            // Heading difference over the arc that produced it, in radians per metre.
            float curvature = Mathf.Abs(Vector3.SignedAngle(here, ahead, Vector3.up))
                              * Mathf.Deg2Rad / CurvatureProbeM;

            if (curvature < StraightCurvature)
            {
                return StraightSpeedMs;
            }

            return Mathf.Min(StraightSpeedMs,
                Mathf.Sqrt(CornerLateralAccelMs2 / curvature) * CornerSpeedScale);
        }

        // ---- getting out of trouble ----------------------------------------------------

        /// <summary>
        /// The three ways an opponent comes to grief, watched the way the session watches the player
        /// so that a field is judged by one yardstick: <see cref="RaceSession.FallBelowY"/>,
        /// <see cref="RaceSession.FlippedUpDot"/> and <see cref="RaceSession.FlipRecoverySeconds"/> are
        /// the session's own constants, shared because a second set of them would only drift.
        ///
        /// Falling through the world and lying on the roof are the predicaments a gear cannot fetch, so
        /// those go straight to a reset. A nose in a barrier and a car off the road behind it are the
        /// ones a reverse was invented for, so those get the reverse first and a reset only once two
        /// attempts have not got it running again.
        /// </summary>
        private void UpdateRecovery(float dt, Vector3 position)
        {
            if (position.y < RaceSession.FallBelowY)
            {
                ResetToTrack("dropped below the world");
                return;
            }

            _flippedFor = Vector3.Dot(_car.transform.up, Vector3.up) < RaceSession.FlippedUpDot
                ? _flippedFor + dt
                : 0f;

            if (_flippedFor >= RaceSession.FlipRecoverySeconds)
            {
                ResetToTrack($"upside down for {RaceSession.FlipRecoverySeconds:F1} s");
                return;
            }

            if (_recovering)
            {
                return; // The manoeuvre is what is being driven; its own clock is the only timer.
            }

            _stuckFor = _car.ForwardSpeed < StuckSpeedMs ? _stuckFor + dt : 0f;
            _offTrackFor = Mathf.Abs(_lateral) > OffTrackLateralM ? _offTrackFor + dt : 0f;

            if (_car.CurrentSpeedMs > RecoveredSpeedMs
                && Mathf.Abs(_lateral) <= TrackLayout.RoadWidth * 0.5f)
            {
                _recoveryAttempts = 0; // Running properly: what happened before is forgotten.
            }

            if (_stuckFor < StuckSeconds && _offTrackFor < OffTrackSeconds)
            {
                return;
            }

            if (_recoveryAttempts >= RecoveryAttemptsBeforeReset)
            {
                ResetToTrack(_stuckFor >= StuckSeconds
                    ? $"held under {StuckSpeedMs:F0} m/s for {StuckSeconds:F1} s"
                    : $"off the road for {OffTrackSeconds:F1} s");
                return;
            }

            BeginRecovery();
        }

        /// <summary>
        /// Put the reverse in. Throttle at nil with the brake held is what engages it, at or under the
        /// speed <see cref="VehicleController"/> latches it at, and this is the one place this driver
        /// reaches for that key. The lock goes the other way from the lock the car arrived with, because
        /// a car that has just driven somewhere it should not has its nose pointed at it.
        /// </summary>
        private void BeginRecovery()
        {
            _recoveryAttempts++;
            RecoveryCount++; // The running tally, which outlives the forgiven strike count above.
            _recovering = true;
            _recoverFor = 0f;
            _stuckFor = 0f;
            _offTrackFor = 0f;

            // Dead centre stays dead centre: a car stalled in the middle of the road backs out in a
            // straight line, which is both what it wants and the least it can be trusted with.
            _reverseSteer = Mathf.Abs(_lastSteer) > CentredSteer ? -_lastSteer : 0f;
            _lastSteer = _reverseSteer;
        }

        /// <summary>
        /// Hold the reverse for <see cref="ReverseSeconds"/>, then let go of it. Letting go means
        /// releasing the brake rather than meeting it with throttle: the latch in
        /// <see cref="VehicleController"/> comes off on a released brake, so the frame after the
        /// attempt has run out finds a car in a gear rather than one still being held backwards.
        /// </summary>
        private void ApplyRecovery(float dt)
        {
            _recoverFor += dt;
            _car.SetTestControls(0f, 1f, _reverseSteer);

            if (_recoverFor < ReverseSeconds)
            {
                return;
            }

            _recovering = false;
            _recoverFor = 0f;
            _lastSteer = 0f;
            _car.SetTestControls(0f, 0f, 0f);
        }

        /// <summary>
        /// Stand the car back up on the centreline at the distance it had already reached, in the lane
        /// it is holding, with nothing moving. The laps it has done are kept and only the mishap is
        /// taken away, which is why this goes to <see cref="_trackDistance"/> rather than to the grid.
        ///
        /// The only writing to a body that is not the driving, and it is written to
        /// <see cref="_car"/> and nothing else — the player's car is armed and fished out by
        /// <see cref="RaceSession"/>, which never hears about this one.
        /// </summary>
        private void ResetToTrack(string reason)
        {
            ResetCount++; // Counted here rather than at the callers: every predicament ends here.
            _recovering = false;
            _recoverFor = 0f;
            _stuckFor = 0f;
            _offTrackFor = 0f;
            _flippedFor = 0f;
            _recoveryAttempts = 0;
            _lastSteer = 0f;

            // The brake comes off before the body moves, so the reverse latch has a control frame to
            // come off in and the car is not driven backwards out of the pose it has just been put in.
            _car.SetTestControls(0f, 0f, 0f);

            float distance = _trackDistance;
            Pose centre = TrackLayout.PoseAt(distance);
            Vector3 position = centre.position + centre.right * _laneOffset
                               + Vector3.up * TrackLayout.SpawnHeight;

            Rigidbody body = _car.PhysicsBody;
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = position;
                body.rotation = centre.rotation;
            }

            _car.transform.SetPositionAndRotation(position, centre.rotation);

            // The metres between the barrier and the centreline are metres the car was carried, not
            // driven, so the odometer follows the body to the pose it has just been stood in and its value
            // is left exactly where it was. Putting the car back is not a lap, and it is not a penalty
            // either: the place it had come to is what it keeps.
            _progress.Rebase(position);

            // So a car that comes to grief again in the next corner has somewhere sensible to come
            // back to, were anything ever to reset it by that route.
            _car.ArmSpawnPoint();

            Debug.Log($"[AiDriver] {Name} reset to the centreline at {distance:F0} m: {reason}.");
        }

        // ---- where on the track --------------------------------------------------------

        /// <summary>
        /// The centreline as a closed list of equally spaced points, built once from
        /// <see cref="TrackLayout"/> and read-only from then on. Static because every car on the circuit
        /// is looking for itself on the same line, and that line is already static maths: a copy per car
        /// would only be more lists to keep in step with it.
        /// </summary>
        private static Pose[] _samples;

        /// <summary>
        /// <see cref="_sampleIndex"/> of the sample nearest <paramref name="position"/>. With a hint the
        /// search looks in a window around it and wraps over either end of the array, which is what keeps
        /// progress running forwards across the finish line — the sample nearest a car on the line is one
        /// just around it, and a search that could not wrap would find it too and then lose track of which
        /// side of the lap it was on.
        /// </summary>
        private static int NearestSample(Pose[] samples, Vector3 position, int hint)
        {
            int count = samples.Length;

            if (hint >= 0 && hint < count)
            {
                int best = hint;
                float bestSqr = float.PositiveInfinity;

                for (int step = -SearchWindowSamples; step <= SearchWindowSamples; step++)
                {
                    int index = Wrap(hint + step, count);
                    float sqr = (samples[index].position - position).sqrMagnitude;

                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = index;
                    }
                }

                // The window reaches as far as a car can move between two frames. Shorter than that is
                // an answer; longer means the car is not anywhere near where it was last — reset,
                // teleport, or the first frame after a respawn — and only then is the whole loop worth
                // walking.
                float reach = SearchWindowSamples * TrackLayout.RealisedSampleSpacing();
                if (bestSqr <= reach * reach)
                {
                    return best;
                }
            }

            int nearest = 0;
            float nearestSqr = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                float sqr = (samples[i].position - position).sqrMagnitude;

                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = i;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Distance round the lap of the centreline point nearest <paramref name="position"/>, worked
        /// along the two chords touching <paramref name="index"/> rather than taken from the sample
        /// itself. The samples are 2 m apart and a target point that arrived in 2 m strides would have
        /// the car sawing at the wheel twice a second down a straight.
        /// </summary>
        private static float DistanceAt(Pose[] samples, int index, Vector3 position)
        {
            int count = samples.Length;
            float spacing = TrackLayout.RealisedSampleSpacing();

            float forward = ChordDistance(samples[index].position, index * spacing,
                samples[Wrap(index + 1, count)].position, position, out float forwardSqr);
            float back = ChordDistance(samples[Wrap(index - 1, count)].position,
                Wrap(index - 1, count) * spacing, samples[index].position, position, out float backSqr);

            return backSqr < forwardSqr ? WrapDistance(back) : WrapDistance(forward);
        }

        /// <summary>
        /// Distance round the lap of the closest point on the chord from <paramref name="from"/> to
        /// <paramref name="to"/>, given the distance of <paramref name="from"/> itself. A chord is not
        /// the arc it stands in for, but on a 60 m radius at a 2 m pitch the two differ by a millimetre.
        /// </summary>
        private static float ChordDistance(Vector3 from, float fromDistance, Vector3 to,
            Vector3 position, out float sqrMagnitude)
        {
            Vector3 chord = to - from;
            float length = chord.magnitude;

            float along = length > 1e-5f
                ? Mathf.Clamp01(Vector3.Dot(position - from, chord) / (length * length))
                : 0f;

            Vector3 point = from + chord * along;
            sqrMagnitude = (point - position).sqrMagnitude;

            return fromDistance + along * length;
        }

        /// <summary>Index <paramref name="index"/> folded into one turn of the sample array.</summary>
        private static int Wrap(int index, int count)
        {
            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

        /// <summary>A distance round the lap folded into one lap of it.</summary>
        private static float WrapDistance(float distance)
        {
            float perimeter = TrackLayout.Length;
            float wrapped = distance % perimeter;
            return wrapped < 0f ? wrapped + perimeter : wrapped;
        }

        /// <summary>
        /// Refresh where this car is on the circuit: the nearest sample, the continuous distance round
        /// the lap of the centreline point nearest it, and the signed sideways offset off that point.
        /// </summary>
        private void UpdateTrackPosition(Vector3 position)
        {
            Pose[] samples = Samples();

            _sampleIndex = NearestSample(samples, position, _sampleIndex);
            _trackDistance = DistanceAt(samples, _sampleIndex, position);

            // The sideways offset comes off the centreline at the distance just worked out rather than
            // off the sample nearest it, so it does not inherit that sample's share of the 2 m pitch and
            // a car sitting neatly in its lane reads as sitting neatly in its lane.
            Pose centre = TrackLayout.PoseAt(_trackDistance);
            Vector3 across = position - centre.position;
            across.y = 0f;
            _lateral = Vector3.Dot(across, centre.right);
        }

        /// <summary>
        /// The cached centreline samples, built on first use — which is the first frame anything drives,
        /// rather than in a static initialiser that would have to run whether a track is in the scene or
        /// not.
        /// </summary>
        private static Pose[] Samples()
        {
            if (_samples != null)
            {
                return _samples;
            }

            int count = TrackLayout.SampleCount();
            float delta = TrackLayout.Length / count;

            var poses = new Pose[count];
            for (int i = 0; i < count; i++)
            {
                poses[i] = TrackLayout.PoseAt(i * delta);
            }

            _samples = poses;
            return poses;
        }

        /// <summary>
        /// How far round the lap the point nearest <paramref name="position"/> is, m — the same
        /// measurement a driver makes of its own car, offered for anything that has a position and wants
        /// to know where on the circuit it is. This is a place <em>round one lap</em>, and it wraps at the
        /// line: standing anything up that has to rank cars against each other needs the continuous
        /// distance of <see cref="TrackProgress"/>, which is how both <see cref="Progress"/> and
        /// <see cref="RaceSession.PlayerProgress"/> are kept.
        /// </summary>
        public static float TrackDistanceAt(Vector3 position)
        {
            Pose[] samples = Samples();
            return DistanceAt(samples, NearestSample(samples, position, NoSample), position);
        }

        /// <summary>
        /// Superseded by <see cref="TrackProgress"/>, and kept only so anything still compiled against it
        /// keeps compiling. It is the figure that was wrong: laps done times the perimeter plus the
        /// distance round the lap in progress mixes two references — the laps only start being counted
        /// when the line is first crossed, and the distance folds back to nought when it is crossed again
        /// — so the sum jumped by a whole lap at whichever of the two came first, and a car that had
        /// driven 818 m reported 1581. A place on the circuit has to be integrated from where the car
        /// started, which is what an odometer does and a snapshot of a lap counter cannot.
        ///
        /// Nothing in the race uses it: the HUD ranks the player's <see cref="RaceSession.PlayerProgress"/>
        /// against each opponent's <see cref="Progress"/>, both of them odometers.
        /// </summary>
        /// <param name="counter">
        /// The counter that has been fed this car's crossings; null reads as a car that has not started.
        /// </param>
        /// <param name="position">Where the car is.</param>
        [System.Obsolete(
            "Progress is a continuous odometer (TrackProgress). This snapshot jumps by a lap at the line.")]
        public static float ProgressOf(LapCounter counter, Vector3 position)
        {
            return LapsDoneForProgress(counter) * TrackLayout.Length + TrackDistanceAt(position);
        }

        /// <summary>
        /// The laps to count for <see cref="ProgressOf"/>, which is the only thing left that asks. The
        /// grid is behind the line and <see cref="LapCounter.IsRunning"/> turns on with the first crossing
        /// of it, so that crossing had to count as a lap done — on
        /// <see cref="LapCounter.CompletedLaps"/> alone every car's progress fell back to about nothing the
        /// moment it crossed, which is the patch that put the two references in one sum in the first place.
        /// Nothing that ranks a car any more goes through here.
        /// </summary>
        private static int LapsDoneForProgress(LapCounter counter)
        {
            if (counter == null)
            {
                return 0;
            }

            return counter.IsRunning ? counter.CompletedLaps + 1 : counter.CompletedLaps;
        }

        // ---- its own crossings ---------------------------------------------------------

        /// <summary>
        /// Subscribe this car's crossings of the track's gates: the same event the session listens to,
        /// for the same reason. The volumes lie across the whole road, so every car on the circuit
        /// reports every gate and only the body says whose lap is being told about.
        /// </summary>
        private void Subscribe(TrackCheckpoint[] checkpoints)
        {
            if (checkpoints == null)
            {
                return; // No track, no gates: the counter is there, it will simply never be fed.
            }

            var subscribed = new TrackCheckpoint[checkpoints.Length];
            for (int i = 0; i < checkpoints.Length; i++)
            {
                TrackCheckpoint checkpoint = checkpoints[i];
                if (checkpoint == null)
                {
                    continue; // Half-built track; the counter is sized for it anyway.
                }

                checkpoint.CrossedBy += OnCheckpointCrossed;
                subscribed[i] = checkpoint;
            }

            _subscribed = subscribed;
        }

        /// <summary>
        /// A gate swept by this car. Every other body that comes through the same volume is somebody
        /// else's lap and is dropped without being looked at.
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
                return;
            }

            _counter.OnCheckpoint(checkpoint.index, forward);
        }

        /// <summary>
        /// The lap clock ticks on the scaled clock, so it stops with the menu in front of it — the same
        /// rule <see cref="RaceSession"/> applies to the player's.
        /// </summary>
        private void Tick()
        {
            if (_counter != null && !GamePause.IsPaused)
            {
                _counter.Tick(Time.deltaTime);
            }
        }

        /// <summary>
        /// The gates outlive this component on the track, and the material its body was painted with is a
        /// runtime object with no asset to unload it, so both are freed here rather than left behind. The
        /// gates go first: a destroyed driver still subscribed would be handed crossings to feed a
        /// counter nobody reads.
        /// </summary>
        private void OnDestroy()
        {
            foreach (TrackCheckpoint checkpoint in _subscribed)
            {
                if (checkpoint != null)
                {
                    checkpoint.CrossedBy -= OnCheckpointCrossed;
                }
            }

            _subscribed = System.Array.Empty<TrackCheckpoint>();

            if (_bodyMaterial != null && Application.isPlaying)
            {
                Destroy(_bodyMaterial);
            }

            _bodyMaterial = null;
        }
    }
}
