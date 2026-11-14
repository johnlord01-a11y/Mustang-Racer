using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenWheelRacer
{
    /// Seated driver's view (default) of the Mustang GT, LEFT seat (US car).
    /// The rig is a ROUND 3-spoke steering wheel — one joined torus rim,
    /// silver spokes, dark hub, navy gloved hands at 9 and 3 — that turns with
    /// the car's actual steer angle, over a short column into the dash front.
    /// There is NO speed/gear readout in the cockpit at all (round-3 brief:
    /// the projected "N 0" text read as a plate pasted below the wheel; the
    /// simplest acceptable outcome is no readout, and the centre touchscreen
    /// stays a plain lit screen). The eye sits high and BACK in the tub — real
    /// seating depth behind the wheel (0.65 m) — and pitches down 24 degrees
    /// at 90 degrees FOV, the round-3 geometry that puts the whole dash set
    /// (twin gauge pods, three centre vents, pop-up touchscreen, manual
    /// shifter) inside one frame over the rim, with the road and sky still
    /// filling the top. The hood, windshield frame, pillars, dash set and
    /// shifter all come from <see cref="MustangMeshBuilder"/>'s body, so one
    /// cabin is shared with the chase cameras. C toggles back to the existing
    /// ChaseCamera (near clip and field of view restore; the chase snaps to
    /// its rest pose so the first outside frame is not stuck mid-lerp inside
    /// the body). All cockpit geometry is collider-free and parented to the
    /// player's car only.
    [RequireComponent(typeof(Camera))]
    public sealed class CockpitView : MonoBehaviour
    {
        /// <summary>Driver eye in the LEFT seat at real seating depth: 0.65 m
        /// behind the wheel plane so the dash set (z 0.15..0.43) lies 0.7..1.0 m
        /// ahead and the console/shifter (z -0.15) still sits 0.40 m ahead of
        /// the eye instead of under it (round-2 lesson: an eye at z -0.34 put
        /// the shifter BEHIND the camera and the screen below the frame).
        /// Seat-back tops (y 0.63) stay below the bottom frame edge, so the
        /// seat never intrudes.</summary>
        public static readonly Vector3 EyeLocal = new Vector3(-0.10f, 1.02f, -0.55f);

        /// <summary>Head-down GT pose: pitch axis 24 degrees below the horizon
        /// puts the dash band (21..33 deg below the eye) across the middle of
        /// the frame and the shifter (49.5 deg) at v -0.89, while the frame top
        /// still clears the horizon by 4.5 degrees (road + sky in shot).</summary>
        public const float EyePitchDown = 24f;

        /// <summary>Wide seated field of view; at 90 horizontal the vertical
        /// half-angle is ~28.5 degrees at 16:9, the window that shows the pods
        /// beside the rim, the vents and screen between them, and both
        /// A-pillar roots at the frame edges.</summary>
        public const float CockpitFieldOfView = 90f;

        public const float WheelTurnRatio = 6f;   // wheel degrees per road-wheel degree
        public const float NearClip = 0.05f;

        public bool CockpitActive { get; private set; } = true;
        public Transform Rig => _rig;
        public Transform SteeringWheel => _wheel;

        private VehicleController _car;
        private ChaseCamera _chase;
        private Camera _camera;
        private Transform _rig, _wheel;
        private Renderer[] _ownGlass;
        private float _defaultNear, _defaultFov;

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

        private void BuildRig(Transform car)
        {
            var black = MakeMaterial(new Color(0.045f, 0.045f, 0.05f));
            var rimDark = MakeMaterial(new Color(0.15f, 0.15f, 0.17f));
            var silver = MakeMaterial(new Color(0.85f, 0.86f, 0.88f));
            var navy = MakeMaterial(new Color(0.10f, 0.10f, 0.13f));

            _rig = new GameObject("Cockpit_Rig").transform;
            _rig.SetParent(car, false);

            // ROUND steering wheel in front of the left seat, rim leaning back
            // 20 degrees: rim torus 0.14 ring / 0.022 tube, centre y 0.68 —
            // the round-3 drop so the sight lines to the touchscreen and the
            // pods pass through the rim OPENING (r<0.118) or outside the tube
            // (r>0.162) instead of through the arc the 0.735 height put across
            // them. Rim top 0.842 stays a hand's width below the 1.02 eye.
            _wheel = new GameObject("Cockpit_Wheel").transform;
            _wheel.SetParent(_rig, false);
            _wheel.localPosition = new Vector3(-0.10f, 0.68f, 0.105f);
            // Joined torus rim, one closed mesh (round 1's fourteen separate
            // cubes gapped into a scalloped ring in every cockpit frame).
            MeshPiece("Wheel_Rim", _wheel, Vector3.zero, Vector3.zero, BuildRimMesh(0.140f, 0.022f, 32, 12), rimDark);
            // Silver side spokes SHORTENED (0.075 long, out to r 0.0965) so
            // they stop where the button pads start (pad inner edge 0.0775):
            // the round-2 spoke length put a 12.5 cm silver bar across the
            // sight line to the centre touchscreen at this seating depth.
            // Bottom spoke unchanged; pads bridge spoke-to-rim as before.
            Piece("Wheel_SpokeL", PrimitiveType.Cube, _wheel, new Vector3(-0.059f, -0.030f, 0f), Vector3.zero, new Vector3(0.075f, 0.030f, 0.024f), silver);
            Piece("Wheel_SpokeR", PrimitiveType.Cube, _wheel, new Vector3(0.059f, -0.030f, 0f), Vector3.zero, new Vector3(0.075f, 0.030f, 0.024f), silver);
            Piece("Wheel_SpokeB", PrimitiveType.Cube, _wheel, new Vector3(0f, -0.082f, 0f), Vector3.zero, new Vector3(0.030f, 0.128f, 0.024f), silver);
            Piece("Wheel_PadL", PrimitiveType.Cube, _wheel, new Vector3(-0.115f, 0.032f, 0.016f), Vector3.zero, new Vector3(0.075f, 0.040f, 0.012f), black);
            Piece("Wheel_PadR", PrimitiveType.Cube, _wheel, new Vector3(0.115f, 0.032f, 0.016f), Vector3.zero, new Vector3(0.075f, 0.040f, 0.012f), black);
            // Hub: DARK centre pad inside the rim, slimmed to 8.8 cm so the
            // cluster-screen sight line (r 0.047 at the wheel plane) passes
            // beside it; the round-2 "black hub, no chrome dinner plate"
            // pattern is unchanged.
            Piece("Wheel_HubRing", PrimitiveType.Cylinder, _wheel, new Vector3(0f, 0.003f, -0.006f), new Vector3(90f, 0f, 0f), new Vector3(0.088f, 0.010f, 0.088f), black);
            // Navy gloves gripping the rim at 9 and 3.
            Piece("Hand_L", PrimitiveType.Capsule, _wheel, new Vector3(-0.152f, 0.038f, -0.012f), new Vector3(0f, 0f, 72f), new Vector3(0.062f, 0.088f, 0.062f), navy);
            Piece("Hand_R", PrimitiveType.Capsule, _wheel, new Vector3(0.152f, 0.038f, -0.012f), new Vector3(0f, 0f, -72f), new Vector3(0.062f, 0.088f, 0.062f), navy);
            // Short column from the hub into the dash front (front face z
            // 0.43): hub -> mid -> dash, re-aimed for the 0.68 hub, ending
            // buried inside DashFront. Boxes silhouette better than lit
            // cylinders, so two crisp angled boxes.
            Piece("Wheel_ColumnA", PrimitiveType.Cube, _rig, new Vector3(-0.10f, 0.656f, 0.190f), new Vector3(102.9f, 0f, 0f), new Vector3(0.050f, 0.145f, 0.050f), silver);
            Piece("Wheel_ColumnB", PrimitiveType.Cube, _rig, new Vector3(-0.10f, 0.627f, 0.3225f), new Vector3(101.3f, 0f, 0f), new Vector3(0.050f, 0.130f, 0.050f), silver);
            // No readout anchor and no OnGUI: round 3 removed the projected
            // gear/speed text entirely (the "plate" in the reviews).
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
