using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenWheelRacer
{
    /// Seated driver's view (default) with a primitive cockpit: open-top black wheel that turns with the
    /// car's actual steer angle, two dark gloved hands on its grips, white cockpit sides, and a small
    /// dash display mounted on the wheel itself (top centre of the wheel face) showing real speed (km/h)
    /// and a derived virtual gear; it is drawn at the wheel's projected screen position, so it moves with the wheel. C toggles back to the
    /// existing ChaseCamera. All cockpit geometry is collider-free and parented to the player's car only.
    [RequireComponent(typeof(Camera))]
    public sealed class CockpitView : MonoBehaviour
    {
        public static readonly Vector3 EyeLocal = new Vector3(0f, 0.78f, -0.35f);
        public const float WheelTurnRatio = 6f;   // wheel degrees per road-wheel degree
        public const float NearClip = 0.05f;
        public const float GearBandKmh = 40f;     // virtual gear: one gear per 40 km/h band, 1..8

        public bool CockpitActive { get; private set; } = true;
        public Transform Rig => _rig;
        public Transform SteeringWheel => _wheel;
        /// <summary>Black display panel on the wheel face; the speed/gear readout is drawn over it.</summary>
        public Transform Dash => _dash;

        private VehicleController _car;
        private ChaseCamera _chase;
        private Camera _camera;
        private Transform _rig, _wheel, _dash;
        private float _defaultNear;
        private GUIStyle _dashStyle;

        public void Bind(VehicleController car, ChaseCamera chase)
        {
            _car = car;
            _chase = chase;
            _camera = GetComponent<Camera>();
            _defaultNear = _camera.nearClipPlane;
            if (_rig == null && car != null) BuildRig(car.transform);
            SetCockpit(true);
        }

        public void SetCockpit(bool on)
        {
            CockpitActive = on;
            if (_chase != null) _chase.enabled = !on;
            if (_rig != null) _rig.gameObject.SetActive(on);
            if (_camera != null) _camera.nearClipPlane = on ? NearClip : _defaultNear;
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

        /// <summary>The dash panel's centre in GUI coordinates (origin top-left); false when not visible.</summary>
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
                transform.SetPositionAndRotation(t.TransformPoint(EyeLocal), t.rotation);
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
            var black = MakeMaterial(new Color(0.05f, 0.05f, 0.06f));
            var glove = MakeMaterial(new Color(0.12f, 0.12f, 0.14f));
            var white = MakeMaterial(new Color(0.92f, 0.92f, 0.94f));

            _rig = new GameObject("Cockpit_Rig").transform;
            _rig.SetParent(car, false);

            // Steering wheel pivot sits low and centred (lower third of the view) so the road stays visible.
            var carbon = MakeMaterial(new Color(0.09f, 0.09f, 0.10f));
            var red = MakeMaterial(new Color(0.9f, 0.15f, 0.1f));
            var blue = MakeMaterial(new Color(0.15f, 0.4f, 0.95f));
            var yellow = MakeMaterial(new Color(0.95f, 0.8f, 0.1f));
            var green = MakeMaterial(new Color(0.2f, 0.8f, 0.3f));
            _wheel = new GameObject("Cockpit_Wheel").transform;
            _wheel.SetParent(_rig, false);
            _wheel.localPosition = new Vector3(0f, 0.59f, 0.08f);
            // Rectangular carbon face with a flat bottom and upright grips either side.
            Piece("Wheel_Face", PrimitiveType.Cube, _wheel, new Vector3(0f, 0f, 0f), new Vector3(0.30f, 0.13f, 0.03f), carbon);
            Piece("Wheel_GripL", PrimitiveType.Cube, _wheel, new Vector3(-0.175f, 0.01f, 0f), new Vector3(0.05f, 0.19f, 0.045f), black);
            Piece("Wheel_GripR", PrimitiveType.Cube, _wheel, new Vector3(0.175f, 0.01f, 0f), new Vector3(0.05f, 0.19f, 0.045f), black);
            // Display at the top centre of the face; the readout is drawn over it.
            _dash = Piece("Wheel_Dash", PrimitiveType.Cube, _wheel, new Vector3(0f, 0.03f, -0.018f), new Vector3(0.12f, 0.05f, 0.008f), black).transform;
            // Round coloured buttons and two rotary dials below the display.
            Piece("Wheel_ButtonR", PrimitiveType.Cylinder, _wheel, new Vector3(0.10f, 0.03f, -0.018f), new Vector3(0.022f, 0.004f, 0.022f), red, true);
            Piece("Wheel_ButtonB", PrimitiveType.Cylinder, _wheel, new Vector3(-0.10f, 0.03f, -0.018f), new Vector3(0.022f, 0.004f, 0.022f), blue, true);
            Piece("Wheel_ButtonY", PrimitiveType.Cylinder, _wheel, new Vector3(0.10f, -0.025f, -0.018f), new Vector3(0.022f, 0.004f, 0.022f), yellow, true);
            Piece("Wheel_ButtonG", PrimitiveType.Cylinder, _wheel, new Vector3(-0.10f, -0.025f, -0.018f), new Vector3(0.022f, 0.004f, 0.022f), green, true);
            Piece("Wheel_DialL", PrimitiveType.Cylinder, _wheel, new Vector3(-0.035f, -0.035f, -0.02f), new Vector3(0.028f, 0.006f, 0.028f), black, true);
            Piece("Wheel_DialR", PrimitiveType.Cylinder, _wheel, new Vector3(0.035f, -0.035f, -0.02f), new Vector3(0.028f, 0.006f, 0.028f), black, true);
            // Dark navy gloved hands wrapped around the grips, each with a small thumb towards the face.
            var navy = MakeMaterial(new Color(0.08f, 0.10f, 0.22f));
            Piece("Hand_L", PrimitiveType.Capsule, _wheel, new Vector3(-0.18f, 0.01f, -0.02f), new Vector3(0.065f, 0.07f, 0.065f), navy);
            Piece("Hand_R", PrimitiveType.Capsule, _wheel, new Vector3(0.18f, 0.01f, -0.02f), new Vector3(0.065f, 0.07f, 0.065f), navy);
            Piece("Thumb_L", PrimitiveType.Capsule, _wheel, new Vector3(-0.145f, 0.045f, -0.03f), new Vector3(0.025f, 0.03f, 0.025f), navy, true);
            Piece("Thumb_R", PrimitiveType.Capsule, _wheel, new Vector3(0.145f, 0.045f, -0.03f), new Vector3(0.025f, 0.03f, 0.025f), navy, true);

            // White cockpit sides framing the view left and right.
            Piece("Cockpit_SideL", PrimitiveType.Cube, _rig, new Vector3(-0.42f, 0.62f, 0.2f), new Vector3(0.12f, 0.18f, 1.4f), white);
            Piece("Cockpit_SideR", PrimitiveType.Cube, _rig, new Vector3(0.42f, 0.62f, 0.2f), new Vector3(0.12f, 0.18f, 1.4f), white);
        }

        private static GameObject Piece(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m, bool faceDriver = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider c = go.GetComponent<Collider>();
            if (c != null) { c.enabled = false; Object.Destroy(c); }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (faceDriver) go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder/capsule axis towards the driver
            Renderer r = go.GetComponent<Renderer>();
            if (r != null && m != null) { r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            return go;
        }

        private static Material MakeMaterial(Color c)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (s == null) return null;
            var m = new Material(s) { color = c };
            return m;
        }
    }
}