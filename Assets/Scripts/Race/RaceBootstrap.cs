using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// Turns the empty <c>Track_Proto</c> scene into a playable session the moment it
    /// finishes loading: track, car on the grid, opponents behind it, chase camera, sun,
    /// session, HUD and pause menu (PLAN.md Stage 3 milestone M1, criterion 1 — drivable from
    /// a cold start; Stage 6 milestone one — a field to race).
    ///
    /// Why a <see cref="RuntimeInitializeOnLoadMethod"/> instead of a component placed in
    /// the scene: there is nothing in the scene to put a component on, and a bootstrap that
    /// lives in code cannot go stale when <see cref="TrackBuilder"/> changes what it
    /// creates or when someone re-saves the scene without it. It also means the whole game
    /// is reproducible from an empty scene file, which is what the PlayMode tests run.
    ///
    /// The two guards are load-bearing. The scene name keeps it out of the Stage 1 sandbox
    /// and out of whatever scene the test runner boots into; the "a session already exists"
    /// check keeps a second, colliding car out of the scene after a reload or a domain
    /// reload with enter-play-mode options disabled — and it counts only a session of the
    /// scene being loaded, so a session that is on its way out cannot hold the assembly back.
    ///
    /// That method alone would cover the cold start and nothing else, because Unity runs a
    /// <c>RuntimeInitializeOnLoadMethod</c> once per application rather than once per scene,
    /// so <see cref="RegisterSceneHook"/> subscribes it to
    /// <see cref="SceneManager.sceneLoaded"/>: that is what makes Esc -&gt; Restart — a scene
    /// unloaded and loaded again — assemble the race a second time.
    ///
    /// Order matters twice over: the track is built before the car, because the car is
    /// placed on the track's spawn pose; and the car is moved before its own
    /// <c>Start</c> runs, so the pose the built-in R key resets to is the grid slot and
    /// not the world origin the chassis was created at.
    /// </summary>
    public static class RaceBootstrap
    {
        /// <summary>The only scene this bootstrap assembles itself into.</summary>
        public const string TrackSceneName = "Track_Proto";

        /// <summary>Host of the track, session, HUD and pause components.</summary>
        public const string RaceRootName = "Race";

        /// <summary>Name given to the camera it creates when the scene has none.</summary>
        public const string CameraName = "Main Camera";

        /// <summary>Name given to the sun it creates when the scene has no light.</summary>
        public const string LightName = "Directional Light";

        /// <summary>The tag <c>Camera.main</c> looks for; assigned to the camera it creates.</summary>
        public const string MainCameraTag = "MainCamera";

        private const float CameraBehind = 7f;
        private const float CameraHeight = 3f;
        private const float CameraAimHeight = 0.6f;
        private const float SunPitch = 52f;
        private const float SunYaw = -34f;
        private const float SunIntensity = 1.15f;

#if !UNITY_EDITOR
        /// <summary>
        /// Has this run already asked the player for a window? Set the first time
        /// <see cref="Boot"/> assembles a race, so the request cannot undo a display mode
        /// the game itself changes to later on. Statics start zeroed at application start,
        /// which is exactly the once-per-run scope wanted here.
        /// </summary>
        private static bool _requestedWindow;
#endif

        /// <summary>
        /// Re-arm the scene hook at the very start of the run.
        ///
        /// A bare <see cref="RuntimeInitializeOnLoadMethodAttribute"/> fires once per
        /// *application* start and never again, so the first <c>LoadScene</c> after it —
        /// Esc -&gt; Restart — used to unload the track with nothing left to put it back:
        /// the player kept showing the last frame of a scene that no longer existed.
        /// Subscribing to <see cref="SceneManager.sceneLoaded"/> makes every load of
        /// <see cref="TrackSceneName"/> an assembly point again.
        ///
        /// The unsubscribe first keeps the subscription to exactly one per run under an
        /// editor with domain reload disabled, where this method runs again while the
        /// delegate field still holds the previous one.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterSceneHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// Assemble the race on <em>any</em> load of <see cref="TrackSceneName"/> — the
        /// cold start, a restart, an additive load. Both guards live inside
        /// <see cref="Boot"/>, so a load that finds a session of this scene already running
        /// stays a no-op rather than producing a second car.
        /// </summary>
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == TrackSceneName)
            {
                Boot();
            }
        }

        /// <summary>
        /// Scene-load entry point. Safe to call by hand (a menu, a test harness, the
        /// console) and called on every load of the track by <see cref="OnSceneLoaded"/>:
        /// it does nothing outside <see cref="TrackSceneName"/> and nothing at all once a
        /// session of that scene is running.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Boot()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != TrackSceneName)
            {
                return;
            }

            if (FindSessionInActiveScene() != null)
            {
                Debug.Log("[RaceBootstrap] a RaceSession is already running; leaving it alone.");
                return;
            }

#if !UNITY_EDITOR
            // The player remembers the fullscreen mode a previous run was left in, and an
            // exclusive fullscreen on a Linux compositor is hard to break out of. Asking
            // for a window the first time a race is assembled — and only then, so a later
            // in-game mode change is never fought over — means the game always opens
            // windowed. SetResolution is deferred by Unity to the start of the next frame.
            if (!_requestedWindow)
            {
                _requestedWindow = true;
                Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            }
