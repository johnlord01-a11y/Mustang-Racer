using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenWheelRacer
{
    /// Seated driver's view (default) of the Mustang GT, LEFT seat (US car).
    /// The rig is a ROUND 3-spoke steering wheel — dark rim, silver spokes,
    /// chrome-ringed pony badge, navy gloved hands at 9 and 3 — that turns with
    /// the car's actual steer angle, a short column into the dash, and a small
    /// readout plate on the lower spoke showing real speed (km/h) and a derived
    /// virtual gear, drawn at the plate's projected screen position. The hood,
    /// windshield frame, twin gauge pods, three centre rounds, touchscreen and
    /// manual shifter all come from <see cref="MustangMeshBuilder"/>'s body, so
    /// one cabin is shared with the chase cameras. C toggles back to the
    /// existing ChaseCamera (near clip and field of view restore). All cockpit
    /// geometry is collider-free and parented to the player's car only.
    [RequireComponent(typeof(Camera))]
    public sealed class CockpitView : MonoBehaviour
    {
        /// <summary>Driver eye in the LEFT seat, dead ahead of the twin gauge pods
        /// and centred over the wheel; z clears the seat back. At seated eye
        /// height the A-pillars cross the sides of the view and the glass base
        /// edge crosses the bottom; the header rail sits ~40 degrees above the
        /// eye at 30 cm (convertible proportions), outside any sane FOV, so it
        /// frames chase shots rather than this one.</summary>
        public static readonly Vector3 EyeLocal = new Vector3(-0.22f, 0.99f, -0.24f);

        /// Slight downward pitch so hood, dash and shifter compose naturally
        /// in the lower half while the road fills the top (matches ref-4).
        public const float EyePitchDown = 8f;

        /// Wider seated field of view than the chase camera restores; at 16:9
        /// the vertical half-angle is ~27 degrees, which frames the gauge pods,
        /// both A-pillar roots and most of the centre stack.
        public const float CockpitFieldOfView = 80f;

        public const float WheelTurnRatio = 6f;   // wheel degrees per road-wheel degree
        public const float NearClip = 0.05f;
        public const float GearBandKmh = 40f;     // virtual gear: one gear per 40 km/h band, 1..8

        public bool CockpitActive { get; private set; } = true;
        public Transform Rig => _rig;
        public Transform SteeringWheel => _wheel;
        /// <summary>Black display plate on the lower spoke; the speed/gear readout is drawn over it.</summary>
        public Transform Dash => _dash;

        private VehicleController _car;
        private ChaseCamera _chase;
        private Camera _camera;
        private Transform _rig, _wheel, _dash;
        private Renderer[] _ownGlass;
        private float _defaultNear, _defaultFov;
        private GUIStyle _dashStyle;

        public void Bind(VehicleController car, ChaseCamera chase)
        {
            _car = car;
            _chase = chase;
            _camera = GetComponent<Camera>();
            _defaultNear = _camera.nearClipPlane;
            _defaultFov = _camera.fieldOfView;
            if (_rig == null && car != null) BuildRig(car.transform);
            SetCockpit(true);
        }

        public void SetCockpit(bool on)
        {
            CockpitActive = on;
            if (_chase != null) _chase.enabled = !on;
            if (_rig != null) _rig.gameObject.SetActive(on);
            if (_camera != null)
            {
                _camera.nearClipPlane = on ? NearClip : _defaultNear;
                _camera.fieldOfView = on ? CockpitFieldOfView : _defaultFov;
            }
            SetOwnGlassVisible(!on);
        }

        /// <summary>
        /// Hides the player's own windscreen while seated in the car. The eye sits
        /// behind a raked pane whose inside face receives no sun, no bounce and
        /// no probe, so it would wash the frame; every race game hides the own
        /// windscreen in cockpit view and shows it on the chase cam. AI cars are
        /// untouched — this only ever walks the bound player's car.
        /// </summary>
        private void SetOwnGlassVisible(bool visible)
        {
            if (_car == null) return;
            if (_ownGlass == null)
                _ownGlass = MustangMeshBuilder.FindOwnGlass(_car.transform);
            if (_ownGlass == null) return;
            foreach (Renderer glass in _ownGlass)
                if (glass != null) glass.enabled = visible;
        }

        public void Toggle() => SetCockpit(!CockpitActive);

        public static float WheelAngleFor(float roadWheelSteerDeg) => roadWheelSteerDeg * WheelTurnRatio;

        public static int VirtualGear(float forwardSpeedMs, bool reversing)
        {
            if (reversing) return -1;
            float kmh = Mathf.Abs(forwardSpeedMs) * 3.6f;
            if (kmh < 1f) return 0;
            return Mathf.Clamp(1 + Mathf.FloorToInt(kmh / GearBandKmh), 1, 8);
        }

        public static string GearLabel(int gear) => gear < 0 ? "R" : gear == 0 ? "N" : gear.ToString();

        /// <summary>The on-wheel readout: gear, then speed in km/h.</summary>
        public static string DashText(int kmh, string gear) => $"{gear}  {kmh}";

        /// <summary>The dash plate's centre in GUI coordinates (origin top-left); false when not visible.</summary>
        public bool TryGetDashGuiPoint(out Vector2 point)
        {
            point = default;
            if (!CockpitActive || _dash == null || _camera == null) return false;
            Vector3 sp = _camera.WorldToScreenPoint(_dash.position);
            if (sp.z <= 0f) return false;
            point = new Vector2(sp.x, Screen.height - sp.y);
            return true;
        }

        private void Update()
        {
            Keyboard k = Keyboard.current;
            if (k != null && k.cKey.wasPressedThisFrame) Toggle();
        }

        private void LateUpdate()
        {
            if (_car == null) return;
            if (_wheel != null)
                _wheel.localRotation = Quaternion.Euler(-20f, 0f, -WheelAngleFor(_car.CurrentSteerAngle));
            if (CockpitActive)
            {
                Transform t = _car.transform;
                transform.SetPositionAndRotation(
                    t.TransformPoint(EyeLocal),
                    t.rotation * Quaternion.Euler(EyePitchDown, 0f, 0f));
            }
        }

        private void OnGUI()
        {
            if (_car == null || !TryGetDashGuiPoint(out Vector2 p)) return;
            int size = Mathf.Max(14, Mathf.RoundToInt(Screen.height / 26f));
            if (_dashStyle == null || _dashStyle.fontSize != size)
            {
                _dashStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = size, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold
                };
                _dashStyle.normal.textColor = Color.white;
            }

            int kmh = Mathf.RoundToInt(_car.CurrentSpeedMs * 3.6f);
            string gear = GearLabel(VirtualGear(_car.ForwardSpeed, _car.IsReversing));
            GUI.Label(new Rect(p.x - size * 3f, p.y - size * 0.7f, size * 6f, size * 1.4f), DashText(kmh, gear), _dashStyle);
        }

        private void BuildRig(Transform car)
        {
            var black = MakeMaterial(new Color(0.045f, 0.045f, 0.05f));
            var carbon = MakeMaterial(new Color(0.08f, 0.08f, 0.09f));
            var silver = MakeMaterial(new Color(0.72f, 0.73f, 0.75f));
            var navy = MakeMaterial(new Color(0.10f, 0.10f, 0.13f));

            _rig = new GameObject("Cockpit_Rig").transform;
            _rig.SetParent(car, false);

            // ROUND steering wheel in front of the left seat, rim leaning back
            // 20 degrees: 37 cm across, centred at eye height over the knees.
            _wheel = new GameObject("Cockpit_Wheel").transform;
            _wheel.SetParent(_rig, false);
            _wheel.localPosition = new Vector3(-0.35f, 0.86f, 0.26f);
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * 360f, rad = a * Mathf.Deg2Rad;
                Piece($"Wheel_Rim{i}", PrimitiveType.Cube, _wheel,
                    new Vector3(Mathf.Cos(rad) * 0.185f, Mathf.Sin(rad) * 0.185f, 0f),
                    new Vector3(0f, 0f, a), new Vector3(0.090f, 0.052f, 0.052f), carbon);
            }
            // Three silver spokes; small dark button pads outboard on the pair.
            Piece("Wheel_SpokeL", PrimitiveType.Cube, _wheel, new Vector3(-0.105f, 0.008f, 0f), Vector3.zero, new Vector3(0.16f, 0.030f, 0.024f), silver);
            Piece("Wheel_SpokeR", PrimitiveType.Cube, _wheel, new Vector3(0.105f, 0.008f, 0f), Vector3.zero, new Vector3(0.16f, 0.030f, 0.024f), silver);
            Piece("Wheel_SpokeB", PrimitiveType.Cube, _wheel, new Vector3(0f, -0.105f, 0f), Vector3.zero, new Vector3(0.030f, 0.16f, 0.024f), silver);
            Piece("Wheel_PadL", PrimitiveType.Cube, _wheel, new Vector3(-0.115f, 0.032f, 0.016f), Vector3.zero, new Vector3(0.075f, 0.040f, 0.012f), black);
            Piece("Wheel_PadR", PrimitiveType.Cube, _wheel, new Vector3(0.115f, 0.032f, 0.016f), Vector3.zero, new Vector3(0.075f, 0.040f, 0.012f), black);
            // Hub: dark pad inside a chrome ring; the badge is a chrome ring +
            // dark plate + a galloping-pony silhouette of six small silver
            // boxes (body, neck, head, two legs, tail) - no image assets.
            Piece("Wheel_HubRing", PrimitiveType.Cylinder, _wheel, new Vector3(0f, 0.003f, 0.010f), new Vector3(90f, 0f, 0f), new Vector3(0.164f, 0.030f, 0.164f), silver);
            Piece("Wheel_Hub", PrimitiveType.Cylinder, _wheel, new Vector3(0f, 0.003f, 0.017f), new Vector3(90f, 0f, 0f), new Vector3(0.150f, 0.026f, 0.150f), carbon);
            Piece("Wheel_BadgeRing", PrimitiveType.Cylinder, _wheel, new Vector3(0f, 0.003f, 0.031f), new Vector3(90f, 0f, 0f), new Vector3(0.066f, 0.008f, 0.066f), silver);
            Piece("Wheel_BadgePlate", PrimitiveType.Cylinder, _wheel, new Vector3(0f, 0.003f, 0.035f), new Vector3(90f, 0f, 0f), new Vector3(0.054f, 0.006f, 0.054f), carbon);
            Piece("Pony_Body", PrimitiveType.Cube, _wheel, new Vector3(0.003f, 0.005f, 0.038f), new Vector3(0f, 0f, -12f), new Vector3(0.030f, 0.011f, 0.007f), silver);
            Piece("Pony_Neck", PrimitiveType.Cube, _wheel, new Vector3(0.016f, 0.013f, 0.038f), new Vector3(0f, 0f, 28f), new Vector3(0.009f, 0.015f, 0.007f), silver);
            Piece("Pony_Head", PrimitiveType.Cube, _wheel, new Vector3(0.022f, 0.021f, 0.038f), new Vector3(0f, 0f, 12f), new Vector3(0.009f, 0.006f, 0.007f), silver);
            Piece("Pony_LegF", PrimitiveType.Cube, _wheel, new Vector3(0.014f, -0.007f, 0.038f), new Vector3(0f, 0f, 28f), new Vector3(0.017f, 0.006f, 0.007f), silver);
            Piece("Pony_LegR", PrimitiveType.Cube, _wheel, new Vector3(-0.014f, -0.006f, 0.038f), new Vector3(0f, 0f, 205f), new Vector3(0.017f, 0.006f, 0.007f), silver);
            Piece("Pony_Tail", PrimitiveType.Cube, _wheel, new Vector3(-0.020f, 0.010f, 0.038f), new Vector3(0f, 0f, 160f), new Vector3(0.015f, 0.006f, 0.007f), silver);
            // Readout plate low on the lower spoke, offset inboard so it sits
            // dead ahead of the eye (the wheel itself centres on the seat).
            _dash = Piece("Wheel_Dash", PrimitiveType.Cube, _wheel,
                new Vector3(0.13f, -0.14f, 0.004f), Vector3.zero, new Vector3(0.072f, 0.034f, 0.010f), black).transform;
            // Navy gloves gripping the rim at 9 and 3.
            Piece("Hand_L", PrimitiveType.Capsule, _wheel, new Vector3(-0.178f, 0.045f, -0.012f), new Vector3(0f, 0f, 72f), new Vector3(0.062f, 0.088f, 0.062f), navy);
            Piece("Hand_R", PrimitiveType.Capsule, _wheel, new Vector3(0.178f, 0.045f, -0.012f), new Vector3(0f, 0f, -72f), new Vector3(0.062f, 0.088f, 0.062f), navy);
            // Short column sloping from the hub into the dash face.
            Piece("Wheel_Column", PrimitiveType.Cylinder, _rig, new Vector3(-0.35f, 0.76f, 0.325f), new Vector3(147f, 0f, 0f), new Vector3(0.044f, 0.26f, 0.044f), black);
        }

        private static GameObject Piece(string name, PrimitiveType type, Transform parent,
            Vector3 pos, Vector3 euler, Vector3 scale, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider c = go.GetComponent<Collider>();
            if (c != null) { c.enabled = false; Object.Destroy(c); }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null && m != null) { r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            return go;
        }

        private static Material MakeMaterial(Color c)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (s == null) return null;
            var m = new Material(s) { hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            else m.color = c;
            return m;
        }
    }
}
