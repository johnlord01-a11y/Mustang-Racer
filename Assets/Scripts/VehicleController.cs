using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenWheelRacer
{
    /// <summary>
    /// WheelCollider vehicle physics per PLAN.md Stage 1:
    /// Rigidbody chassis + 4 WheelColliders, speed-sensitive steering,
    /// power-curve roll-off, engine braking, handbrake, downforce, reset.
    ///
    /// Scene graph (auto-built if not pre-authored):
    ///   Car_Primitive (Rigidbody + this + BoxCollider chassis)
    ///     Wheel_FL / Wheel_FR   (steered)
    ///     Wheel_RL / Wheel_RR   (handbrake axle)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleController : MonoBehaviour
    {
        [SerializeField] private VehicleTuning tuning;
        [SerializeField] private InputActionAsset inputAssetOverride;

        private readonly WheelCollider[] _wheels = new WheelCollider[4]; // 0=FL 1=FR 2=RL 3=RR
        private static readonly bool[] IsFront = { true, true, false, false };

        private VehicleInput _input;
        private Rigidbody _rigidbody;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        private float _currentSteerAngle;
        private float _currentSpeed;

        // Headless referee overrides (see SetTestControls / StepTestFrame).
        private bool _testControls;
        private float _testThrottle, _testBrake, _testSteer;
        private bool _testHandbrake;

        public VehicleTuning Tuning => tuning;
        public float CurrentSpeedMs => _currentSpeed;
        public float CurrentSteerAngle => _currentSteerAngle;
        public int GroundedWheelCount { get; private set; }

        /// <summary>Exposed for the headless referee (velocity kicks, teleports).</summary>
        public Rigidbody PhysicsBody => _rigidbody;

        public WheelCollider FrontLeftWheel => _wheels[0];
        public WheelCollider FrontRightWheel => _wheels[1];
        public WheelCollider RearLeftWheel => _wheels[2];
        public WheelCollider RearRightWheel => _wheels[3];

        private bool _initialized;

        /// <summary>
        /// Factory used by editor bootstrap AND PlayMode tests: builds an
        /// inactive chassis so tuning is assigned before anything reads it.
        /// Unity 6 edit mode never calls Awake on script-added components,
        /// so Initialize() is also invoked explicitly — it is idempotent.
        /// </summary>
        public static GameObject BuildCar(VehicleTuning tuningOrNull)
        {
            var go = new GameObject("Car_Primitive");
            go.SetActive(false);
            go.AddComponent<Rigidbody>();
            var chassisCollider = go.AddComponent<BoxCollider>();
            chassisCollider.center = new Vector3(0f, 0.35f, 0f);
            chassisCollider.size = new Vector3(1.7f, 0.6f, 3.7f);

            var controller = go.AddComponent<VehicleController>();
            controller.tuning = tuningOrNull;
            go.SetActive(true); // In play, Awake() runs here with tuning wired.

            // In edit mode no lifecycle message fires (and asset-import domain
            // reloads can swap the component), so re-fetch and initialize now.
            go.GetComponent<VehicleController>().Initialize();
            return go;
        }

        public static VehicleTuning CreateDefaultTuning()
        {
            var t = ScriptableObject.CreateInstance<VehicleTuning>();
            t.name = "VehicleTuning_Runtime";
            return t;
        }

        // --------------------------------------------------------------- setup

        private void Awake()
        {
            Initialize();
        }

        /// <summary>
        /// Idempotent setup for both Unity's Awake and editor harnesses that
        /// never get lifecycle messages. Safe to call twice; second call no-ops.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (tuning == null)
            {
                Debug.LogWarning("[VehicleController] No VehicleTuning assigned; using runtime defaults.");
                tuning = CreateDefaultTuning();
            }

            _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody == null) _rigidbody = gameObject.AddComponent<Rigidbody>();

            BuildPhysicsBody();
            BuildWheels();

            _input = new VehicleInput();
            _input.Initialize(inputAssetOverride);
        }

        private void Start()
        {
            ArmSpawnPoint();
        }

        private void OnDestroy()
        {
            _input?.Shutdown();
        }

        private void BuildPhysicsBody()
        {
            _rigidbody.mass = tuning.ChassisMassKg;
            _rigidbody.centerOfMass = tuning.CenterOfMassOffset;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        private void BuildWheels()
        {
            _wheels[0] = GetOrBuildWheel("Wheel_FL", tuning.WheelFrontLeft);
            _wheels[1] = GetOrBuildWheel("Wheel_FR", tuning.WheelFrontRight);
            _wheels[2] = GetOrBuildWheel("Wheel_RL", tuning.WheelRearLeft);
            _wheels[3] = GetOrBuildWheel("Wheel_RR", tuning.WheelRearRight);

            for (int i = 0; i < 4; i++)
            {
                ConfigureWheel(_wheels[i], tuning);
            }

            // Unity 6.6: substep config moved from static Rigidbody to the wheel
            // itself; one call configures the vehicle's physics scene.
            _wheels[0].ConfigureVehicleSubsteps(5f, 10, 15);
        }

        /// <summary>
        /// Single source of truth for per-wheel setup. Used by runtime BuildWheels
        /// AND the editor bootstrap that authors the prefab, so the prefab and a
        /// runtime-rebuilt car can never drift apart.
        /// </summary>
        public static void ConfigureWheel(WheelCollider w, VehicleTuning tuning)
        {
            var side = w.sidewaysFriction;
            side.stiffness = tuning.LateralGripMultiplier * 2f; // 2 = Unity default
            w.sidewaysFriction = side;

            var fwd = w.forwardFriction;
            fwd.stiffness = tuning.LongitudinalGripMultiplier * 2f;
            w.forwardFriction = fwd;

            w.radius = tuning.WheelRadius;
            w.suspensionDistance = tuning.SuspensionTravel;

            // Field-wise so this compiles against both the legacy SoftJointLimit
            // and Unity 6's nested SuspensionSpring struct.
            var spring = w.suspensionSpring;
            spring.spring = tuning.SuspensionSpring;
            spring.damper = tuning.SuspensionDamper;
            spring.targetPosition = tuning.SuspensionDistance;
            w.suspensionSpring = spring;
        }

        private WheelCollider GetOrBuildWheel(string name, Vector3 localPos)
        {
            Transform child = transform.Find(name);
            if (child == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.localPosition = localPos;
                return go.AddComponent<WheelCollider>();
            }
            var existing = child.GetComponent<WheelCollider>();
            return existing != null ? existing : child.gameObject.AddComponent<WheelCollider>();
        }

        // -------------------------------------------------------------- update

        private void Update()
        {
            if (_input != null && _input.ResetPressed)
            {
                ResetToSpawn();
            }
        }

        private void FixedUpdate()
        {
            if (_input == null || tuning == null) return;
            ApplyControlFrame(Time.fixedDeltaTime, readInput: true);
        }

        /// <summary>
        /// One control frame: read controls, set steer/motor/brake on the wheels,
        /// apply downforce. Shared by the live FixedUpdate path and the headless
        /// referee's deterministic stepping in <see cref="StepTestFrame"/>.
        /// </summary>
        private void ApplyControlFrame(float dt, bool readInput)
        {
            float throttle, brake, steerInput;
            bool handbrake;

            if (readInput && _input != null)
            {
                throttle = _input.ThrottleValue;
                brake = _input.BrakeValue;
                handbrake = _input.HandbrakePressed;
                steerInput = _input.SteerAxis;
            }
            else
            {
                throttle = _testThrottle;
                brake = _testBrake;
                handbrake = _testHandbrake;
                steerInput = _testSteer;
            }

            _currentSpeed = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, Vector3.up).magnitude;

            ApplySteering(steerInput, dt);
            ApplyDriveAndBrake(throttle, brake, handbrake);
            ApplyDownforce();
            UpdateGroundedCount();
        }

        private void ApplySteering(float steerInput, float dt)
        {
            // Speed-sensitive: full lock crawling, short flick at speed.
            float t = Mathf.InverseLerp(0f, tuning.HighSpeedSteerThreshold, _currentSpeed);
            float maxAngle = Mathf.Lerp(tuning.MaxSteerAngle, tuning.HighSpeedSteerAngle, t);
            float targetAngle = steerInput * maxAngle;

            // Rate-limited rack so direction changes never snap the body.
            _currentSteerAngle = Mathf.MoveTowardsAngle(
                _currentSteerAngle, targetAngle, tuning.SteerRate * dt);

            for (int i = 0; i < 4; i++)
            {
                _wheels[i].steerAngle = IsFront[i] ? _currentSteerAngle : 0f;
            }
        }

        private void ApplyDriveAndBrake(float throttle, float brake, bool handbrake)
        {
            // Engine curve: full force off the line, fading toward TopSpeedMs.
            float speedT = Mathf.Clamp01(_currentSpeed / tuning.TopSpeedMs);
            float powerCurve = 1f - speedT * speedT;

            // DriveForcePerWheel is a contact-patch force (N); torque via radius.
            float driveTorque = throttle > 0.01f
                ? throttle * tuning.DriveForcePerWheel * powerCurve * tuning.WheelRadius
                : 0f;

            for (int i = 0; i < 4; i++)
            {
                WheelCollider w = _wheels[i];
                bool driven = tuning.AllWheelDrive || IsFront[i] == false;

                w.motorTorque = driven ? driveTorque : 0f;

                float brakeTorque = 0f;
                if (brake > 0.01f)
                {
                    brakeTorque = brake * tuning.BrakeTorquePerWheel;
                }
                else if (throttle < 0.01f)
                {
                    brakeTorque = tuning.EngineBrakeTorque; // lift-off drag
                }
                if (handbrake && !IsFront[i])
                {
                    brakeTorque = Mathf.Max(brakeTorque, tuning.BrakeTorquePerWheel); // rear lock-up
                }
                w.brakeTorque = brakeTorque;
            }
        }

        private void ApplyDownforce()
        {
            float ratio = _currentSpeed / Mathf.Max(1f, tuning.DownforceRefSpeed);
            _rigidbody.AddForce(-Vector3.up * (tuning.Downforce * ratio * ratio), ForceMode.Force);
        }

        private void UpdateGroundedCount()
        {
            int grounded = 0;
            for (int i = 0; i < 4; i++)
            {
                if (_wheels[i] != null && _wheels[i].isGrounded) grounded++;
            }
            GroundedWheelCount = grounded;
        }

        // ------------------------------------------------ headless test hooks

        /// <summary>
        /// Override analog inputs without any Input System device. Used by the
        /// editor referee (and future PlayMode tests) to drive the car
        /// deterministically after <see cref="StepTestFrame"/>.
        /// </summary>
        public void SetTestControls(float throttle, float brake, float steer, bool handbrake = false)
        {
            _testControls = true;
            _testThrottle = throttle;
            _testBrake = brake;
            _testSteer = steer;
            _testHandbrake = handbrake;
        }

        /// <summary>
        /// One deterministic physics step for headless verification: applies one
        /// control frame from test controls, then advances the physics scene
        /// exactly once via Physics.Simulate. Requires
        /// Physics.simulationMode = Manual for the duration of the run.
        /// </summary>
        public void StepTestFrame(float dt)
        {
            if (tuning == null) return;
            ApplyControlFrame(dt, readInput: !_testControls);
            Physics.Simulate(dt);
            UpdateGroundedCount();
            _currentSpeed = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, Vector3.up).magnitude;
        }

        /// <summary>
        /// Capture the current pose as the reset target. Start() does this in
        /// play; editor harnesses call it explicitly after placing the car.
        /// </summary>
        public void ArmSpawnPoint()
        {
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        public void ResetToSpawn()
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.position = _spawnPosition;
            _rigidbody.rotation = _spawnRotation;
            _currentSteerAngle = 0f;
            Debug.Log("[VehicleController] Reset to spawn pose.");
        }
    }
}
