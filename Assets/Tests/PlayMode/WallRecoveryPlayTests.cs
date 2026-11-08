using System.Collections;
using NUnit.Framework;
using OpenWheelRacer;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// <summary>
    /// The way back out of a barrier. There was none: the drive only ever made a positive force, so a car
    /// nosed into a wall with W held was finished — the throttle pushed it harder into the wall, the brake
    /// only held it there, and <c>R</c> was the one door left. These run that mistake end to end in real
    /// play, on a bare plane with one <see cref="BoxCollider"/> wall stood in front of the car: the car is
    /// driven onto the wall, left pushing at it on the throttle, and then asked to come back out on the
    /// brake key alone — the key that has to do both jobs, because that is where the reverse lives.
    ///
    /// What the brake key is supposed to do instead is judged in the same breath, because the easy way to
    /// put a reverse in is to put it in where a stop was asked for: a car braked from speed has to be
    /// stopped before it ever goes backwards and must not be latched into the reverse while it is still
    /// travelling; a car rolling backwards and handed throttle has to be caught by the discs and then
    /// driven forwards out of the roll, not shoved backwards harder.
    ///
    /// The stage is this fixture's own copy of what <see cref="HandlingDiagnosticsPlayTests"/> builds: a
    /// primitive plane, <see cref="VehicleController.BuildCar"/> on the runtime tuning — which is where
    /// the reverse numbers come from — transform and rigid body placed together, the reset pose armed, the
    /// controls parked, and a <see cref="CollisionProbe"/> on the chassis to say when the nose has really
    /// arrived rather than nearly. Nothing is ever written to the rigid body to make a speed: the throttle
    /// is the only way onto one and the brake the only way off it, which is the whole point of a recovery.
    /// Windows are timed in <see cref="Time.fixedDeltaTime"/> steps of a live clock resumed with
    /// <see cref="WaitForFixedUpdate"/>, and every gate is logged with a <c>[Wall]</c> line before it is
    /// asserted, so a run of this fixture reads as a report of the manoeuvre either way.
    /// </summary>
    [TestFixture]
    public sealed class WallRecoveryPlayTests
    {
        // ---- harness -------------------------------------------------------------------------

        /// <summary>Fixed time the car is left alone to find its ride height before being driven.</summary>
        private const float SettleSeconds = 0.6f;

        /// <summary>Height the car is set down at: chassis centre clear of the surface, leaving it the
        /// same short drop to its ride height the grid pose gives it on the circuit.</summary>
        private const float CarSpawnHeightM = 1f;

        /// <summary>A Unity plane is 10 m across at scale 1, so this is a 500 m sheet of tarmac with no
        /// edge the car can be run off inside a manoeuvre — in either direction, which a reverse needs.</summary>
        private const float PlaneScale = 50f;

        // ---- the wall, and the manoeuvre onto it ------------------------------------------------

        /// <summary>The wall: 20 m across the front, 3 m up from the tarmac and 1 m thick, so nothing on
        /// a chassis that rides half a metre off the ground can be driven over, under or through it.</summary>
        private static readonly Vector3 WallSize = new Vector3(20f, 3f, 1f);

        /// <summary>How far ahead of the car the wall centre stands (m). Its near face is half a wall
        /// thickness back from there, and the nose of the chassis box reaches about 1.85 m of that gap.</summary>
        private const float WallDistanceM = 15f;

        /// <summary>Square on — the case as reported, a car driven straight at a barrier.</summary>
        private const float HeadOnWallYawDegrees = 0f;

        /// <summary>The glancing case: the same wall turned 30° about Y, where the car can be deflected
        /// along the barrier rather than stopped by it, and still has to come back out of it.</summary>
        private const float AngledWallYawDegrees = 30f;

        /// <summary>How long full throttle may be held before the nose has arrived, s.</summary>
        private const float ContactTimeoutSeconds = 6f;

        /// <summary>Fixed time the throttle is held on after the contact — the trapped case itself, a
        /// second of W against a wall with the car going nowhere and making no speed.</summary>
        private const float StuckSeconds = 1f;

        /// <summary>Fixed time the brake is then held alone for: the whole of the way back out.</summary>
        private const float ReverseWindowSeconds = 3f;

        /// <summary>Throttle or brake, held the way a keyboard holds it.</summary>
        private const float FullInput = 1f;

        /// <summary>How much clearance the car has to gain off the wall inside the window for it to count
        /// as driven away rather than twitched, m.</summary>
        private const float MinimumClearanceGainM = 2f;

        /// <summary>Signed forward speed (m/s) that counts as the car genuinely going backwards rather
        /// than easing off the wall it had been sprung off.</summary>
        private const float ReverseEvidenceSpeedMs = -0.5f;

        /// <summary>
        /// Largest one-fixed-step move of the body that is still motion and not a teleport. The reverse is
        /// capped at <see cref="VehicleTuning.ReverseTopSpeedMs"/> — 8 m/s on the tuning these runs use —
        /// and 0.5 m in a 50 Hz fixed step would be 25 m/s, so anything past this is a reset or a scripted
        /// shove wearing the disguise of a drive away, which is exactly what these tests must not accept.
        /// </summary>
        private const float MaximumBodyStepM = 0.5f;

        // ---- braking from speed: the stop first, the reverse second ----------------------------------

        /// <summary>Speed the open-plane brake run is taken from (m/s), and how far short of it the
        /// approach may arrive and still be the speed that was asked for.</summary>
        private const float BrakeEntrySpeedMs = 15f, ApproachSpeedToleranceMs = 0.5f;

        /// <summary>How long full throttle may be held to make that speed, s.</summary>
        private const float ApproachTimeoutSeconds = 10f;

        /// <summary>Fixed time the brake is held for while the order of things is watched: the stop, and
        /// then only the reverse that may follow it.</summary>
        private const float BrakingWindowSeconds = 6f;

        /// <summary>Signed forward speed the car has to come inside for the stop to count, m/s.</summary>
        private const float StoppedSpeedMs = 1f;

        /// <summary>Signed forward speed above which the brake key may never be latched into the reverse,
        /// however hard it is held. A car still doing walking pace down the plane is being stopped, and a
        /// reverse that arrives there is a drivetrain fighting its driver down the straight.</summary>
        private const float LatchSpeedCeilingMs = 1.5f;

        /// <summary>Signed forward speed that counts as the car having gone backwards at all, m/s —
        /// inside the band a stopped car rocks in, and outside anything the reverse has made.</summary>
        private const float BackwardEvidenceSpeedMs = -0.3f;

        // ---- throttle taken on a car that is rolling backwards ---------------------------------------

        /// <summary>Fixed time the brake is held from a standstill to get the car rolling backwards before
        /// W is asked for it, s.</summary>
        private const float RollBackSeconds = 2f;

        /// <summary>Fixed time W is then held for, and the signed forward speed it has to have recovered
        /// to by the end of it — far enough past the roll it was given to be driving, not stalling.</summary>
        private const float RecoverySeconds = 3f, MinimumRecoveredSpeedMs = 2f;

        // ---- stage -------------------------------------------------------------------------------

        /// <summary>The name the wall is built under, and the name looked for in the probe's first
        /// contact, so a nose scraped along the tarmac cannot be reported as an arrival at the wall.</summary>
        private const string WallName = "RecoveryWall";

        private GameObject _plane, _wall, _carObject;
        private BoxCollider _wallCollider;
        private VehicleController _car;
        private CollisionProbe _probe;

        /// <summary>How far the nose of the chassis box reaches ahead of the origin the clearance is
        /// measured to, read off the car rather than remembered — a car that grows must not quietly make
        /// every clearance here mean something else.</summary>
        private float _noseOverhangM;

        /// <summary>The wall manoeuvre the last run measured, filled by
        /// <see cref="RunIntoWallAndReverse"/> and judged by whichever test asked for it.</summary>
        private WallRun _run;

        // Deferred destruction as the other fixtures do it — the plane and the wall go the same way the
        // car does, this stage going before the next test builds one on top of it. Every window here is
        // timed off the engine clock, so a fixture that left it stopped or paused would have these tests
        // waiting on seconds that never arrive.
        [SetUp]
        public void SetUp() { Time.timeScale = 1f; }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_carObject != null) Object.Destroy(_carObject);
            if (_wall != null) Object.Destroy(_wall);
            if (_plane != null) Object.Destroy(_plane);
            _carObject = null; _car = null; _probe = null;
            _wall = null; _wallCollider = null; _plane = null; _run = null;
            _noseOverhangM = 0f;
        }

        // ---- the two stages ---------------------------------------------------------------------

        /// <summary>Bare plane, the car on the origin facing +Z, and a wall <see cref="WallDistanceM"/>
        /// ahead of it, square-on to the nose at <paramref name="wallYawDegrees"/>. The wall stands on the
        /// tarmac — base on the surface, top 3 m over it — because a barrier a car can be driven over is
        /// not the problem being tested, and 20 m of width is more than a car deflected along it can run
        /// off the end of inside a window.</summary>
        private void BuildWallStage(float wallYawDegrees)
        {
            BuildOpenPlaneStage();

            _wall = new GameObject(WallName);
            _wall.transform.position = new Vector3(0f, WallSize.y * 0.5f, WallDistanceM);
            _wall.transform.rotation = Quaternion.Euler(0f, wallYawDegrees, 0f);
            _wallCollider = _wall.AddComponent<BoxCollider>();
            _wallCollider.size = WallSize;
        }

        /// <summary>The same stage with nothing in front of the car: 500 m of tarmac, the car on the
        /// origin, and the probe on the chassis still counting anything it rubs.</summary>
        private void BuildOpenPlaneStage()
        {
            _plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _plane.name = "WallRecoveryPlane";
            _plane.transform.localScale = new Vector3(PlaneScale, 1f, PlaneScale);

            BuildCarAtOrigin();
        }

        /// <summary>Car plus probe on the origin facing +Z — transform and rigid body moved together, the
        /// reset pose armed and the controls parked — with the nose overhang read off the chassis box the
        /// car was actually built with, since that is what meets the wall.</summary>
        private void BuildCarAtOrigin()
        {
            var position = new Vector3(0f, CarSpawnHeightM, 0f);
            var rotation = Quaternion.identity;

            _carObject = VehicleController.BuildCar(null);
            _carObject.transform.SetPositionAndRotation(position, rotation);

            _car = _carObject.GetComponent<VehicleController>();
            Assert.IsNotNull(_car.PhysicsBody, "BuildCar must deliver an assembled physics body");
            _car.PhysicsBody.position = position;
            _car.PhysicsBody.rotation = rotation;
            _car.ArmSpawnPoint();
            _car.SetTestControls(0f, 0f, 0f);
            _probe = _carObject.AddComponent<CollisionProbe>();

            BoxCollider chassis = _carObject.GetComponent<BoxCollider>();
            Assert.IsNotNull(chassis, "the car is driven into the wall on its chassis box, so it needs one");
            _noseOverhangM = Mathf.Abs(chassis.center.z) + Mathf.Abs(chassis.size.z) * 0.5f;
        }

        // ---- what a wall contact and a distance off it mean ---------------------------------------

        /// <summary>
        /// The gap between the chassis origin and the near face of the wall, measured along the wall's own
        /// normal (m): positive in front of it, and down to the nose overhang once the nose is against it.
        /// The number that has to grow for the car to have driven away is therefore the distance travelled
        /// off the wall whichever way either of them is pointing; the centre-to-centre distance would turn
        /// the angled wall into an arc and call a slide along it an escape.
        /// </summary>
        private float ClearanceFromWallM()
        {
            Vector3 toCar = _car.transform.position - _wall.transform.position;
            return -Vector3.Dot(toCar, _wall.transform.forward) - _wallCollider.size.z * 0.5f;
        }

        /// <summary>Whether the nose is on the wall: the wall itself has been counted as a contact, or the
        /// clearance has closed to the nose box plus a hand's span of suspension travel.</summary>
        private bool NoseOnWall()
        {
            return HasWallContact() || ClearanceFromWallM() <= _noseOverhangM + 0.05f;
        }

        /// <summary>Whether what the probe has counted is the wall and not something else the car came
        /// alongside — the probe only ever names its first contact, and a floor scraped along the tarmac
        /// is not an arrival at a barrier.</summary>
        private bool HasWallContact()
        {
            return _probe.ContactCount > 0 && _probe.FirstColliderName != null
                && _probe.FirstColliderName.Contains(WallName);
        }

        /// <summary>Hold for <paramref name="seconds"/> of engine time counted in fixed steps — the same
        /// clock the car's physics runs on, so a window means the same thing on any machine.</summary>
        private static IEnumerator FixedSteps(float seconds)
        {
            for (int step = 0, steps = StepsFor(seconds); step < steps; step++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private static int StepsFor(float seconds) { return Mathf.Max(1, Mathf.CeilToInt(seconds / FixedDelta())); }

        private static float FixedDelta() { return Mathf.Max(0.005f, Time.fixedDeltaTime); }

        // ---- 1 and 2: driven onto a wall, asked back off it on the brake key --------------------------

        /// <summary>The reported case, square on: the car is driven onto the wall, left pushing at it on
        /// the throttle for a second, and then handed nothing but the brake — the key that both holds it
        /// there and has to walk it off again, and the only reason a player staring at a barrier is
        /// staring at one.</summary>
        [UnityTest]
        public IEnumerator HeadOn_WallContact_ThenHoldBrake_ReversesAway()
        {
            yield return RunIntoWallAndReverse("head-on", HeadOnWallYawDegrees);
            AssertDrivenOffTheWall("head-on");
        }

        /// <summary>The same manoeuvre on a wall turned 30° about Y. This is the one where the nose can be
        /// shoved along the barrier rather than stopped by it, so the car arrives at the brake with heading
        /// and speed it did not ask for — and it still has to come back out of it, rather than be scraped
        /// off the wall by its own deflection.</summary>
        [UnityTest]
        public IEnumerator Angled_30deg_WallContact_ReversesAway()
        {
            yield return RunIntoWallAndReverse("30deg", AngledWallYawDegrees);
            AssertDrivenOffTheWall("30deg");
        }

        /// <summary>
        /// A shallow glance: the car is aimed 10° at a long barrier (5–15° from parallel-to-path)
        /// and brought up to about 35 m/s so it slides along the face rather than stopping in it.
        /// It must not tunnel through, must keep meaningful forward speed, and must stay drivable.
        /// </summary>
        [UnityTest]
        public IEnumerator Shallow_10deg_WallGlance_SlidesWithoutTunneling()
        {
            const float innerFaceX = 12f;
            const float thickness = 1f;
            const float glanceYawDegrees = 10f;
            const float glanceSpeedMs = 35f;
            const float minRetainedSpeedMs = 14f;
            const float observeSeconds = 1.5f;
            const float farFaceX = innerFaceX + thickness;

            BuildOpenPlaneStage();

            _wall = new GameObject(WallName);
            _wall.transform.position = new Vector3(innerFaceX + thickness * 0.5f, WallSize.y * 0.5f, 60f);
            _wall.transform.rotation = Quaternion.identity;
            _wallCollider = _wall.AddComponent<BoxCollider>();
            _wallCollider.size = new Vector3(thickness, 3f, 160f);

            yield return FixedSteps(SettleSeconds);

            Vector3 pos = _car.PhysicsBody.position;
            Quaternion heading = Quaternion.Euler(0f, glanceYawDegrees, 0f);
            _carObject.transform.SetPositionAndRotation(pos, heading);
            _car.PhysicsBody.position = pos;
            _car.PhysicsBody.rotation = heading;
            _car.ArmSpawnPoint();
            _car.SetTestControls(0f, 0f, 0f);
            yield return new WaitForFixedUpdate();

            yield return AccelerateTo(glanceSpeedMs);
            Assert.GreaterOrEqual(_approachSpeedMs, 30f,
                $"never reached glance speed, only {_approachSpeedMs:F2} m/s in {_approachSeconds:F2} s " +
                $"(already on wall={NoseOnWall()}, x={_car.PhysicsBody.position.x:F2})");

            float untilContact = 0f;
            while (untilContact < 5f && !NoseOnWall())
            {
                _car.SetTestControls(FullInput, 0f, 0f);
                yield return new WaitForFixedUpdate();
                untilContact += FixedDelta();
            }

            Assert.IsTrue(NoseOnWall(),
                $"never touched the wall after {untilContact:F2} s (x={_car.PhysicsBody.position.x:F2}, " +
                $"z={_car.PhysicsBody.position.z:F2}, first={_probe.FirstColliderName})");

            float speedAtContact = _car.ForwardSpeed;
            float maxX = _car.PhysicsBody.position.x;
            float maxFwd = speedAtContact;
            float observe = 0f;
            bool tunneled = false;
            bool nan = false;
            _car.SetTestControls(FullInput, 0f, 0f);
            while (observe < observeSeconds)
            {
                yield return new WaitForFixedUpdate();
                observe += FixedDelta();
                float x = _car.PhysicsBody.position.x;
                float fwd = _car.ForwardSpeed;
                maxX = Mathf.Max(maxX, x);
                maxFwd = Mathf.Max(maxFwd, fwd);
                if (x > farFaceX) tunneled = true;
                if (float.IsNaN(x) || float.IsNaN(fwd)) nan = true;
            }

            Debug.Log($"[Wall] shallow-10deg: approach={_approachSpeedMs:F2} contactV={speedAtContact:F2} " +
                $"endV={_car.ForwardSpeed:F2} maxFwd={maxFwd:F2} maxX={maxX:F2} farFace={farFaceX:F2} " +
                $"tunneled={tunneled} grounded={_car.GroundedWheelCount} reversing={_car.IsReversing}");

            Assert.IsFalse(nan, "position or speed became NaN during the glance");
            Assert.IsFalse(tunneled,
                $"tunneled through the wall: max x {maxX:F2} m past the far face at {farFaceX:F2} m");
            Assert.Greater(_car.ForwardSpeed, minRetainedSpeedMs,
                $"lost forward speed after the glance: {_car.ForwardSpeed:F2} m/s at end " +
                $"(contact {speedAtContact:F2}, approach {_approachSpeedMs:F2})");
            Assert.GreaterOrEqual(_car.GroundedWheelCount, 2,
                $"not drivable after the glance: {_car.GroundedWheelCount} wheels grounded");
            Assert.IsFalse(_car.IsReversing, "glance latched the reverse; the car should still be travelling forward");
        }

        /// <summary>
        /// The manoeuvre, run the same way every time: full throttle until the nose is on the wall,
        /// <see cref="StuckSeconds"/> of throttle held there — the trapped state itself, W pushing at a
        /// barrier with the car going nowhere and making no speed — then the brake alone for
        /// <see cref="ReverseWindowSeconds"/>, watching the clearance off the wall, the signed forward
        /// speed, whether the reverse engaged at all, and the biggest single-fixed-step move the body made
        /// while it went. Measuring and logging only: the gates belong to the calling test, so a failure
        /// says which wall it was driven into.
        /// </summary>
        private IEnumerator RunIntoWallAndReverse(string label, float wallYawDegrees)
        {
            BuildWallStage(wallYawDegrees);
            yield return FixedSteps(SettleSeconds);

            var run = new WallRun();
            _run = run;

            // ---- drive until the nose has arrived, and never past what the window allows
            _car.SetTestControls(FullInput, 0f, 0f);
            while (run.ContactSeconds < ContactTimeoutSeconds && !NoseOnWall())
            {
                yield return new WaitForFixedUpdate();
                run.ContactSeconds += FixedDelta();
            }
            run.Contacted = NoseOnWall();

            Debug.Log($"[Wall] {label}: wall contact {run.Contacted} after {run.ContactSeconds:F2}s of full " +
                $"throttle — first contact {_probe.FirstColliderName} at {_probe.FirstTimeSeconds:F2}s, " +
                $"{_probe.ContactCount} contacts, wall among them {HasWallContact()}");

            // ---- keep pushing at it, which is the state the whole change is about
            yield return FixedSteps(StuckSeconds);
            run.StuckSpeedMs = _car.CurrentSpeedMs;
            run.StuckForwardSpeedMs = _car.ForwardSpeed;
            run.StuckClearanceM = ClearanceFromWallM();

            Debug.Log($"[Wall] {label}: pushed into it {StuckSeconds:F1}s on full throttle — speed " +
                $"{run.StuckSpeedMs:F2} m/s, signed forward speed {run.StuckForwardSpeedMs:F2} m/s, clearance " +
                $"{run.StuckClearanceM:F2} m with the nose box reaching {_noseOverhangM:F2} m of the wall, " +
                $"reversing {_car.IsReversing}");

            // ---- then the brake alone, which is where the way out has to be
            _car.SetTestControls(0f, FullInput, 0f);
            Vector3 previousBody = _car.PhysicsBody.position;
            run.MinForwardSpeedMs = _car.ForwardSpeed;
            run.EndClearanceM = run.StuckClearanceM;

            int steps = StepsFor(ReverseWindowSeconds);
            for (int step = 0; step < steps; step++)
            {
                yield return new WaitForFixedUpdate();

                Vector3 body = _car.PhysicsBody.position;
                run.MaxBodyStepM = Mathf.Max(run.MaxBodyStepM, Vector3.Distance(body, previousBody));
                previousBody = body;

                run.MinForwardSpeedMs = Mathf.Min(run.MinForwardSpeedMs, _car.ForwardSpeed);
                if (_car.IsReversing) run.EngagedReverse = true;
                run.EndClearanceM = ClearanceFromWallM();
            }
            run.ClearanceGainM = run.EndClearanceM - run.StuckClearanceM;

            Debug.Log($"[Wall] {label}: brake held alone {ReverseWindowSeconds:F1}s — clearance now " +
                $"{run.EndClearanceM:F2} m, a gain of {run.ClearanceGainM:F2} m; slowest signed forward speed " +
                $"{run.MinForwardSpeedMs:F2} m/s, reverse engaged {run.EngagedReverse}; biggest one-step body " +
                $"move {run.MaxBodyStepM:F3} m against the {MaximumBodyStepM:F2} m a teleport would clear; " +
                $"grounded wheels {_car.GroundedWheelCount}/4, contacts now {_probe.ContactCount}");
        }

        /// <summary>
        /// The four gates an escape has to clear, in the order they matter: the car did reach the wall, or
        /// there is no story; the brake key alone walked it more than <see cref="MinimumClearanceGainM"/>
        /// further out; it got there by going backwards at some point rather than by being sprung off; and
        /// it went by moving, every fixed step of it within what the reverse is capable of. That last one
        /// is here because <c>R</c> is still in the game: a reset that reports as a recovery is the failure
        /// a player would never notice, and the one that would let this test pass for the wrong reason.
        /// </summary>
        private void AssertDrivenOffTheWall(string label)
        {
            WallRun run = _run;

            Assert.IsTrue(run.Contacted,
                $"{label}: the car never arrived at the wall inside {ContactTimeoutSeconds:F0} s of full " +
                "throttle, so there was nothing to drive out of");
            Assert.IsTrue(run.EngagedReverse,
                $"{label}: the brake held on a car stopped against a wall never engaged the reverse — the " +
                $"signed forward speed got no lower than {run.MinForwardSpeedMs:F2} m/s");
            Assert.Less(run.MinForwardSpeedMs, ReverseEvidenceSpeedMs,
                $"{label}: the car was never genuinely going backwards — slowest signed forward speed " +
                $"{run.MinForwardSpeedMs:F2} m/s against the {ReverseEvidenceSpeedMs:F1} m/s wanted");
            Assert.Greater(run.ClearanceGainM, MinimumClearanceGainM,
                $"{label}: the brake did not drive the car off the wall — clearance went from " +
                $"{run.StuckClearanceM:F2} m to {run.EndClearanceM:F2} m, a gain of " +
                $"{run.ClearanceGainM:F2} m against the {MinimumClearanceGainM:F0} m asked for");
            Assert.LessOrEqual(run.MaxBodyStepM, MaximumBodyStepM,
                $"{label}: the body moved {run.MaxBodyStepM:F3} m in one fixed step, which is more than the " +
                "reverse can ever make — a reset or a scripted shove, not a drive away");
        }

        // ---- 3: the stop that has to come first --------------------------------------------------------

        /// <summary>
        /// The brake doing what it always did, which is the one thing a reverse could easily spoil. Held
        /// from <see cref="BrakeEntrySpeedMs"/> on open tarmac it has to bring the car down through every
        /// speed above the latch without once being latched into the reverse while the car is still
        /// travelling, and the car has to be stopped before it goes backwards at all. Both halves are the
        /// same bug seen from either end: a reverse that engages at speed is a drivetrain pulling against
        /// its driver mid-brake, and one that never engages is the trap this change exists to close.
        /// </summary>
        [UnityTest]
        public IEnumerator BrakeFromSpeed_StopsBeforeReversing()
        {
            BuildOpenPlaneStage();
            yield return FixedSteps(SettleSeconds);

            yield return AccelerateTo(BrakeEntrySpeedMs);
            Assert.GreaterOrEqual(_approachSpeedMs, BrakeEntrySpeedMs - ApproachSpeedToleranceMs,
                $"the throttle never made the {BrakeEntrySpeedMs:F0} m/s this is meant to be braked from — " +
                $"it made {_approachSpeedMs:F2} m/s in {_approachSeconds:F2} s");
            Assert.IsFalse(_car.IsReversing,
                $"the brake key was latched into the reverse at {_approachSpeedMs:F2} m/s with the brake " +
                "not even being held");

            float approachSpeedMs = _approachSpeedMs;
            float stoppedAtSeconds = float.NaN, backwardsAtSeconds = float.NaN;
            float minForwardSpeedMs = _car.ForwardSpeed, fastestLatchedMs = float.NegativeInfinity;

            _car.SetTestControls(0f, FullInput, 0f);
            int steps = StepsFor(BrakingWindowSeconds);
            for (int step = 0; step < steps; step++)
            {
                yield return new WaitForFixedUpdate();

                float elapsedSeconds = (step + 1) * FixedDelta();
                float forwardSpeedMs = _car.ForwardSpeed;
                minForwardSpeedMs = Mathf.Min(minForwardSpeedMs, forwardSpeedMs);

                // The fastest speed the reverse was ever found engaged at. It is allowed nowhere above
                // its own engage speed, however hard the key is held: up there it is not recovering
                // anything, it is pulling the other way on a car the driver is still stopping.
                if (_car.IsReversing) fastestLatchedMs = Mathf.Max(fastestLatchedMs, forwardSpeedMs);

                if (float.IsNaN(stoppedAtSeconds) && forwardSpeedMs < StoppedSpeedMs) stoppedAtSeconds = elapsedSeconds;
                if (float.IsNaN(backwardsAtSeconds) && forwardSpeedMs < BackwardEvidenceSpeedMs)
                {
                    backwardsAtSeconds = elapsedSeconds;
                }
            }

            Debug.Log($"[Wall] stop-order: braked from {approachSpeedMs:F2} m/s on the full key — stopped " +
                $"(under {StoppedSpeedMs:F1} m/s signed) at {At(stoppedAtSeconds)}, first read backwards " +
                $"(under {BackwardEvidenceSpeedMs:F1} m/s) at {At(backwardsAtSeconds)}; slowest signed " +
                $"forward speed {minForwardSpeedMs:F2} m/s, reverse never latched above " +
                $"{fastestLatchedMs:F2} m/s, speed at the end {_car.CurrentSpeedMs:F2} m/s");

            Assert.LessOrEqual(fastestLatchedMs, LatchSpeedCeilingMs,
                $"the reverse was engaged while the car was still doing {fastestLatchedMs:F2} m/s forward, " +
                $"above the {LatchSpeedCeilingMs:F1} m/s it may engage below: a brake that turns into a " +
                "reverse mid-stop is worse than a brake that never reverses at all");
            Assert.IsFalse(float.IsNaN(stoppedAtSeconds),
                $"the brake never brought the car under {StoppedSpeedMs:F1} m/s signed forward speed inside " +
                $"{BrakingWindowSeconds:F0} s of {approachSpeedMs:F2} m/s — slowest it got was " +
                $"{minForwardSpeedMs:F2} m/s");
            Assert.IsTrue(float.IsNaN(backwardsAtSeconds) || backwardsAtSeconds >= stoppedAtSeconds,
                $"the car was read going backwards ({BackwardEvidenceSpeedMs:F1} m/s at " +
                $"{At(backwardsAtSeconds)}) before it had stopped ({StoppedSpeedMs:F1} m/s at " +
                $"{At(stoppedAtSeconds)}): the key let go of the stop to drive the car backwards out of it");
        }

        /// <summary>The speed the last <see cref="AccelerateTo"/> made and how long the throttle was held
        /// for it. Nothing is written to the rigid body to make a speed anywhere in this fixture: the
        /// tyres are the only way onto one, which is precisely what makes the brake the thing being judged.</summary>
        private float _approachSpeedMs, _approachSeconds;

        /// <summary>Full throttle until the speed is made or the window runs out, and held there — the
        /// caller is about to ask for the brake, and the speed it is asked at is the speed under test.</summary>
        private IEnumerator AccelerateTo(float targetSpeedMs)
        {
            _car.SetTestControls(FullInput, 0f, 0f);

            _approachSpeedMs = _car.CurrentSpeedMs;
            _approachSeconds = 0f;
            while (_approachSeconds < ApproachTimeoutSeconds && _car.CurrentSpeedMs < targetSpeedMs)
            {
                yield return new WaitForFixedUpdate();
                _approachSeconds += FixedDelta();
                _approachSpeedMs = Mathf.Max(_approachSpeedMs, _car.CurrentSpeedMs);
            }

            Debug.Log($"[Wall] approach: asked {targetSpeedMs:F0} m/s, made {_approachSpeedMs:F2} m/s in " +
                $"{_approachSeconds:F2} s of full throttle (signed forward speed " +
                $"{_car.ForwardSpeed:F2} m/s, contacts {_probe.ContactCount})");
        }

        /// <summary>A moment inside a window, or the plain fact that it never happened.</summary>
        private static string At(float seconds)
        {
            return float.IsNaN(seconds) ? "never" : $"{seconds:F2}s";
        }

        // ---- 4: throttle taken on a car that is rolling backwards ---------------------------------------

        /// <summary>
        /// The other half of the one-key problem, and the half a player walks into without meaning to: W
        /// taken on a car still rolling backwards has to be caught by the discs and then driven forwards out
        /// of the roll. This is also the plain end-to-end proof that the reverse is a gear a driver has —
        /// the brake held from a standstill has to walk the car backwards at all, and throttle asked for
        /// from there has to have it travelling forwards again inside <see cref="RecoverySeconds"/>. Nothing
        /// is written to the body to help either half along, which is the only reason the second one means
        /// anything: a car that reverses because a test told it to prove nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator Throttle_WhileRollingBack_DrivesForwardAgain()
        {
            BuildOpenPlaneStage();
            yield return FixedSteps(SettleSeconds);

            _car.SetTestControls(0f, FullInput, 0f);
            yield return FixedSteps(RollBackSeconds);
            float rollSpeedMs = _car.ForwardSpeed;

            Assert.IsTrue(_car.IsReversing,
                "the brake held from a standstill never engaged the reverse, so there was no roll for W " +
                "to catch and nothing here being tested");
            Assert.Less(rollSpeedMs, ReverseEvidenceSpeedMs,
                $"the brake key never made the car roll backwards — signed forward speed was " +
                $"{rollSpeedMs:F2} m/s after {RollBackSeconds:F1} s of it, wanted under " +
                $"{ReverseEvidenceSpeedMs:F1} m/s");

            _car.SetTestControls(FullInput, 0f, 0f);
            yield return FixedSteps(RecoverySeconds);
            float recoveredSpeedMs = _car.ForwardSpeed;

            Debug.Log($"[Wall] roll-back: brake held {RollBackSeconds:F1}s from a standstill made " +
                $"{rollSpeedMs:F2} m/s signed, reverse engaged; W held {RecoverySeconds:F1}s from there ends " +
                $"at {recoveredSpeedMs:F2} m/s signed ({_car.CurrentSpeedMs:F2} m/s unsigned), reversing " +
                $"{_car.IsReversing}, grounded wheels {_car.GroundedWheelCount}/4, contacts {_probe.ContactCount}");

            Assert.Greater(recoveredSpeedMs, MinimumRecoveredSpeedMs,
                $"W taken at {rollSpeedMs:F2} m/s of backward roll still had the car at " +
                $"{recoveredSpeedMs:F2} m/s signed after {RecoverySeconds:F1} s — the discs caught the roll " +
                "at best and the drive never took over from them");
        }

        /// <summary>
        /// What one wall manoeuvre measured, kept together so the runner can measure and the calling test
        /// judge: whether the nose arrived and when, the clearance the car was sitting on while it pushed at
        /// the wall and the clearance the reverse window ended on, the slowest signed forward speed of the
        /// lot, whether the reverse engaged at all, and the largest one-fixed-step move the body made on the
        /// way. Plain fields — nothing outside this fixture ever reads them.
        /// </summary>
        private sealed class WallRun
        {
            public bool Contacted;
            public float ContactSeconds;
            public float StuckSpeedMs;
            public float StuckForwardSpeedMs;
            public float StuckClearanceM;
            public float EndClearanceM;
            public float ClearanceGainM;
            public float MinForwardSpeedMs = float.PositiveInfinity;
            public bool EngagedReverse;
            public float MaxBodyStepM;
        }
    }
}