#endif

            var root = new GameObject(RaceRootName);

            // AddComponent already ran TrackBuilder.Awake, which builds on awake by
            // default; the explicit call is the fallback for a builder that was added to
            // an inactive object or otherwise skipped its lifecycle message.
            TrackBuilder track = root.AddComponent<TrackBuilder>();
            if (!track.HasBuilt)
            {
                track.Build();
            }

            VehicleController car = PlaceCarOnGrid(track);
            AimCameraAt(car);
            EnsureDirectionalLight();

            var session = root.AddComponent<RaceSession>();
            root.AddComponent<RaceHud>();
            root.AddComponent<GamePause>();
            session.Init(track, car);

            // The field is built after the player's car is on the grid, because it is lined up behind
            // it, and after the session exists, because the session is what ranks the player against it.
            // The camera keeps its target: three cars joining a race is no reason for the player to stop
            // being the one it is following.
            var opponents = AiField.Spawn(track, AiField.DefaultOpponentCount);
            session.SetOpponents(opponents);

            Debug.Log($"[RaceBootstrap] '{TrackSceneName}' assembled: car on spawn " +
                      $"({track.Spawn.position.x:F1}, {track.Spawn.position.y:F1}, " +
                      $"{track.Spawn.position.z:F1}), {track.Checkpoints.Length} checkpoints, " +
                      $"{opponents.Count} opponents, HUD and pause menu live.");
        }

        /// <summary>
        /// The running session of the active scene, or null. A session that belongs to some
        /// other scene is not a live one: on a restart the scene being replaced can still
        /// hold its session the very frame the new scene loads, and honouring it is what
        /// used to leave the reloaded track unassembled — an empty scene, no camera, and the
        /// last frame of the old one still on screen.
        /// </summary>
        private static RaceSession FindSessionInActiveScene()
        {
            Scene active = SceneManager.GetActiveScene();

            foreach (RaceSession session in
                         Object.FindObjectsByType<RaceSession>())
            {
                if (session != null && session.gameObject.scene == active)
                {
                    return session;
                }
            }

            return null;
        }

        /// <summary>
        /// Build the car and stand it on the grid. <see cref="VehicleController.BuildCar"/>
        /// creates the chassis at the world origin with its physics body already assembled,
        /// so both the transform and the rigid body are moved — the body because physics
        /// answers to it and not to the transform, the transform because that is what the
        /// camera and the flip test read on the frame the car appears.
        /// </summary>
        private static VehicleController PlaceCarOnGrid(TrackBuilder track)
        {
            GameObject chassis = VehicleController.BuildCar(null);
            Pose spawn = track.Spawn;

            chassis.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

            VehicleController car = chassis.GetComponent<VehicleController>();
            Rigidbody body = car.PhysicsBody;
            if (body != null)
            {
                body.position = spawn.position;
                body.rotation = spawn.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            // Start() arms the same pose one frame later; arming here means a reset pressed
            // in that first window already has somewhere to go.
            car.ArmSpawnPoint();
            return car;
        }

        /// <summary>
        /// Point the chase camera at the car — creating one, and a light to see it by, if
        /// the scene has neither. The starting pose is the offset the rig is aiming for:
        /// <see cref="ChaseCamera"/> smooths towards its target, so a camera left at the
        /// origin spends its first half-second flying across the scene instead of driving.
        /// </summary>
        private static void AimCameraAt(VehicleController car)
        {
            Transform target = car.transform;
            Camera camera = Camera.main;

            if (camera == null)
            {
                var cameraObject = new GameObject(CameraName);
                cameraObject.tag = MainCameraTag; // Camera.main only finds cameras by tag.
                camera = cameraObject.AddComponent<Camera>();

                Vector3 behind = target.position - target.forward * CameraBehind
                                 + Vector3.up * CameraHeight;
                cameraObject.transform.position = behind;
                cameraObject.transform.rotation = Quaternion.LookRotation(
                    target.position + Vector3.up * CameraAimHeight - behind, Vector3.up);
            }

            ChaseCamera chase = camera.GetComponent<ChaseCamera>();
            if (chase == null)
            {
                chase = camera.gameObject.AddComponent<ChaseCamera>();
            }

            chase.target = target;

            // Seated driver's view is the default; C toggles back to the chase camera.
            CockpitView cockpit = camera.GetComponent<CockpitView>();
            if (cockpit == null)
            {
                cockpit = camera.gameObject.AddComponent<CockpitView>();
            }

            cockpit.Bind(car, chase);
        }

        /// <summary>
        /// One directional light, unless the scene already has one. Built-in or URP makes no
        /// difference here: a sun-less scene under a skybox renders as flat silhouette
        /// colour, which makes a track nobody can read the corners in.
        /// </summary>
        private static void EnsureDirectionalLight()
        {
            if (Object.FindAnyObjectByType<Light>() != null)
            {
                return;
            }

            var lightObject = new GameObject(LightName);
            lightObject.transform.rotation = Quaternion.Euler(SunPitch, SunYaw, 0f);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = SunIntensity;
        }
    }
}
