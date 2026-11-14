using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenWheelRacer
{
    /// Seated driver's view (default) of the Mustang GT, LEFT seat (US car).
    /// The rig is a ROUND 3-spoke steering wheel — one joined torus rim,
    /// silver spokes, dark hub with a compact pony badge, navy gloved hands at
    /// 9 and 3 — that turns with the car's actual steer angle, a short column
    /// into the dash, and a speed (km/h) + derived virtual-gear readout
    /// anchored to the centre touchscreen, drawn at the panel's projected
    /// screen position (it never rides the wheel, so it does not spin). The
    /// hood, windshield frame, twin gauge pods, three centre rounds and manual
    /// shifter all come from <see cref="MustangMeshBuilder"/>'s body, so one
    /// cabin is shared with the chase cameras. C toggles back to the existing
    /// ChaseCamera (near clip and field of view restore; the chase snaps to
    /// its rest pose so the first outside frame is not stuck mid-lerp inside
    /// the body). All cockpit geometry is collider-free and parented to the
    /// player's car only.
    [RequireComponent(typeof(Camera))]
    public sealed class CockpitView : MonoBehaviour
    {
        /// <summary>Driver eye in the LEFT seat, dead ahead of the twin gauge
        /// pods and centred over the wheel; z clears the seat back. Seated at
        /// y 1.02, z -0.34, x -0.30 (round-2 lesson: sit the eye directly
        /// behind the offset GT wheel, over the driver seat at x -0.35): an eye at
        /// head height over the tub sees DOWN onto the 0.636 dash cap, the
        /// seat-back tops frame the shoulders, and the eye sits BACK of the
        /// wheel so the slimmed hub clears the gauge pods instead of eating
        /// them. Near-centre seating keeps the driver's own door card and
        /// the centre stack both in the frame like ref-4.</summary>
        public static readonly Vector3 EyeLocal = new Vector3(-0.10f, 1.02f, -0.34f);

        /// Slight downward pitch so hood, dash and shifter compose naturally
        /// in the lower half while the road fills the top (matches ref-4).
        public const float EyePitchDown = 10f;

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
        /// <summary>Anchor at the centre touchscreen's lit UI panel; the speed/gear readout
        /// is drawn at its projected position, so it never spins with the wheel.</summary>
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
            if (_chase != null)
            {
                _chase.enabled = !on;
                // Hand back at the rest pose, not mid-lerp through the body
                // (the round-1 frame_02 chase_near embed artifact).
                if (!on) _chase.SnapToTarget();
            }
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
                _dashStyle.normal.textColor = new Color(0.06f, 0.06f, 0.07f, 0.95f);  // dark digits on the lit screen
            }

            int kmh = Mathf.RoundToInt(_car.CurrentSpeedMs * 3.6f);
            string gear = GearLabel(VirtualGear(_car.ForwardSpeed, _car.IsReversing));
            GUI.Label(new Rect(p.x - size * 3f, p.y - size * 0.7f, size * 6f, size * 1.4f), DashText(kmh, gear), _dashStyle);
        }

        private void BuildRig(Transform car)
        {
            var black = MakeMaterial(new Color(0.045f, 0.045f, 0.05f));
            var carbon = MakeMaterial(new Color(0.08f, 0.08f, 0.09f));
            var gunmetal = MakeMaterial(new Color(0.30f, 0.31f, 0.34f));
            var rimDark = MakeMaterial(new Color(0.15f, 0.15f, 0.17f));
            var silver = MakeMaterial(new Color(0.85f, 0.86f, 0.88f));
            var navy = MakeMaterial(new Color(0.10f, 0.10f, 0.13f));

            _rig = new GameObject("Cockpit_Rig").transform;
            _rig.SetParent(car, false);

            // ROUND steering wheel in front of the left seat, rim leaning back
            // 20 degrees: 37 cm across, centred at the sternum line below the
            // new 0.88 eye so the rim tops out at ~0.92 and stays under the
            // eye line like the reference (round-1's 0.86 centre read high).
            _wheel = new GameObject("Cockpit_Wheel").transform;
            _wheel.SetParent(_rig, false);
            _wheel.localPosition = new Vector3(-0.10f, 0.735f, 0.105f);
            // Joined torus rim, one closed mesh (round 1's fourteen separate
            // cubes gapped into a scalloped ring in every cockpit frame).
            MeshPiece("Wheel_Rim", _wheel, Vector3.zero, Vector3.zero, BuildRimMesh(0.140f, 0.022f, 32, 12), rimDark);
            // Three silver spokes; small dark button pads outboard on the pair.
            Piece("Wheel_SpokeL", PrimitiveType.Cube, _wheel, new Vector3(-0.084f, -0.030f, 0f), Vector3.zero, new Vector3(0.125f, 0.030f, 0.024f), silver);
            Piece("Wheel_SpokeR", PrimitiveType.Cube, _wheel, new Vector3(0.084f, -0.030f, 0f), Vector3.zero, new Vector3(0.125f, 0.030f, 0.024f), silver);
            Piece("Wheel_SpokeB", PrimitiveType.Cube, _wheel, new Vector3(0f, -0.082f, 0f), Vector3.zero, new Vector3(0.030f, 0.128f, 0.024f), silver);
            Piece("Wheel_PadL", PrimitiveType.Cube, _wheel, new Vector3(-0.115f, 0.032f, 0.016f), Vector3.zero, new Vector3(0.075f, 0.040f, 0.012f), black);
            Piece("Wheel_PadR", PrimitiveType.Cube, _wheel, new Vector3(0.115f, 0.032f, 0.016f), Vector3.zero, new Vector3(0.075f, 0.040f, 0.012f), black);
            // Hub: DARK centre pad inside a slim silver bezel — the reference
            // wheel's hub is a dark centre, not a chrome dinner plate (round-1
            // hub + badge occupied most of the wheel opening). The badge is a
            // compact galloping-pony silhouette of six small silver boxes
            // (body, neck, head, two legs, tail) at ~2/3 round-1 scale, flat
            // on the pad — no image assets, no chrome ring around it.
            Piece("Wheel_HubRing", PrimitiveType.Cylinder, _wheel, new Vector3(0f, 0.003f, -0.006f), new Vector3(90f, 0f, 0f), new Vector3(0.118f, 0.010f, 0.118f), black);
            // Speed/gear readout anchor: the centre touchscreen's lit UI
            // panel (body TouchUI at 0.02, 0.6325, 0.15), NOT the wheel, so
            // the digits stay glued to the screen while the wheel spins.
            // Invisible transform, no renderer, no collider.
            var dashGo = new GameObject("Dash_Readout");
            dashGo.transform.SetParent(_rig, false);
            dashGo.transform.localPosition = new Vector3(0.02f, 0.6335f, 0.150f);
            _dash = dashGo.transform;
            // Navy gloves gripping the rim at 9 and 3.
            Piece("Hand_L", PrimitiveType.Capsule, _wheel, new Vector3(-0.152f, 0.038f, -0.012f), new Vector3(0f, 0f, 72f), new Vector3(0.062f, 0.088f, 0.062f), navy);
            Piece("Hand_R", PrimitiveType.Capsule, _wheel, new Vector3(0.152f, 0.038f, -0.012f), new Vector3(0f, 0f, -72f), new Vector3(0.062f, 0.088f, 0.062f), navy);
            // Short column from the hub down through the open tub into the
            // dash front panel (cap front face z 0.43, top 0.630; the wheel
            // plane z 0.105 clears the driver cut-out rear edge z 0.21).
            // Column as two crisp angled boxes: hub -> mid -> dash (boxes silhouette better than lit cylinders).
            Piece("Wheel_ColumnA", PrimitiveType.Cube, _rig, new Vector3(-0.10f, 0.695f, 0.170f), new Vector3(-58.4f, 0f, 0f), new Vector3(0.055f, 0.160f, 0.055f), silver);
            Piece("Wheel_ColumnB", PrimitiveType.Cube, _rig, new Vector3(-0.10f, 0.615f, 0.325f), new Vector3(-66.0f, 0f, 0f), new Vector3(0.055f, 0.205f, 0.055f), silver);
        }

        private static GameObject MeshPiece(string name, Transform parent, Vector3 pos, Vector3 euler, Mesh mesh, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            var r = go.AddComponent<MeshRenderer>();
            var f = go.AddComponent<MeshFilter>();
            f.sharedMesh = mesh;
            if (m != null) { r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            return go;
        }

        /// <summary>
        /// Closed torus rim lying in the wheel's XY plane (axis +Z), built
        /// double-sided so the back faces shaded from the driver's side stay
        /// lit. One mesh, no seams, no gaps -- the rim must read as a circle.
        /// </summary>
        private static Mesh BuildRimMesh(float ringR, float tubeR, int major, int minor)
        {
            int ring = minor + 1;
            var verts = new Vector3[(major + 1) * ring];
            var normals = new Vector3[verts.Length];
            for (int i = 0; i <= major; i++)
            {
                float u = (float)i / major * Mathf.PI * 2f, cu = Mathf.Cos(u), su = Mathf.Sin(u);
                for (int j = 0; j <= minor; j++)
                {
                    float v = (float)j / minor * Mathf.PI * 2f, cv = Mathf.Cos(v), sv = Mathf.Sin(v);
                    verts[i * ring + j] = new Vector3((ringR + tubeR * cv) * cu, (ringR + tubeR * cv) * su, tubeR * sv);
                    normals[i * ring + j] = new Vector3(cu * cv, su * cv, sv);
                }
            }
            var tris = new int[major * minor * 12];
            int t = 0;
            for (int i = 0; i < major; i++)
            {
                for (int j = 0; j < minor; j++)
                {
                    int a = i * ring + j, b = a + ring, c = a + 1, d = b + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = b; tris[t++] = d; tris[t++] = c;
                }
            }
            var m = new Mesh { name = "Wheel_Rim_Mesh" };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
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
