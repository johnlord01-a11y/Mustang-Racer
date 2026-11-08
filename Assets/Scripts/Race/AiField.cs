using System.Collections.Generic;
using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// Builds the opponents and stands them on the grid behind the player (PLAN.md Stage 6, first
    /// milestone: a field that races, not a field that is clever).
    ///
    /// A static helper rather than a component, for the reason <see cref="RaceBootstrap"/> is one:
    /// there is nothing in the scene to put a component on, and a field assembled in code cannot go
    /// stale when the car factory or the layout changes under it.
    ///
    /// Each opponent is built exactly the way the player's car is built —
    /// <see cref="VehicleController.BuildCar{Tuning}"/> with the runtime default tuning, so nobody is
    /// faster than anybody for a reason the garage did not decide — with one difference that matters:
    /// <c>humanControlled</c> is false, which is what keeps three of them off the player's keyboard
    /// and off the player's reset key. Then an <see cref="AiDriver"/> goes on the chassis to drive it,
    /// and is handed the same checkpoints the session is watching, each of them filtering the crossings
    /// down to its own car.
    ///
    /// <b>Where they go.</b> Behind the player, two cars to a row, each row
    /// <see cref="RowSpacingM"/> metres further up the lane and the pair straddling the centreline at
    /// <see cref="LaneOffsetM"/> either side. A staggered grid rather than a row across the road
    /// because a road this wide put three cars abreast would be a start-line incident before the race
    /// had started, and it puts every car in a lane it can hold through the first corner. The distances
    /// are measured round the centreline and folded into the lap by <see cref="TrackLayout.PoseAt"/>,
    /// so a grid that runs back past the finish line keeps running back into the lap rather than off
    /// the end of the track, and no two cars are ever closer than <see cref="MinSeparationM"/> — the
    /// guard that keeps them apart has to be there before the physics does, because cars that start
    /// inside each other separate by exploding.
    ///
    /// <b>What they look like.</b> <see cref="VehicleController.BuildCar"/> builds physics and no
    /// graphics: a rigid body, a chassis box, four wheel colliders. That is invisible but harmless when
    /// the only car in the race is the one the player cannot see themselves in, and it is not harmless
    /// once the cars that have to be read at 25 m/s are somebody else's. So each opponent gets one
    /// cube the size of the chassis box it stands inside, painted in its own colour with the same
    /// shader and the same colour property <see cref="TrackBuilder"/> paints the track with, and no
    /// collider of its own.
    /// </summary>
    public static class AiField
    {
        /// <summary>Opponents built when nothing says how many the race wants.</summary>
        public const int DefaultOpponentCount = 3;

        /// <summary>
        /// How much further back each successive row starts, measured along the centreline, m. The
        /// chassis is 3.7 m long, so this is a car and a half: enough that nothing is alongside anything
        /// at the lights and enough that the field is still on one straight.
        /// </summary>
        public const float RowSpacingM = 8f;

        /// <summary>
        /// How far off the centreline the two lanes sit, m, one either side. Half the width of the road is
        /// 6 m and a car is 1.7 across, so a pair like this leaves nearly two metres of road between them
        /// and another two and a half to the wall, which is room for a third car between them — what the
        /// first corner tends to ask for, and what the player parked on the centreline needs in order to be
        /// passed without being touched: a lane at half this sat 2.5 m off a stationary car, which is less
        /// than a metre of road between the two bodies and a nudge as sure as it was close.
        /// </summary>
        public const float LaneOffsetM = 3.5f;

        /// <summary>
        /// Closest two cars on the grid are allowed to end up, m. Longer than a chassis, so a car that
        /// would have started inside another is pushed back a row instead.
        /// </summary>
        public const float MinSeparationM = 5f;

        /// <summary>Rows this helper will push a car back through looking for room before giving up.</summary>
        public const int MaxPlacementPushes = 8;

        /// <summary>Body colours, in order: red, blue, yellow, then round again.</summary>
        private static readonly Color[] BodyColors =
        {
            new Color(0.72f, 0.18f, 0.15f),
            new Color(0.16f, 0.36f, 0.78f),
            new Color(0.88f, 0.75f, 0.16f)
        };

        /// <summary>Where the chassis box sits on the car, so the body cube lands on it.</summary>
        private static readonly Vector3 BodyCentre = new Vector3(0f, 0.35f, 0f);

        /// <summary>Size of the chassis box <see cref="VehicleController.BuildCar"/> stands up.</summary>
        private static readonly Vector3 BodySize = new Vector3(1.7f, 0.6f, 3.7f);


        /// <summary>
        /// Build <paramref name="count"/> opponents and place them on the grid behind the player, each
        /// one already driving itself.
        /// </summary>
        /// <param name="track">
        /// The built track: its checkpoints size and feed every opponent's lap counter, and its grid
        /// slot is what the field is placed behind. Null is taken for no race to join rather than a
        /// fault — a bootstrap that cannot build a field should still leave a race that runs, and it
        /// returns an empty list in that case.
        /// </param>
        /// <param name="count">How many opponents to build. Nothing for zero or fewer.</param>
        /// <returns>The drivers, in the order they were placed, oldest row first. Never null.</returns>
        public static List<AiDriver> Spawn(TrackBuilder track, int count = DefaultOpponentCount)
        {
            var drivers = new List<AiDriver>(count > 0 ? count : 0);

            if (track == null)
            {
                Debug.LogWarning("[AiField] no track to line up behind; racing nobody.");
                return drivers;
            }

            if (count <= 0)
            {
                return drivers;
            }

            Shader shader = FindBodyShader();

            // The field is placed behind wherever the player's car actually stands rather than behind a
            // grid slot worked out again here, and by the same measurement the opponents use to drive:
            // a bootstrap that ever moves the player's slot moves the field with it.
            float playerDistance = AiDriver.TrackDistanceAt(track.Spawn.position);

            // Everything already standing in the lane, the player among them, that a new car must not
            // be put on top of.
            var placed = new List<Vector3> { track.Spawn.position };

            for (int i = 0; i < count; i++)
            {
                float lane = (i % 2 == 0 ? 1f : -1f) * LaneOffsetM;
                string name = $"AI_{i + 1}";
                Color color = BodyColors[i % BodyColors.Length];

                Pose pose = GridPose(playerDistance, lane, i + 1, placed);

                // The runtime default tuning, so nobody in the field is faster than anybody for a
                // reason the garage did not decide, and humanControlled false, which is what keeps
                // three of them off the player's keyboard and off the player's reset key.
                GameObject chassis = VehicleController.BuildCar(null, humanControlled: false);
                chassis.name = name;

                VehicleController car = chassis.GetComponent<VehicleController>();
                if (car == null)
                {
                    // The factory always wires one; a build that did not is not worth driving, and
                    // worth less than a warning that says so.
                    Debug.LogWarning($"[AiField] {name} came out of the car factory without a controller.");
                    return drivers;
                }

                Material material;
                PaintBody(chassis, color, shader, name, out material);
                Place(chassis, car, pose);
                car.ArmSpawnPoint();

                AiDriver driver = chassis.AddComponent<AiDriver>();

                // A slightly different pace per opponent, fastest first: the reference driver
                // drives the tuning exactly, the ones behind it trail it a few percent so the
                // field spreads out down a straight instead of arriving as one three-car train.
                int paceSlot = i % AIDriverProfile.DefaultFieldPaceScales.Length;
                driver.Profile = AIDriverProfile.CreatePaced(AIDriverProfile.DefaultFieldPaceScales[paceSlot]);

                driver.Init(track, car, name, color, lane, material);

                drivers.Add(driver);
                placed.Add(pose.position);
            }

            return drivers;
        }

        /// <summary>
        /// The pose of a grid slot <paramref name="row"/> rows of <see cref="RowSpacingM"/> metres behind
        /// the player and <paramref name="lane"/> metres off the centreline, pushed back a row at a time
        /// until it is clear of every car already standing there.
        ///
        /// The distances are folded into the lap by <see cref="TrackLayout.PoseAt"/>, so a field long
        /// enough to run back past the finish line carries on back into the lap rather than off the end
        /// of the track; the separation guard is what makes sure nothing starts inside anything, and it
        /// has to be here rather than left to physics, because cars that begin overlapping spend their
        /// first frame pushing each other into the barriers.
        /// </summary>
        private static Pose GridPose(float playerDistance, float lane, int row, List<Vector3> placed)
        {
            float distance = playerDistance;

            for (int attempt = 0; attempt < MaxPlacementPushes; attempt++)
            {
                Pose centre = TrackLayout.PoseAt(distance - RowSpacingM * row);
                Vector3 position = centre.position + centre.right * lane
                                   + Vector3.up * TrackLayout.SpawnHeight;

                if (IsClearOf(position, placed))
                {
                    return new Pose(position, centre.rotation);
                }
            }

            // A row far enough back to have run out of patience: returned rather than refused, because
            // an opponent standing in somebody else's lane is a poor start, and no opponent at all is a
            // race that did not happen.
            Pose last = TrackLayout.PoseAt(distance - RowSpacingM * MaxPlacementPushes);
            return new Pose(last.position + last.right * lane + Vector3.up * TrackLayout.SpawnHeight,
                last.rotation);
        }

        /// <summary>Whether <paramref name="position"/> is at least <see cref="MinSeparationM"/> from
        /// every point already taken.</summary>
        private static bool IsClearOf(Vector3 position, List<Vector3> placed)
        {
            float minSqr = MinSeparationM * MinSeparationM;

            for (int i = 0; i < placed.Count; i++)
            {
                if ((placed[i] - position).sqrMagnitude < minSqr)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Stand a freshly built car on its grid slot. The factory leaves the chassis at the world origin
        /// with its physics body already assembled, so — exactly as
        /// <see cref="RaceBootstrap"/>'s own placement does — the body is moved because physics answers to
        /// it and the transform is moved because that is what the camera and the flip test read on the
        /// frame the car appears, and both are stopped dead so a car spawned on top of another's velocity
        /// does not inherit it.
        /// </summary>
        private static void Place(GameObject chassis, VehicleController car, Pose slot)
        {
            chassis.transform.SetPositionAndRotation(slot.position, slot.rotation);

            Rigidbody body = car.PhysicsBody;
            if (body == null)
            {
                return;
            }

            body.position = slot.position;
            body.rotation = slot.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// Give the car a body to be seen wearing in this opponent's colour. The body itself —
        /// the open-wheel primitives of <see cref="VehicleController.BuildCar"/>'s
        /// <see cref="VehicleController.BodyRootName"/> root — exists already; all this does is
        /// make one material in the opponent's colour and put it on every piece of that body.
        ///
        /// A car whose body root is missing (defence against a chassis built some other way)
        /// still gets the old fallback: one collider-free cube the size of the chassis box.
        /// </summary>
        /// <param name="chassis">The car to fit out.</param>
        /// <param name="color">The colour of the body.</param>
        /// <param name="shader">Body shader, or null to leave the body on its default material.</param>
        /// <param name="name">Name of the car, which a fallback body cube is named after.</param>
        /// <param name="material">
        /// The material created for it — the caller hands it to the driver, whose own destruction frees
        /// it. Null when there was no shader to make one from.
        /// </param>
        private static void PaintBody(GameObject chassis, Color color, Shader shader, string name,
            out Material material)
        {
            material = null;

            Transform bodyRoot = chassis.transform.Find(VehicleController.BodyRootName);
            if (bodyRoot == null)
            {
                // No body to paint: wear the old plain cube rather than be invisible.
                var fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fallback.name = $"{name}_Body";
                fallback.transform.SetParent(chassis.transform, false);
                fallback.transform.localPosition = BodyCentre;
                fallback.transform.localScale = BodySize;

                Collider fallbackCollider = fallback.GetComponent<Collider>();
                if (fallbackCollider != null)
                {
                    // The chassis box already is this cube's shape; two colliders on one car is a car
                    // that collides with itself.
                    Object.Destroy(fallbackCollider);
                }

                bodyRoot = fallback.transform;
            }

            if (shader == null)
            {
                return; // Nothing to paint with: default-material parts still beat an invisible car.
            }

            material = new Material(shader)
            {
                name = $"{name}_BodyMaterial",
                hideFlags = HideFlags.DontSave
            };

            // URP Lit keeps its albedo in _BaseColor, Standard in _Color — one helper in
            // VehicleController knows both, so the field and the player's car are painted
            // through exactly the same door.
            VehicleController.PaintColor(material, color);

            Renderer[] renderers = bodyRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = material;
            }
        }

        /// <summary>
        /// The shader the opponents are painted with: the URP lit shader where a render-pipeline package
        /// provides one and the built-in Standard shader otherwise, which is the same pair
        /// <see cref="TrackBuilder"/> resolves for the track so the field and the circuit are drawn the
        /// same way whatever is installed. Null when neither is available.
        /// </summary>
        private static Shader FindBodyShader()
        {
            Shader shader = Shader.Find(TrackBuilder.PreferredShaderName);
            if (shader == null)
            {
                shader = Shader.Find(TrackBuilder.FallbackShaderName);
            }

            if (shader == null)
            {
                Debug.LogWarning($"[AiField] neither '{TrackBuilder.PreferredShaderName}' nor " +
                                 $"'{TrackBuilder.FallbackShaderName}' could be found; opponents keep " +
                                 "the default material.");
            }

            return shader;
        }
    }
}
