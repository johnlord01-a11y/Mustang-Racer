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
    /// Diagnostics only: nothing here judges the car and every test ends in Assert.Pass, so what the
    /// fixture produces is a log — lines starting with <c>[Diag]</c> saying what each
    /// <see cref="WheelCollider"/> carries at rest and what the body does through a held lock, with the
    /// <see cref="CollisionProbe"/> on the chassis naming and timestamping every contact. Each manoeuvre
    /// runs once on the circuit a <see cref="TrackBuilder"/> lays and once on a bare wall-less plane from
    /// the same pose, and the turn is <c>HandlingMetricsPlayTests.SustainedKeyboardTurn_DoesNotSpin</c>
    /// input for input. Nothing is written to the rigid body to make a speed: throttle is the only way
    /// onto one, and every line reports what it actually delivered.
    /// </summary>
    [TestFixture]
    public sealed class HandlingDiagnosticsPlayTests
    {
        // Speeds, inputs and windows taken unchanged from the metrics fixture, so the two logs line
        // up: full throttle to the entry speed, then the lock and the throttle held for the window.
        private const float StaticRideSeconds = 2f, StaticDropHeightM = 1f, SettleSeconds = 0.6f;
        private const float PlaneScale = 50f;
        private const float ApproachThrottle = 1f, ApproachTimeoutSeconds = 6f;
        private const float ApproachStopToleranceMs = 0.1f, ApproachSettleSeconds = 0.25f;
        private const float TurnEntrySpeedMs = 20f, TurnThrottle = 0.5f, TurnSteer = 1f, TurnSeconds = 2.5f;
        private const float OrdinaryEntrySpeedMs = 15f, OrdinaryThrottle = 0.3f;
        private const float OrdinarySteer = 0.5f, OrdinarySeconds = 3f;

        /// <summary>Sampling period of a held turn, and the speed below which there is no slip to log.</summary>
        private const float SampleSeconds = 0.1f, SideslipMinimumSpeedMs = 3f;

        private GameObject _plane, _trackRoot, _carObject;
        private TrackBuilder _track;
        private VehicleController _car;
        private CollisionProbe _probe;

        /// <summary>Height of the driving surface (0 on both stages) and the pose every line of the
        /// log is measured along, across and from.</summary>
        private float _surfaceY;
        private Vector3 _linePosition, _lineForward, _lineRight;

        // Every window here is timed off the engine clock, so a fixture that left it stopped or paused
        // would have these tests wait on seconds that never arrive.
        [SetUp]
        public void SetUp() { Time.timeScale = 1f; }

        // Deferred destruction on purpose: the builder frees the materials it created in its own
        // OnDestroy, and this stage goes before the next test builds its own on top of it.
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_carObject != null) Object.Destroy(_carObject);
            if (_plane != null) Object.Destroy(_plane);
            if (_trackRoot != null) Object.Destroy(_trackRoot);
            _carObject = null; _car = null; _probe = null;
            _plane = null; _trackRoot = null; _track = null;
        }

        // ---- 1: what the wheels are carrying at rest -------------------------------------------

        /// <summary>Standing still on bare tarmac: each wheel's configured spring, damper, rest position,
        /// travel and radius; the sprung load <c>GetGroundHit</c> reports; and the compression the contact
        /// point implies — <c>1 - ((localHitY * -1 - radius) / travel)</c>, with the raw local hit height
        /// alongside so the formula can be checked rather than trusted. Mass, centre of mass and ride
        /// height go with them, because a load taken at the wrong height says nothing about the spring.</summary>
        [UnityTest]
        public IEnumerator Diag_StaticSuspension()
        {
            BuildBarePlane();
            BuildCarAt(new Vector3(0f, StaticDropHeightM, 0f), Quaternion.identity);
            yield return FixedSteps(StaticRideSeconds);

            LogWheel(_car.FrontLeftWheel);
            LogWheel(_car.FrontRightWheel);
            LogWheel(_car.RearLeftWheel);
            LogWheel(_car.RearRightWheel);

            Rigidbody body = _car.PhysicsBody;
            BoxCollider chassis = _carObject.GetComponent<BoxCollider>();
            float chassisFloor = chassis != null ? chassis.bounds.min.y - _surfaceY : float.NaN;
            Debug.Log($"[Diag] static body: mass {body.mass:F1} kg, centerOfMass {body.centerOfMass}, " +
                $"chassis origin {(_car.transform.position.y - _surfaceY):F3} m and box floor " +
                $"{chassisFloor:F3} m above the plane (y={_surfaceY:F2}), grounded wheels " +
                $"{_car.GroundedWheelCount}/4 after {StaticRideSeconds:F1} s");

            Assert.Pass("static suspension readout logged");
        }

        // ---- 2 and 3: the same sustained turn, with and without a circuit ------------------------

        /// <summary>The sustained keyboard turn of the metrics fixture on the built circuit: full
        /// throttle to 20 m/s down the start straight, then full lock and half throttle held 2.5 s,
        /// sampled every 0.1 s — walls included, because that is what the circuit is.</summary>
        [UnityTest]
        public IEnumerator Diag_SustainedTurn_OnTrack()
        {
            BuildTrackStage();
            yield return RunTurn("track-turn", TurnEntrySpeedMs, TurnThrottle, TurnSteer, TurnSeconds);
            Assert.Pass("sustained turn on the circuit logged");
        }

        /// <summary>The identical manoeuvre on a bare plane — same pose, approach, lock and window —
        /// with nothing to hit: whatever differs from <see cref="Diag_SustainedTurn_OnTrack"/> is the
        /// road surface, a wall or a trigger rather than the tyres.</summary>
        [UnityTest]
        public IEnumerator Diag_SustainedTurn_OpenPlane()
        {
            BuildPlaneStage();
            yield return RunTurn("plane-turn", TurnEntrySpeedMs, TurnThrottle, TurnSteer, TurnSeconds);
            Assert.Pass("sustained turn on the open plane logged");
        }

        // ---- 4: the turn a player actually takes -------------------------------------------------

        /// <summary>Half the lock and throttle of the sustained turn, entered 5 m/s slower and held half
        /// a second longer: the cornering that is most of a lap, on tarmac where nothing but the tyres
        /// can be blamed for what the log says about it.</summary>
        [UnityTest]
        public IEnumerator Diag_OrdinaryTurn_OpenPlane()
        {
            BuildPlaneStage();
            yield return RunTurn("plane-half-lock", OrdinaryEntrySpeedMs, OrdinaryThrottle,
                OrdinarySteer, OrdinarySeconds);
            Assert.Pass("ordinary turn on the open plane logged");
        }

        // ---- the two stages -----------------------------------------------------------------------

        /// <summary>The circuit as the bootstrap builds it — a <see cref="TrackBuilder"/> built if its
        /// own awake did not do it, and the car on <see cref="TrackBuilder.Spawn"/> with the reset pose
        /// armed — ground, road, walls and triggers all present, because contacts with any of them are
        /// part of what these runs are there to read out.</summary>
        private void BuildTrackStage()
        {
            _trackRoot = new GameObject("HandlingDiagnosticsTrackHarness");
            _track = _trackRoot.AddComponent<TrackBuilder>();
            if (!_track.HasBuilt) _track.Build();

            _surfaceY = 0f;   // the ground the builder lays has its surface on y = 0, road 0.1 m proud
            Pose spawn = _track.Spawn;
            BuildCarAt(spawn.position, spawn.rotation);
        }

        /// <summary>The bare plane with the car at the grid pose of <see cref="TrackLayout"/>: the same
        /// start, heading and height as the on-circuit run, so the two logs differ by the circuit alone.</summary>
        private void BuildPlaneStage()
        {
            BuildBarePlane();
            Pose spawn = TrackLayout.SpawnPose();
            BuildCarAt(spawn.position, spawn.rotation);
        }

        /// <summary>One big primitive plane and nothing else: no walls, no road ribbon, no triggers, and
        /// no edge the car can be run off inside a manoeuvre.</summary>
        private void BuildBarePlane()
        {
            _plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _plane.name = "DiagPlane";
            _plane.transform.localScale = new Vector3(PlaneScale, 1f, PlaneScale);
            _surfaceY = _plane.transform.position.y;
        }

        /// <summary>Car plus probe at <paramref name="position"/>: transform and rigid body moved
        /// together, controls parked straight ahead, and the axes every line is measured along and
        /// across taken from the pose the manoeuvre starts on.</summary>
        private void BuildCarAt(Vector3 position, Quaternion rotation)
        {
            _carObject = VehicleController.BuildCar(null);
            _carObject.transform.SetPositionAndRotation(position, rotation);

            _car = _carObject.GetComponent<VehicleController>();
            Assert.IsNotNull(_car.PhysicsBody, "BuildCar must deliver an assembled physics body");
            _car.PhysicsBody.position = position;
            _car.PhysicsBody.rotation = rotation;
            _car.ArmSpawnPoint();
            _car.SetTestControls(0f, 0f, 0f);
            _probe = _carObject.AddComponent<CollisionProbe>();

            _linePosition = position;
            _lineForward = Flatten(rotation * Vector3.forward);
            _lineRight = Flatten(rotation * Vector3.right);
        }

        // ---- one manoeuvre, sampled every 0.1 s -----------------------------------------------------

        /// <summary>Settle, reach the entry speed on the throttle alone, hold the turn while logging the
        /// body every <see cref="SampleSeconds"/>, then hand back the contact list the probe kept.</summary>
        private IEnumerator RunTurn(string label, float entrySpeedMs, float throttle, float steer, float seconds)
        {
            yield return FixedSteps(SettleSeconds);
            yield return AccelerateToSpeed(label, entrySpeedMs);
            yield return HoldTurnAndLog(label, throttle, steer, seconds);

            Debug.Log($"[Diag] {label} contacts: {(_probe == null ? "no probe fitted" : _probe.Describe())}");
        }

        /// <summary>Throttle until the speed the tyres have actually made reaches the target — never a
        /// velocity written to the body — then close it and let the rack and suspension settle, logging
        /// what the approach delivered and where along the straight it arrived.</summary>
        private IEnumerator AccelerateToSpeed(string label, float targetSpeedMs)
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

            bool reached = _car.CurrentSpeedMs >= stopAtSpeedMs;
            _car.SetTestControls(0f, 0f, 0f);
            yield return FixedSteps(ApproachSettleSeconds);

            Debug.Log($"[Diag] {label} approach: asked {targetSpeedMs:F0} m/s, target met {reached} in " +
                $"{held * FixedDelta():F2} s of full throttle, made {_car.CurrentSpeedMs:F1} m/s at " +
                $"{AlongM():F0} m along and {Mathf.Abs(AcrossM()):F1} m off the line, " +
                $"{_probe.ContactCount} contacts so far");
        }

        /// <summary>Hold <paramref name="steer"/> against <paramref name="throttle"/> for
        /// <paramref name="seconds"/> of fixed time, logging the body every <see cref="SampleSeconds"/>
        /// of it, and leave the rack and throttle parked when the window is done.</summary>
        private IEnumerator HoldTurnAndLog(string label, float throttle, float steer, float seconds)
        {
            _car.SetTestControls(throttle, 0f, steer);

            int every = Mathf.Max(1, Mathf.RoundToInt(SampleSeconds / FixedDelta()));
            int steps = StepsFor(seconds);
            for (int step = 0; step < steps; step++)
            {
                yield return new WaitForFixedUpdate();
                if ((step + 1) % every == 0) Debug.Log(SampleLine(label, (step + 1) * FixedDelta()));
            }

            _car.SetTestControls(0f, 0f, 0f);
        }
        // ---- what a line is made of -----------------------------------------------------------------

        /// <summary>One wheel as the physics sees it: the spring, damper, rest position, travel and
        /// radius it was configured with, then the contact <c>GetGroundHit</c> holds — its load and the
        /// contact point in the wheel's own space, from which the compression is read as
        /// <c>1 - ((localHitY * -1 - radius) / travel)</c>. A wheel touching nothing logs NaN rather than
        /// a zero that would read as a soft spring.</summary>
        private void LogWheel(WheelCollider wheel)
        {
            if (wheel == null)
            {
                Debug.Log("[Diag] static wheel: a wheel collider is missing");
                return;
            }

            var spring = wheel.suspensionSpring;
            bool grounded = wheel.GetGroundHit(out WheelHit hit);
            float localHitY = grounded ? wheel.transform.InverseTransformPoint(hit.point).y : float.NaN;
            float compression = grounded
                ? 1f - ((localHitY * -1f - wheel.radius) / Mathf.Max(1e-4f, wheel.suspensionDistance))
                : float.NaN;

            Debug.Log($"[Diag] static {wheel.name}: spring {spring.spring:F1} damper {spring.damper:F2} " +
                $"target {spring.targetPosition:F2} travel {wheel.suspensionDistance:F3} " +
                $"radius {wheel.radius:F3} contact {grounded} load {(grounded ? hit.force : float.NaN):F0} N " +
                $"compression {compression:F3} from hit local y {localHitY:F3} " +
                $"(world y {(grounded ? hit.point.y : float.NaN):F3})");
        }

        /// <summary>One sample of the body: time, speed, sideslip — "n/a" below
        /// <see cref="SideslipMinimumSpeedMs"/>, where the direction travelled is whatever way a
        /// standstill last happened to be sliding — yaw rate about the world up axis read off the body so
        /// it needs no unwrapping at the wrap, wheels down, metres across and along the centreline the
        /// manoeuvre started on, and the contacts so far with the first named and timestamped, so a line
        /// that is really a wall bounce reads as one instead of as a sudden change of direction.</summary>
        private string SampleLine(string label, float elapsedSeconds)
        {
            Vector3 travelled = Flatten(_car.PhysicsBody.linearVelocity);
            Vector3 nose = Flatten(_car.transform.forward);
            bool slipping = travelled.magnitude >= SideslipMinimumSpeedMs && nose.sqrMagnitude > 1e-6f;
            string slip = slipping ? Vector3.Angle(nose, travelled).ToString("F1") : "n/a";
            string first = _probe.ContactCount > 0
                ? $"{_probe.FirstColliderName}@{_probe.FirstTimeSeconds:F2}s"
                : "none";

            return $"[Diag] {label} t={elapsedSeconds:F2}s speed={_car.CurrentSpeedMs:F2} slip={slip} " +
                $"yaw={Mathf.Abs(_car.PhysicsBody.angularVelocity.y) * Mathf.Rad2Deg:F0}deg/s " +
                $"grounded={_car.GroundedWheelCount} lateral={Mathf.Abs(AcrossM()):F2}m " +
                $"along={AlongM():F1}m contacts={_probe.ContactCount} first={first}";
        }

        /// <summary>Across, signed, and along the centreline the manoeuvre was started on.</summary>
        private float AcrossM() { return Vector3.Dot(_car.transform.position - _linePosition, _lineRight); }

        private float AlongM() { return Vector3.Dot(_car.transform.position - _linePosition, _lineForward); }

        /// <summary>Hold for <paramref name="seconds"/> of engine time counted in fixed steps — the same
        /// clock the car's physics runs on, so a log line means the same thing on any machine.</summary>
        private static IEnumerator FixedSteps(float seconds)
        {
            for (int step = 0, steps = StepsFor(seconds); step < steps; step++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private static int StepsFor(float seconds) { return Mathf.Max(1, Mathf.CeilToInt(seconds / FixedDelta())); }

        private static float FixedDelta() { return Mathf.Max(0.005f, Time.fixedDeltaTime); }

        private static Vector3 Flatten(Vector3 vector) { return Vector3.ProjectOnPlane(vector, Vector3.up); }
    }
    /// <summary>
    /// Records every <see cref="OnCollisionEnter"/> and <see cref="OnCollisionStay"/> the chassis makes
    /// with a collider that is not its own — when it happened, what was hit, and whether the contact was
    /// new or being held. A <see cref="WheelCollider"/> is not part of it, because Unity reports wheel
    /// contacts only with <c>provideEvents</c> on, so what lands here is the chassis
    /// <see cref="BoxCollider"/> meeting a wall, a road piece or the ground — exactly what a log line
    /// that blames the tyres has to have ruled out first.
    /// </summary>
    public sealed class CollisionProbe : MonoBehaviour
    {
        /// <summary>Contacts kept per run, oldest first; a car scraped along a wall cannot grow it freely.</summary>
        private const int MaxRecordedContacts = 48;

        private readonly List<string> _contacts = new List<string>();
        private HashSet<Collider> _own;

        /// <summary>Contacts with anything outside the car so far, enter and stay counted together, with
        /// when the first landed, what it was on, and when the last was — NaN and "none" until one does.</summary>
        public int ContactCount { get; private set; }
        public float FirstTimeSeconds { get; private set; } = float.NaN;
        public float LastTimeSeconds { get; private set; } = float.NaN;
        public string FirstColliderName { get; private set; } = "none";

        private void Awake() { _own = new HashSet<Collider>(GetComponentsInChildren<Collider>(true)); }

        private void OnCollisionEnter(Collision collision) { Note(collision, "enter"); }

        private void OnCollisionStay(Collision collision) { Note(collision, "stay"); }

        /// <summary>The contacts in the order they happened, for the closing line of a manoeuvre.</summary>
        public string Describe()
        {
            if (ContactCount == 0) return "none with anything outside the car";

            int shown = Mathf.Min(MaxRecordedContacts, _contacts.Count);
            string tail = ContactCount > shown ? $"; +{ContactCount - shown} more" : string.Empty;
            return $"{ContactCount} contacts, first at {FirstTimeSeconds:F2}s on {FirstColliderName}: " +
                string.Join("; ", _contacts.GetRange(0, shown)) + tail;
        }

        /// <summary>One contact, if it was with something outside the car.</summary>
        private void Note(Collision collision, string kind)
        {
            if (_own == null) _own = new HashSet<Collider>(GetComponentsInChildren<Collider>(true));

            Collider other = collision.collider != null ? collision.collider : collision.collider;
            if (IsPartOfCar(other)) return;

            string name = other != null ? other.name : "<unnamed>";
            ContactCount++;
            LastTimeSeconds = Time.time;
            if (float.IsNaN(FirstTimeSeconds))
            {
                FirstTimeSeconds = Time.time;
                FirstColliderName = name;
            }
            if (_contacts.Count < MaxRecordedContacts) _contacts.Add($"{kind}@{Time.time:F2}s:{name}");
        }

        /// <summary>Anything this object carries — chassis, wheels, whatever a later stage hangs off it —
        /// is not a collision worth logging, which keeps the car's own parts quiet against each other.</summary>
        private bool IsPartOfCar(Collider collider)
        {
            if (collider == null || _own.Contains(collider)) return true;

            Transform other = collider.transform;
            return other == transform || other.IsChildOf(transform) || transform.IsChildOf(other);
        }
    }
}
