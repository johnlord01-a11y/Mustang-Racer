using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// Builds the stage 3 test circuit out of primitives, from the numbers in
    /// <see cref="TrackLayout"/> — nothing about the shape is authored here, so the
    /// layout tests and the scene cannot drift apart.
    ///
    /// Children created by <see cref="Build"/>:
    ///   Ground                one plane primitive, scaled to bury the whole oval,
    ///                         its mesh collider being the single driving surface;
    ///   Road/Road_###         thin cubes 12 m wide, 0.1 m thick, laid end to end on
    ///                         the centreline with a little overlap so the ribbon is
    ///                         continuous; colliders removed — overlapping boxes along
    ///                         the seams is exactly how wheel colliders get snagged,
    ///                         and the plane underneath already stops the car;
    ///   Walls/Wall_Inner_###  cubes 1 m high, 0.5 m thick, 6.5 m off the centreline on
    ///         Wall_Outer_###  the inside (the circuit is driven counter-clockwise, so
    ///                         the interior is to the driver's left) and outside;
    ///   Checkpoints/Checkpoint_#  triggers 14 x 4 x 2 m with a <see cref="TrackCheckpoint"/>.
    ///
    /// Call <see cref="Build"/> explicitly (an editor bootstrap, a scene harness) or let
    /// <c>buildOnAwake</c> do it. Calling it again rebuilds from scratch. The builder's
    /// own transform should stay at unit scale: pieces are placed and oriented in world
    /// space and only their size is local.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrackBuilder : MonoBehaviour
    {
        // ---- primitive dimensions ----------------------------------------------------

        /// <summary>Vertical size of a road piece; the road lies 0.1 m proud of the ground.</summary>
        public const float RoadThickness = 0.1f;

        /// <summary>Height of a wall piece above the road surface.</summary>
        public const float WallHeight = 1f;

        /// <summary>Wall thickness across the traffic lane.</summary>
        public const float WallThickness = 0.5f;

        /// <summary>Distance from the centreline to the inner face line of either wall.</summary>
        public const float WallOffset = 6.5f;

        /// <summary>
        /// Arc length between road/wall pieces. The centreline itself is sampled at 2 m
        /// for measurement; building one cube per metre would mean some 1150 GameObjects
        /// and colliders for a track nobody can crash into at that resolution, so the
        /// geometry uses 8 m pieces (about 1.9 degrees through a turn — the chord of such
        /// a piece falls a centimetre short of the arc, well inside the wall thickness).
        /// </summary>
        public const float SegmentSpacing = 8f;

        /// <summary>How much longer than the gap a piece is made, so neighbours overlap.</summary>
        public const float SegmentOverlap = 0.4f;

        /// <summary>Ground that sticks out this far beyond the outer wall.</summary>
        public const float GroundMargin = 40f;

        /// <summary>Checkpoint trigger size: 14 m across a 12 m road, 4 m high, 2 m deep.</summary>
        public static readonly Vector3 CheckpointTriggerSize = new Vector3(14f, 4f, 2f);

        /// <summary>Surface shader tried first — present only when a URP package is installed.</summary>
        public const string PreferredShaderName = "Universal Render Pipeline/Lit";

        /// <summary>Surface shader used otherwise — the built-in pipeline's Standard.</summary>
        public const string FallbackShaderName = "Standard";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit
        private static readonly int ColorId = Shader.PropertyToID("_Color");         // Standard

        [SerializeField] private bool buildOnAwake = true;

        /// <summary>Materials and physics materials this component created and must clean up.</summary>
        private readonly List<UnityEngine.Object> _ownedAssets = new List<UnityEngine.Object>();

        private TrackCheckpoint[] _checkpoints = Array.Empty<TrackCheckpoint>();
        private Pose _spawn;
        private bool _hasBuilt;

        /// <summary>The four checkpoints of the built track, index 0 being the line. Empty before <see cref="Build"/>.</summary>
        public TrackCheckpoint[] Checkpoints => _checkpoints;

        /// <summary>Where the car goes: on the start straight, 6 m short of the line.</summary>
        public Pose Spawn => _hasBuilt ? _spawn : TrackLayout.SpawnPose();

        /// <summary>True once <see cref="Build"/> has run to completion.</summary>
        public bool HasBuilt => _hasBuilt;

        // ---- colours -----------------------------------------------------------------

        private static readonly Color GroundColor = new Color(0.20f, 0.29f, 0.17f);
        // Visibility pass: dark charcoal asphalt, and both barriers in alternating high-contrast
        // panels (inner red/white, outer blue/white) so either wall reads clearly against the road.
        public static readonly Color RoadColor = new Color(0.09f, 0.09f, 0.10f);
        public static readonly Color InnerWallColor = new Color(1.00f, 0.15f, 0.10f);
        public static readonly Color OuterWallColor = new Color(0.10f, 0.45f, 1.00f);
        public static readonly Color PanelWhite = new Color(0.95f, 0.95f, 0.95f);
        public static readonly Color EdgeLineColor = new Color(0.97f, 0.97f, 0.97f);

        /// <summary>Visual-only white edge line: width, height above the road top, inset from the road edge.</summary>
        public const float EdgeLineWidth = 0.15f;
        public const float EdgeLineRaise = 0.01f;
        public const float EdgeLineInset = 0.3f;

        /// <summary>Colour of wall panel <paramref name="index"/>: even panels coloured, odd panels white.</summary>
        public static Color WallPanelColor(bool inner, int index) =>
            index % 2 == 0 ? (inner ? InnerWallColor : OuterWallColor) : PanelWhite;

        private void Awake()
        {
            if (buildOnAwake)
            {
                Build();
            }
        }

        /// <summary>
        /// Create the circuit from scratch beneath this transform: ground, road ribbon,
        /// both walls and the checkpoints of <see cref="TrackLayout.CheckpointPoses"/>.
        /// Whatever this component built before is thrown away first, so it is safe to
        /// call repeatedly after changing the layout.
        /// </summary>
        public void Build()
        {
            Clear();

            _spawn = TrackLayout.SpawnPose();

            int pieces = Mathf.Max(8, Mathf.CeilToInt(TrackLayout.Length / SegmentSpacing));
            float step = TrackLayout.Length / pieces;

            Shader surface = FindSurfaceShader();

            BuildGround(CreateSurfaceMaterial(surface, "Track_Ground", GroundColor));
            BuildRoadAndWalls(pieces, step, surface);
            BuildCheckpoints();

            _hasBuilt = true;

            Debug.Log($"[TrackBuilder] perimeter {TrackLayout.Length:F1} m, {pieces} road pieces, " +
                      $"{pieces * 2} wall pieces, {_checkpoints.Length} checkpoints, spawn " +
                      $"({Spawn.position.x:F1}, {Spawn.position.y:F1}, {Spawn.position.z:F1}).");
        }

        private void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyNow(transform.GetChild(i).gameObject);
            }

            foreach (UnityEngine.Object asset in _ownedAssets)
            {
                DestroyNow(asset);
            }

            _ownedAssets.Clear();
            _checkpoints = Array.Empty<TrackCheckpoint>();
            _hasBuilt = false;
        }

        /// <summary>
        /// One plane primitive stretched over the whole oval (the widest reach is the
        /// straights plus a turn plus a wall, then <see cref="GroundMargin"/> to run off
        /// into). Its mesh collider is the surface the car's wheel colliders rest on.
        /// </summary>
        private void BuildGround(Material material)
        {
            float half = TrackLayout.StraightLength * 0.5f
                         + TrackLayout.TurnRadius + WallOffset + GroundMargin;

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(transform, false);
            ground.transform.localPosition = Vector3.zero;
            ground.transform.localRotation = Quaternion.identity;
            ground.transform.localScale = new Vector3(half * 2f / 10f, 1f, half * 2f / 10f);

            ApplyMaterial(ground, material);
        }

        /// <summary>
        /// Lay the road ribbon and both walls by stepping round the centreline. Each piece
        /// is the <em>chord</em> between the two ends of its stretch of road at its own
        /// lateral offset rather than a box carrying the rotation of the pose it sits on:
        /// consecutive chords share their endpoints, so the faces of neighbouring pieces
        /// meet instead of leaving the corner-to-corner gap that tangent-placed boxes open
        /// up on a turn, where the outside of the bend travels further than the centreline
        /// does. The walls ride <c>pose.right</c>, which points away from the interior of a
        /// counter-clockwise circuit, so a positive offset is the outer wall and a negative
        /// one the inner. Distances outside one lap — the half step that piece 0 starts
        /// behind the line — are folded back onto the loop by
        /// <see cref="TrackLayout.PoseAt"/>, which takes any distance at all.
        /// </summary>
        private void BuildRoadAndWalls(int pieces, float step, Shader surface)
        {
            Transform roadRoot = NewChildRoot("Road");
            Transform wallRoot = NewChildRoot("Walls");

            Material roadMaterial = CreateSurfaceMaterial(surface, "Track_Road", RoadColor);
            Material innerMaterial = CreateSurfaceMaterial(surface, "Track_Wall_Inner", InnerWallColor);
            Material outerMaterial = CreateSurfaceMaterial(surface, "Track_Wall_Outer", OuterWallColor);
            Material whiteMaterial = CreateSurfaceMaterial(surface, "Track_Wall_White", PanelWhite);
            Material lineMaterial = CreateSurfaceMaterial(surface, "Track_EdgeLine", EdgeLineColor);
            Transform lineRoot = NewChildRoot("EdgeLines");
            PhysicsMaterial wallPhysics = CreateWallPhysicsMaterial();

            float halfStep = step * 0.5f;

            for (int i = 0; i < pieces; i++)
            {
                Pose start = TrackLayout.PoseAt(i * step - halfStep);
                Pose end = TrackLayout.PoseAt(i * step + halfStep);

                // The ribbon has to be as long as its own longest edge — on a turn that is
                // the edge round the outside of the bend — so neither edge of the road is
                // notched between one piece and the next.
                float roadLength = Mathf.Max(
                    ChordLength(start, end, TrackLayout.RoadWidth * 0.5f),
                    ChordLength(start, end, -TrackLayout.RoadWidth * 0.5f)) + SegmentOverlap;

                CreateCube($"Road_{i:000}",
                    ChordPose(start, end, 0f, RoadThickness * 0.5f),
                    new Vector3(TrackLayout.RoadWidth, RoadThickness, roadLength),
                    roadRoot, roadMaterial, removeCollider: true);

                bool colouredPanel = i % 2 == 0;
                CreateWall(i, "Inner", -WallOffset, start, end, wallRoot,
                    colouredPanel ? innerMaterial : whiteMaterial, wallPhysics);
                CreateWall(i, "Outer", WallOffset, start, end, wallRoot,
                    colouredPanel ? outerMaterial : whiteMaterial, wallPhysics);

                // Continuous white edge lines, visual only (no collider), just above the road top.
                float lineOffset = TrackLayout.RoadWidth * 0.5f - EdgeLineInset;
                float lineY = RoadThickness + EdgeLineRaise * 0.5f;
                CreateCube($"EdgeLine_Inner_{i:000}",
                    ChordPose(start, end, -lineOffset, lineY),
                    new Vector3(EdgeLineWidth, EdgeLineRaise, ChordLength(start, end, -lineOffset) + SegmentOverlap),
                    lineRoot, lineMaterial, removeCollider: true);
                CreateCube($"EdgeLine_Outer_{i:000}",
                    ChordPose(start, end, lineOffset, lineY),
                    new Vector3(EdgeLineWidth, EdgeLineRaise, ChordLength(start, end, lineOffset) + SegmentOverlap),
                    lineRoot, lineMaterial, removeCollider: true);
            }
        }

        /// <summary>
        /// One wall piece spanning the chord at its own lateral offset, so its two ends sit
        /// on the same arc as the ends of the pieces before and after it and the string of
        /// boxes closes up on itself instead of exposing corners to a wheel.
        /// </summary>
        private void CreateWall(
            int index, string side, float lateralOffset, Pose start, Pose end,
            Transform parent, Material material, PhysicsMaterial physics)
        {
            GameObject wall = CreateCube(
                $"Wall_{side}_{index:000}",
                ChordPose(start, end, lateralOffset, WallHeight * 0.5f),
                new Vector3(WallThickness, WallHeight,
                    ChordLength(start, end, lateralOffset) + SegmentOverlap),
                parent, material, removeCollider: false);

            Collider collider = wall.GetComponent<Collider>();
            if (collider != null && physics != null)
            {
                collider.sharedMaterial = physics;
            }
        }

        /// <summary>
        /// The pose of a piece running straight from <paramref name="start"/> to
        /// <paramref name="end"/> — the centreline poses at the two ends of one segment —
        /// offset sideways along <c>right</c> by <paramref name="lateralOffset"/> and lifted
        /// by <paramref name="height"/>: centred on that chord and pointed along it, level
        /// with the ground plane, so a cube scaled by the length it is given covers the
        /// stretch it belongs to end to end.
        /// </summary>
        private static Pose ChordPose(Pose start, Pose end, float lateralOffset, float height)
        {
            Vector3 a = start.position + start.right * lateralOffset;
            Vector3 b = end.position + end.right * lateralOffset;
            Vector3 forward = b - a;
            forward.y = 0f;

            if (forward.sqrMagnitude < 1e-8f)
            {
                forward = start.rotation * Vector3.forward;
                forward.y = 0f;
            }

            return new Pose((a + b) * 0.5f + Vector3.up * height,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        /// <summary>
        /// The straight distance between the two ends of one segment at
        /// <paramref name="lateralOffset"/> metres across the centreline. It is shorter than
        /// the arc it stands in for by whatever the chord cuts the corner, and shorter on
        /// the inside of a turn than on the outside — which is exactly why every offset gets
        /// its own chord instead of one shared length.
        /// </summary>
        private static float ChordLength(Pose start, Pose end, float lateralOffset)
        {
            Vector3 a = start.position + start.right * lateralOffset;
            Vector3 b = end.position + end.right * lateralOffset;
            Vector3 chord = b - a;
            chord.y = 0f;

            return chord.magnitude;
        }

        /// <summary>
        /// Checkpoint triggers, index 0 = the line. The volume is centred 2 m up so the
        /// 4 m tall box straddles the road surface from 0 to 4 m, while the transform
        /// itself stays on the ground and level with the driving direction — that is the
        /// axis <see cref="TrackCheckpoint.IsForward"/> judges a crossing by.
        /// </summary>
        private void BuildCheckpoints()
        {
            Pose[] poses = TrackLayout.CheckpointPoses(TrackLayout.DefaultCheckpointCount);
            Transform checkpointRoot = NewChildRoot("Checkpoints");

            var created = new TrackCheckpoint[poses.Length];
            for (int i = 0; i < poses.Length; i++)
            {
                GameObject volume = new GameObject($"Checkpoint_{i}");
                volume.transform.SetParent(checkpointRoot, false);
                volume.transform.SetPositionAndRotation(poses[i].position, poses[i].rotation);

                var trigger = volume.AddComponent<BoxCollider>();
                trigger.size = CheckpointTriggerSize;
                trigger.center = new Vector3(0f, CheckpointTriggerSize.y * 0.5f, 0f);
                trigger.isTrigger = true;

                TrackCheckpoint checkpoint = volume.AddComponent<TrackCheckpoint>();
                checkpoint.index = i;

                created[i] = checkpoint;
            }

            _checkpoints = created;
        }

        // ---- primitives --------------------------------------------------------------

        private Transform NewChildRoot(string rootName)
        {
            var root = new GameObject(rootName);
            root.transform.SetParent(transform, false);
            return root.transform;
        }

        /// <summary>
        /// A unit cube scaled to <paramref name="size"/> and posed in world space. Road
        /// pieces ask for their collider back gone, so the driving surface stays the one
        /// plane underneath instead of a chain of boxes with steps at every seam.
        /// </summary>
        private GameObject CreateCube(
            string pieceName, Pose pose, Vector3 size, Transform parent, Material material, bool removeCollider)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = pieceName;
            cube.transform.SetParent(parent, false);
            cube.transform.SetPositionAndRotation(pose.position, pose.rotation);
            cube.transform.localScale = size;

            if (removeCollider)
            {
                Collider collider = cube.GetComponent<Collider>();
                if (collider != null)
                {
                    DestroyNow(collider);
                }
            }

            ApplyMaterial(cube, material);
            return cube;
        }

        private static void ApplyMaterial(GameObject target, Material material)
        {
            if (material == null)
            {
                return; // No usable shader: the primitive keeps its default material.
            }

            var renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        // ---- materials ---------------------------------------------------------------

        /// <summary>
        /// Surface shader for everything this track builds: the URP lit shader when a
        /// render-pipeline package provides one, otherwise the built-in Standard shader
        /// (which is what this project has). Null when neither is available — callers
        /// then leave the primitives on their default material rather than wrapping a
        /// Material around a missing shader.
        /// </summary>
        private static Shader FindSurfaceShader()
        {
            Shader shader = Shader.Find(PreferredShaderName);
            if (shader == null)
            {
                shader = Shader.Find(FallbackShaderName);
            }

            if (shader == null)
            {
                Debug.LogWarning($"[TrackBuilder] neither '{PreferredShaderName}' nor " +
                                 $"'{FallbackShaderName}' could be found; track pieces keep " +
                                 "their default material.");
            }

            return shader;
        }

        private Material CreateSurfaceMaterial(Shader shader, string materialName, Color color)
        {
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave
            };

            // URP Lit keeps its albedo in _BaseColor, Standard in _Color; set whichever
            // the resolved shader actually declares.
            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, color);
            }

            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, color);
            }

            _ownedAssets.Add(material);
            return material;
        }

        /// <summary>
        /// Wall surfaces must not throw the car back out into the road: no bounce, and
        /// the Minimum combine so the less springy of the two contacting materials wins
        /// even if the chassis ever arrives wearing something bouncier.
        /// </summary>
        private PhysicsMaterial CreateWallPhysicsMaterial()
        {
            var physics = new PhysicsMaterial("Track_Wall")
            {
                hideFlags = HideFlags.DontSave,
                staticFriction = 0.6f,
                dynamicFriction = 0.5f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

            _ownedAssets.Add(physics);
            return physics;
        }

        // ---- lifetime ----------------------------------------------------------------

        /// <summary>The runtime materials and physics material were created by us, so we free them.</summary>
        private void OnDestroy()
        {
            foreach (UnityEngine.Object asset in _ownedAssets)
            {
                DestroyNow(asset);
            }

            _ownedAssets.Clear();
        }

        /// <summary>
        /// Deferred in play mode, immediate in the editor: <see cref="Build"/> may run
        /// from Awake in one and from an editor bootstrap in the other, and a rebuild
        /// must not leave the previous set of children alive alongside the new one.
        /// </summary>
        private static void DestroyNow(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }




    }
}
