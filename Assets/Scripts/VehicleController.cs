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

        // PhysX WheelColliders with motorTorque exactly 0 pin a parked rigidbody
        // against hits; a token torque keeps the wheel solver awake without adding
        // meaningful drive (Ctl8: 1e-4 Nm raised a 21.6 m/s hit from ~0.3 to ~11 m/s).
        private const float RestingMotorTorque = 0.01f;

        private VehicleInput _input;
        private Rigidbody _rigidbody;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        private float _currentSteerAngle;
        private float _currentSpeed;
        private bool _restingWheelTorqueArmed;

        /// <summary>
        /// Signed speed along the chassis forward axis in m/s, taken with the rest of the control
        /// frame: positive is nose-first, negative is the car rolling backwards. <see cref="_currentSpeed"/>
        /// is the magnitude the HUD, the grip and the power curve are after; this is the figure the
        /// reverse latch needs, because throttle and reverse are keys the physics can only tell apart
        /// by which way the car is actually going.
        /// </summary>
        private float _currentForwardSpeed;

        /// <summary>
        /// Latched state of the brake key: whether it is driving the car backwards right now rather
        /// than slowing it. Latched rather than re-derived every frame so the car keeps backing out of
        /// the wall it has just been walked off instead of flipping to braking the instant the reverse
        /// crosses the engage speed — see <see cref="ApplyDriveAndBrake"/>.
        /// </summary>
        private bool _reversing;

        /// <summary>
        /// Signed forward speed (m/s) below which throttle taken by a car that is already rolling
        /// backwards gets the discs rather than the drive, so W stops the roll before driving off from
        /// it instead of spinning against a car still on its way the other way.
        /// </summary>
        private const float BackwardRollCutoverMs = -0.5f;

        /// <summary>Below this, a steer angle is treated as centred rather than locked either way.</summary>
        private const float SteerAngleEpsilon = 0.001f;

        /// <summary>
        /// Rear axle sideways stiffness last written to the WheelColliders by
        /// <see cref="ApplyDriveAndBrake"/>. Starts as NaN so the first control frame writes it and
        /// every later frame only writes when the handbrake actually changes the value.
        /// </summary>
        private float _rearSidewaysStiffness = float.NaN;

        // Headless referee overrides (see SetTestControls / StepTestFrame).
        private bool _testControls;
        private float _testThrottle, _testBrake, _testSteer;
        private bool _testHandbrake;

        public VehicleTuning Tuning => tuning;
        public float CurrentSpeedMs => _currentSpeed;

        /// <summary>
        /// Whether a person in front of a keyboard drives this car. Default true, which is every car
        /// this project built before there were opponents: <see cref="Update"/> listens for the R key
        /// and <see cref="FixedUpdate"/> reads the devices.
        ///
        /// Set false and the car is remote-controlled — an AI driver, a replay, an incident
        /// reconstruction — and the machines that a person drives stay somebody else's: the reset key
        /// is ignored, and the control frames take only what has been handed in through
        /// <see cref="SetTestControls"/>. Two reasons, both about a field of cars rather than one:
        /// every car built as a human car listens to the one shared keyboard, so a grid of three
        /// opponents was a grid of three cars resetting on the same keypress and steering wherever the
        /// player steered; and loading and enabling the input map per car lets any one of them take it
        /// away from the player when it is destroyed. An AI car has no use for the map at all, so
        /// <see cref="Initialize"/> does not build one.
        ///
        /// Public field rather than a property assigned in the inspector because the only writer so far
        /// sets it while building the car, before Unity has run any lifecycle message on it — see
        /// <see cref="BuildCar(VehicleTuning, bool)"/>.
        /// </summary>
        public bool HumanControlled = true;

        /// <summary>
        /// Signed speed along the chassis forward axis in m/s — positive nose-first, negative backwards
        /// — where <see cref="CurrentSpeedMs"/> is the magnitude. Both a stood-still car and a car pinned
        /// nose-first on a wall read 0 on the magnitude; only the sign says which way the brake key has
        /// to drive it, which is what the reverse latch and anything watching it are after.
        /// </summary>
        public float ForwardSpeed => _currentForwardSpeed;

        /// <summary>Whether the brake key is driving the car backwards this frame (reverse engaged).</summary>
        public bool IsReversing => _reversing;

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
        /// <param name="tuningOrNull">Tuning to wire in; runtime defaults when null.</param>
        /// <param name="humanControlled">
        /// Whether a person drives the car built. It has to be said here rather than assigned
        /// afterwards, because <see cref="HumanControlled"/> decides whether
        /// <see cref="Initialize"/> loads and enables the input map at all, and Awake — which is
        /// where initialization happens for a car built by activation — runs inside the
        /// <c>SetActive(true)</c> below. Left true, every caller that has ever called the one-argument
        /// form gets exactly the car it always got.
        /// </param>
        public static GameObject BuildCar(VehicleTuning tuningOrNull, bool humanControlled = true)
        {
            var go = new GameObject("Car_Primitive");
            go.SetActive(false);
            go.AddComponent<Rigidbody>();
            var chassisCollider = go.AddComponent<BoxCollider>();
            chassisCollider.center = new Vector3(0f, 0.35f, 0f);
            chassisCollider.size = new Vector3(1.7f, 0.6f, 3.7f);

            var controller = go.AddComponent<VehicleController>();
            controller.tuning = tuningOrNull != null ? tuningOrNull : ResolveDefaultTuning();
            controller.HumanControlled = humanControlled;
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

        /// <summary>
        /// The project default ScriptableObject when present under Resources; otherwise
        /// an in-memory instance with the same field defaults as <see cref="VehicleTuning"/>.
        /// Used by <see cref="BuildCar"/> so Awake never sees a null tuning.
        /// </summary>
        public static VehicleTuning ResolveDefaultTuning()
        {
            VehicleTuning loaded = Resources.Load<VehicleTuning>("VehicleTuning_Default");
            return loaded != null ? loaded : CreateDefaultTuning();
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
                // Spawn paths (BuildCar, scene objects) resolve the default asset or a
                // runtime instance so every car has real tuning before physics starts.
                tuning = ResolveDefaultTuning();
            }

            _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody == null) _rigidbody = gameObject.AddComponent<Rigidbody>();

            BuildPhysicsBody();
            BuildWheels();
            BuildVisualBody();

            // A car that never reads devices has no reason to load the map that reads them, and
            // several cars sharing one asset makes that more than a wasted load: each one enables
            // the same map and each one disables it again on destroy, so the first opponent torn
            // down would leave the player's keyboard disabled behind it.
            if (HumanControlled)
            {
                _input = new VehicleInput();
                _input.Initialize(inputAssetOverride);
            }
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
                ConfigureWheel(_wheels[i], tuning, IsFront[i]);
            }

            // Unity 6.6: substep config moved from static Rigidbody to the wheel
            // itself; one call configures the vehicle's physics scene.
            _wheels[0].ConfigureVehicleSubsteps(5f, 10, 15);
        }

        /// <summary>
        /// Single source of truth for per-wheel setup. Used by runtime BuildWheels
        /// AND the editor bootstrap that authors the prefab, so the prefab and a
        /// runtime-rebuilt car can never drift apart. <paramref name="isFront"/> selects
        /// the axle's sideways grip: front wheels run
        /// <see cref="VehicleTuning.LateralGripMultiplier"/>, rear wheels the higher
        /// <see cref="VehicleTuning.RearLateralGripMultiplier"/>, which is what keeps the
        /// tail planted.
        /// </summary>
        public static void ConfigureWheel(WheelCollider w, VehicleTuning tuning, bool isFront)
        {
            var side = w.sidewaysFriction;
            side.stiffness = (isFront ? tuning.LateralGripMultiplier
                                      : tuning.RearLateralGripMultiplier) * 2f; // 2 = Unity default
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

        // -------------------------------------------------------- visual body

        /// <summary>
        /// Name of the child every placeholder body part hangs under, and the handle
        /// <c>AiField</c> paints instead of adding a second body.
        /// </summary>
        public const string BodyRootName = "BodyVisual";

        /// <summary>Body colour of a car nobody has painted: racing red.</summary>
        public static readonly Color DefaultBodyColor = new Color(0.72f, 0.09f, 0.10f);

        /// <summary>
        /// Name of the empty the halo pieces hang under. Public because
        /// <c>AiField</c> paints a field car body-colour piece by piece and the halo
        /// is the one piece that stays dark grey on every car.
        /// </summary>
        public const string HaloRootName = "Halo";

        /// <summary>Titanium grey of the halo, on every car whatever else is painted.</summary>
        public static readonly Color HaloColor = new Color(0.16f, 0.16f, 0.18f);

        /// <summary>
        /// Name of the empty the tinted windshield hangs under. Like the halo it is
        /// exempt from <c>AiField</c>'s team-colour pass — glass is glass on every
        /// car — and it is kept below the cockpit sight line so the driver sees the
        /// track through the top of it, not smoked plastic.
        /// </summary>
        public const string GlassRootName = "Windshield";

        /// <summary>Smoked-glass tint of the windshield, same on every car.</summary>
        public static readonly Color GlassColor = new Color(0.09f, 0.11f, 0.14f);

        private static readonly string[] BodyShaderNames =
        {
            "Universal Render Pipeline/Lit", "Standard",
        };

        /// <summary>
        /// A placeholder F1 car cut out of primitives: a narrow tub with a pointed
        /// nose, stubby sidepods, an engine cover with an airbox over the driver's
        /// head, a halo over a closed-in cockpit — tinted windshield rising below the
        /// sight line, painted cowl and headrest shoulders around the helmet — a rear
        /// wing on two endplates and a front wing with its own endplates. Wheels stay
        /// exposed outside the body — that is what makes it read as an open-wheel car.
        ///
        /// Every piece is pure graphics: each primitive's collider is destroyed the
        /// moment it is created, so the car keeps wearing exactly one box and four
        /// wheels to physics, exactly as it did before there was anything to look at.
        /// Nothing here reads <c>tuning</c>; changing the shape cannot change the drive.
        /// </summary>
        private void BuildVisualBody()
        {
            if (transform.Find(BodyRootName) != null)
            {
                return; // Built already (editor path re-invokes Initialize).
            }

            var root = new GameObject(BodyRootName);
            root.transform.SetParent(transform, worldPositionStays: false);

            Material paint = CreateBodyPaint();

            // Chassis-local: +z nose, ground at y = 0, wheels at x = +/-0.75,
            // z = +1.20 / -1.25. Everything below stays inside x = +/-0.7 so the
            // wheels hang out in the open past the bodywork.
            AddVisualPart(root.transform, "Tub", PrimitiveType.Cube,
                new Vector3(0f, 0.33f, -0.05f), new Vector3(0.55f, 0.5f, 2.2f), paint);
            BuildNose(root.transform, paint);
            AddVisualPart(root.transform, "CockpitHelmet", PrimitiveType.Sphere,
                new Vector3(0f, 0.60f, 0.28f), new Vector3(0.28f, 0.28f, 0.28f), paint);
            BuildHalo(root.transform);
            BuildCockpit(root.transform, paint);
            AddVisualPart(root.transform, "Airbox", PrimitiveType.Cube,
                new Vector3(0f, 0.74f, -0.08f), new Vector3(0.24f, 0.30f, 0.34f), paint);
            AddVisualPart(root.transform, "EngineCoverUpper", PrimitiveType.Cube,
                new Vector3(0f, 0.52f, -0.78f), new Vector3(0.42f, 0.34f, 0.9f), paint);
            AddVisualPart(root.transform, "EngineCoverLower", PrimitiveType.Cube,
                new Vector3(0f, 0.40f, -1.38f), new Vector3(0.36f, 0.24f, 0.6f), paint);
            AddVisualPart(root.transform, "Sidepod_L", PrimitiveType.Cube,
                new Vector3(-0.56f, 0.30f, -0.10f), new Vector3(0.40f, 0.36f, 1.2f), paint);
            AddVisualPart(root.transform, "Sidepod_R", PrimitiveType.Cube,
                new Vector3(0.56f, 0.30f, -0.10f), new Vector3(0.40f, 0.36f, 1.2f), paint);
            AddVisualPart(root.transform, "RearWing", PrimitiveType.Cube,
                new Vector3(0f, 0.82f, -1.62f), new Vector3(1.10f, 0.05f, 0.40f), paint);
            AddVisualPart(root.transform, "RearWingEndplate_L", PrimitiveType.Cube,
                new Vector3(-0.55f, 0.62f, -1.62f), new Vector3(0.05f, 0.42f, 0.45f), paint);
            AddVisualPart(root.transform, "RearWingEndplate_R", PrimitiveType.Cube,
                new Vector3(0.55f, 0.62f, -1.62f), new Vector3(0.05f, 0.42f, 0.45f), paint);
            AddVisualPart(root.transform, "FrontWing", PrimitiveType.Cube,
                new Vector3(0f, 0.12f, 1.85f), new Vector3(1.44f, 0.05f, 0.45f), paint);
            AddVisualPart(root.transform, "FrontWingEndplate_L", PrimitiveType.Cube,
                new Vector3(-0.70f, 0.17f, 1.85f), new Vector3(0.05f, 0.15f, 0.42f), paint);
            AddVisualPart(root.transform, "FrontWingEndplate_R", PrimitiveType.Cube,
                new Vector3(0.70f, 0.17f, 1.85f), new Vector3(0.05f, 0.15f, 0.42f), paint);
        }

        /// <summary>
        /// The nose: an empty named "Nose" carrying a stack of cubes that get
        /// narrower and lower toward the front, run long from the front of the tub
        /// out to the front wing so the whole front reads as one tapered nose into
        /// the wing. Kept under y = 0.6 and below the cockpit sight line — the
        /// driver looks over this, not through it.
        /// </summary>
        private static void BuildNose(Transform body, Material paint)
        {
            Transform nose = AddVisualGroup(body, "Nose");

            AddVisualPart(nose, "NoseRoot", PrimitiveType.Cube,
                new Vector3(0f, 0.40f, 1.02f), new Vector3(0.34f, 0.36f, 0.34f), paint);
            AddVisualPart(nose, "NoseMid", PrimitiveType.Cube,
                new Vector3(0f, 0.35f, 1.34f), new Vector3(0.28f, 0.30f, 0.34f), paint);
            AddVisualPart(nose, "NoseFront", PrimitiveType.Cube,
                new Vector3(0f, 0.30f, 1.63f), new Vector3(0.22f, 0.24f, 0.30f), paint);
            AddVisualPart(nose, "NoseTip", PrimitiveType.Cube,
                new Vector3(0f, 0.24f, 1.86f), new Vector3(0.16f, 0.15f, 0.24f), paint);
        }

        /// <summary>
        /// The halo: an empty named "Halo" wearing thin dark grey pieces — two side
        /// rails at x = +/-0.40, a front band and a rear cross bar joining them into
        /// a hoop around the driver, and one slim centre pillar from the front of the
        /// hoop down to the tub. The hoop sits with its lower edge above the cockpit
        /// sight line and the pillar tops out below it, so a straight-ahead view from
        /// <see cref="CockpitView.EyeLocal"/> passes between them and sees the track,
        /// not the safety structure.
        /// </summary>
        private static void BuildHalo(Transform body)
        {
            Material dark = CreateHaloPaint();
            Transform halo = AddVisualGroup(body, HaloRootName);

            AddVisualPart(halo, "HaloRail_L", PrimitiveType.Cube,
                new Vector3(-0.40f, 0.83f, 0.02f), new Vector3(0.05f, 0.06f, 0.98f), dark);
            AddVisualPart(halo, "HaloRail_R", PrimitiveType.Cube,
                new Vector3(0.40f, 0.83f, 0.02f), new Vector3(0.05f, 0.06f, 0.98f), dark);
            AddVisualPart(halo, "HaloFrontBand", PrimitiveType.Cube,
                new Vector3(0f, 0.83f, 0.50f), new Vector3(0.85f, 0.06f, 0.08f), dark);
            AddVisualPart(halo, "HaloRearBar", PrimitiveType.Cube,
                new Vector3(0f, 0.83f, -0.46f), new Vector3(0.85f, 0.06f, 0.08f), dark);
            AddVisualPart(halo, "HaloCentrePillar", PrimitiveType.Cube,
                new Vector3(0f, 0.645f, 0.50f), new Vector3(0.06f, 0.25f, 0.07f), dark);
        }

        /// <summary>
        /// The closed-in cockpit: a group named "Windshield" carrying one angled
        /// smoked-glass panel that rises from the cowl to just under the halo's front
        /// band, its top edge held below the cockpit sight line (y = 0.78 at the eye)
        /// so a straight-ahead view passes over the glass; plus body-coloured cowl
        /// and cockpit-side shoulders and a padded headrest behind the helmet, so
        /// the opening around the driver reads as sealed bodywork rather than a
        /// bathtub. The windshield keeps its own group because — like the halo —
        /// <c>AiField</c> must not paint it team colour.
        /// </summary>
        private static void BuildCockpit(Transform body, Material paint)
        {
            Material glass = CreatePaint("Car_GlassMaterial", GlassColor);
            Transform windshield = AddVisualGroup(body, GlassRootName);

            var glassPanel = AddVisualPart(windshield, "WindshieldGlass", PrimitiveType.Cube,
                new Vector3(0f, 0.60f, 0.58f), new Vector3(0.56f, 0.34f, 0.05f), glass);
            // Lean the panel back toward the driver: top edge y ~ 0.74, below the eye.
            glassPanel.localRotation = Quaternion.Euler(-35f, 0f, 0f);

            AddVisualPart(body, "CockpitCowl", PrimitiveType.Cube,
                new Vector3(0f, 0.50f, 0.52f), new Vector3(0.50f, 0.18f, 0.28f), paint);
            AddVisualPart(body, "CockpitSide_L", PrimitiveType.Cube,
                new Vector3(-0.30f, 0.55f, 0.16f), new Vector3(0.13f, 0.20f, 0.62f), paint);
            AddVisualPart(body, "CockpitSide_R", PrimitiveType.Cube,
                new Vector3(0.30f, 0.55f, 0.16f), new Vector3(0.13f, 0.20f, 0.62f), paint);
            AddVisualPart(body, "Headrest", PrimitiveType.Cube,
                new Vector3(0f, 0.53f, -0.04f), new Vector3(0.46f, 0.17f, 0.30f), paint);
        }

        /// <summary>
        /// An empty named <paramref name="name"/> under <paramref name="parent"/> to
        /// hang several visual pieces on as one unit. Nothing to strip the collider
        /// from — a bare GameObject never had one.
        /// </summary>
        private static Transform AddVisualGroup(Transform parent, string name)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, worldPositionStays: false);
            return group.transform;
        }

        /// <summary>
        /// One visual piece: a primitive under <paramref name="parent"/>, painted when
        /// there is paint to paint with, and stripped of the collider Unity insists on
        /// giving every primitive — a car whose wings collided with the wall would be
        /// a car steered by its front wing. Returns the part's transform so a caller
        /// that needs one (the leaned-back windshield) can pose it after the fact.
        /// </summary>
        private static Transform AddVisualPart(Transform parent, string name, PrimitiveType type,
            Vector3 localPos, Vector3 localScale, Material paint)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, worldPositionStays: false);
            part.transform.localPosition = localPos;
            part.transform.localScale = localScale;

            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                // Immediate, not deferred: a piece that collides for the rest of the
                // frame it was born in is a piece a same-frame test — or a car already
                // touching a wall — can still hit.
                Object.DestroyImmediate(collider);
            }

            if (paint != null)
            {
                Renderer renderer = part.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = paint;
                }
            }

            return part.transform;
        }

        /// <summary>
        /// The shared red for an unpainted car's body, or null when the project has
        /// neither shader to make one from — the parts then keep Unity's default
        /// material, which still beats an invisible car. The same pair of shaders
        /// <c>TrackBuilder</c> paints the circuit with, so car and track agree on
        /// where the colour lives.
        /// </summary>
        private static Material CreateBodyPaint()
        {
            return CreatePaint("Car_BodyMaterial", DefaultBodyColor);
        }

        /// <summary>The halo's shared dark grey, made the same way as the body paint.</summary>
        private static Material CreateHaloPaint()
        {
            return CreatePaint("Car_HaloMaterial", HaloColor);
        }

        private static Material CreatePaint(string materialName, Color color)
        {
            Shader shader = null;
            for (int i = 0; i < BodyShaderNames.Length && shader == null; i++)
            {
                shader = Shader.Find(BodyShaderNames[i]);
            }

            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave
            };

            PaintColor(material, color);
            return material;
        }

        /// <summary>
        /// Write <paramref name="color"/> to the albedo of either pipeline: URP Lit
        /// keeps it in <c>_BaseColor</c>, the built-in Standard shader in <c>_Color</c>.
        /// Public so <c>AiField</c> can paint one car's body in its own colour through
        /// the same door the parts were built behind.
        /// </summary>
        public static void PaintColor(Material material, Color color)
        {
            static int Id(string propertyName) => Shader.PropertyToID(propertyName);

            int baseColorId = Id("_BaseColor");
            int colorId = Id("_Color");

            if (material.HasProperty(baseColorId))
            {
                material.SetColor(baseColorId, color);
            }
            else if (material.HasProperty(colorId))
            {
                material.SetColor(colorId, color);
            }
        }

        // -------------------------------------------------------------- update

        private void Update()
        {
            // The reset key belongs to whoever is sitting in front of the screen, and there is only
            // one keyboard to go round: a car driven by something else must not join every other car
            // on the grid in resetting the moment it is pressed.
            if (HumanControlled && _input != null && _input.ResetPressed)
            {
                ResetToSpawn();
            }
        }

        private void FixedUpdate()
        {
            if (tuning == null) return;

            // A human car whose input map never loaded has no controls to apply, which is how it has
            // always behaved: it sits the physics step out rather than driving on a phantom input.
            // An AI car is not missing anything — it has no map by design and its controls come from
            // the driver component, so it carries on past this point with _input null.
            if (HumanControlled && _input == null) return;

            // Controls armed with SetTestControls replace the devices for the rest of this
            // component's life, so a PlayMode test drives the same code path the headless
            // referee steps through in StepTestFrame (which applies the same rule). A car that is
            // not human-driven never reads the devices at all: its driver is an AI that hands the
            // same four controls in through SetTestControls, and _input stays null for it.
            ApplyControlFrame(Time.fixedDeltaTime, readInput: CanReadDevices());
        }

        /// <summary>
        /// Whether a control frame of this car may take its inputs off the devices: a human has to be
        /// assigned to it, the input map has to exist to be read, and no test or AI driver may have
        /// armed the override that <see cref="SetTestControls"/> is.
        /// </summary>
        private bool CanReadDevices()
        {
            return HumanControlled && !_testControls && _input != null;
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

            // Race gate (countdown, or this car has finished): no drive and no steer, the rear
            // wheels held by the handbrake. Brake stays 0 so the standstill reverse latch cannot engage;
            // gravity and the suspension still settle the car.
            if (DriveLocked)
            {
                throttle = 0f;
                brake = 0f;
                steerInput = 0f;
                handbrake = true;
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

            // Rate-limited rack so direction changes never snap the body. Winding onto more lock
            // slews at SteerRate; coming off the lock — back toward centre or across to the other
            // side of it — unwinds at the quicker SteerReturnRate.
            float rate = IsWindingOffLock(targetAngle) ? tuning.SteerReturnRate : tuning.SteerRate;
            _currentSteerAngle = Mathf.MoveTowardsAngle(
                _currentSteerAngle, targetAngle, rate * dt);

            for (int i = 0; i < 4; i++)
            {
                _wheels[i].steerAngle = IsFront[i] ? _currentSteerAngle : 0f;
            }
        }

        /// <summary>
        /// Whether the rack is heading off the lock it is holding: less lock is being asked for
        /// than is in, or the ask is the other way round. Both angles have to be off centre for the
        /// sign test to count, so a rack being wound on from straight ahead keeps SteerRate.
        /// </summary>
        private bool IsWindingOffLock(float targetAngle)
        {
            float target = Mathf.Abs(targetAngle);
            float current = Mathf.Abs(_currentSteerAngle);

            if (target < current - SteerAngleEpsilon) return true;

            return target > SteerAngleEpsilon
                   && current > SteerAngleEpsilon
                   && Mathf.Sign(targetAngle) != Mathf.Sign(_currentSteerAngle);
        }

        /// <summary>
        /// Drive, brake and reverse the four wheels for one control frame.
        ///
        /// One key does three jobs, and which one it is doing comes from the signed forward speed
        /// rather than from the input. Held with the car above
        /// <see cref="VehicleTuning.ReverseEngageSpeedMs"/> the brake is a brake and nothing else, so a
        /// car coming down the straight is stopped before anything else can be asked of it. Held with the
        /// car below it — stood still, or parked on a wall with the nose pushing at it and nowhere left to
        /// go, which makes no speed for a brake to act on — it engages the reverse: a fraction of the drive
        /// force (<see cref="VehicleTuning.ReverseForceScale"/>), wound off to nothing at
        /// <see cref="VehicleTuning.ReverseTopSpeedMs"/>, and the difference between a mistake a driver can
        /// drive out of and one they can only reset out of. Throttle has the mirror-image problem and the
        /// mirror-image answer: taken while the car is already rolling backwards it gets the discs, so W
        /// stops the roll and only then pulls the car forwards out of it.
        /// </summary>
        private void ApplyDriveAndBrake(float throttle, float brake, bool handbrake)
        {
            // The rear axle grips harder than the nose by default, so the handbrake has to take that
            // grip away as well as locking the discs: held, the rear wheels drop to a fraction of
            // their planted sideways stiffness and the car rotates; released, it comes straight back.
            float rearGrip = tuning.RearLateralGripMultiplier * 2f; // 2 = Unity default stiffness
            ApplyRearSidewaysGrip(handbrake ? rearGrip * tuning.HandbrakeRearGripScale : rearGrip);

            // Signed, because brake and reverse are one key and only the direction travelled says which
            // is being asked for. A car with its nose against a barrier makes no speed at all for a brake
            // to act on, and the force holding it there is the force that has to come back out of it.
            float forwardSpeed = Vector3.Dot(_rigidbody.linearVelocity, transform.forward);
            _currentForwardSpeed = forwardSpeed;
            UpdateReverseLatch(throttle, brake, forwardSpeed);

            // Engine curve: full force off the line, fading toward TopSpeedMs.
            float speedT = Mathf.Clamp01(_currentSpeed / tuning.TopSpeedMs);
            float powerCurve = 1f - speedT * speedT;

            // DriveForcePerWheel is a contact-patch force (N); torque via radius.
            float driveTorque = throttle > 0.01f
                ? throttle * tuning.DriveForcePerWheel * powerCurve * tuning.WheelRadius
                : 0f;

            // The same contact-patch rule run backwards. The power curve is built on the unsigned speed
            // and tops out at TopSpeedMs, so it says nothing about a reverse and is not used for one: the
            // fade here is against the reverse speed itself, which gives the reverse its own ceiling.
            float reverseTorque = _reversing
                ? -brake * tuning.DriveForcePerWheel * tuning.ReverseForceScale * tuning.WheelRadius
                  * (1f - Mathf.Clamp01(-forwardSpeed / Mathf.Max(0.001f, tuning.ReverseTopSpeedMs)))
                : 0f;

            // W taken by a car already on its way backwards: discs first, drive once the roll is gone.
            bool rollingBackwards = throttle > 0.01f && forwardSpeed < BackwardRollCutoverMs;

            bool idle = throttle < 0.01f && brake < 0.01f && !handbrake && !_reversing;
            bool parked = Mathf.Abs(forwardSpeed) < tuning.ReverseEngageSpeedMs;
            if (idle && parked)
            {
                // Leave the last resting motor/brake on the colliders. Rewriting
                // them every FixedUpdate is what kept a 1e-4 Nm rest torque from
                // matching Ctl8 (controller disabled after setting the same value).
                if (!_restingWheelTorqueArmed)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        _wheels[i].motorTorque = RestingMotorTorque;
                        _wheels[i].brakeTorque = 0f;
                    }
                    _restingWheelTorqueArmed = true;
                }
                return;
            }

            _restingWheelTorqueArmed = false;

            for (int i = 0; i < 4; i++)
            {
                WheelCollider w = _wheels[i];
                bool driven = tuning.AllWheelDrive || IsFront[i] == false;

                if (_reversing)
                {
                    w.motorTorque = driven ? reverseTorque : 0f;
                }
                else
                {
                    w.motorTorque = driven && !rollingBackwards ? driveTorque : 0f;
                }

                if (Mathf.Abs(w.motorTorque) < RestingMotorTorque)
                {
                    w.motorTorque = RestingMotorTorque;
                }

                float brakeTorque = 0f;
                if (_reversing)
                {
                    // Nothing is clamped on the reverse: it is driven, not scrubbed, and a locked wheel
                    // on a car being walked backwards out of a barrier only steers it sideways. The
                    // handbrake lock below stays on top of this because it is a key held for its own
                    // reason, and an undriven axle is left free to follow it rather than drag on it.
                    brakeTorque = 0f;
                }
                else if (rollingBackwards)
                {
                    brakeTorque = throttle * tuning.BrakeTorquePerWheel;
                }
                else if (brake > 0.01f)
                {
                    brakeTorque = brake * tuning.BrakeTorquePerWheel;
                }
                else if (throttle < 0.01f)
                {
                    // Full lift-off drag only while already rolling so a parked
                    // car is not extra-pinned on top of the wheel-solver issue.
                    float fade = Mathf.Clamp01(Mathf.Abs(forwardSpeed) /
                        Mathf.Max(0.001f, tuning.ReverseEngageSpeedMs));
                    brakeTorque = tuning.EngineBrakeTorque * fade;
                }
                if (handbrake && !IsFront[i])
                {
                    brakeTorque = Mathf.Max(brakeTorque, tuning.BrakeTorquePerWheel); // rear lock-up
                }
                w.brakeTorque = brakeTorque;
            }
        }

        /// <summary>
        /// Which of its jobs the brake key is doing this frame. It engages on a brake held against a
        /// closed throttle with the car under <see cref="VehicleTuning.ReverseEngageSpeedMs"/>, below
        /// which there is nothing left for it to brake; it lets go the moment either of the keys it
        /// shares says otherwise, which is throttle being asked for or the brake being released. A brake
        /// held at or above the engage speed leaves the latch as it was, and that sliver of hysteresis is
        /// what keeps a car backing out of a wall backing out of it instead of changing its mind twice
        /// the speed goes one way and then the other.
        /// </summary>
        private void UpdateReverseLatch(float throttle, float brake, float forwardSpeed)
        {
            if (brake > 0.01f && throttle < 0.01f && forwardSpeed < tuning.ReverseEngageSpeedMs)
            {
                _reversing = true;
            }
            else if (throttle > 0.01f || brake <= 0.01f)
            {
                _reversing = false;
            }
        }

        /// <summary>
        /// Write a sideways stiffness onto the rear WheelColliders, and only when it differs from
        /// what is already on them — friction curves are copied structs, so handing Unity a new one
        /// every fixed step of a held handbrake would be work for nothing.
        /// </summary>
        private void ApplyRearSidewaysGrip(float stiffness)
        {
            // NaN until the first control frame, and Approximately is false against NaN.
            if (Mathf.Approximately(_rearSidewaysStiffness, stiffness)) return;
            _rearSidewaysStiffness = stiffness;

            for (int i = 0; i < 4; i++)
            {
                if (IsFront[i] || _wheels[i] == null) continue;

                var side = _wheels[i].sidewaysFriction;
                side.stiffness = stiffness;
                _wheels[i].sidewaysFriction = side;
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
        /// <summary>Set by RaceSession from RaceFlow: true during the countdown and once this car has finished.</summary>
        public bool DriveLocked;

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
        /// Raised at the end of every <see cref="ResetToSpawn"/>, whoever asked for it — the R key, the
        /// session's automatic recovery, or anything else that puts the car back on the pose it was armed
        /// with. A listener cannot tell a body that has been carried from a body that has been driven any
        /// other way, and <see cref="OpenWheelRacer.Race.RaceSession"/> keeps an odometer of how far the
        /// car has come, in which metres it was picked up and put down are not metres it travelled.
        /// </summary>
        public event System.Action SpawnReset;

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
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            _currentSteerAngle = 0f;
            // The reset is the one way out of a jam that does not go through the drivetrain, so the
            // reverse latch has to come off with it: a car put back on the grid is not backing at it.
            _reversing = false;
            _currentForwardSpeed = 0f;
            Debug.Log("[VehicleController] Reset to spawn pose.");

            // Last, so anything listening reads the body where it has actually been put rather than where
            // it happened to be when the reset was asked for.
            SpawnReset?.Invoke();
        }
    }
}
