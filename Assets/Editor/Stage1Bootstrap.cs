#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenWheelRacer.EditorTools
{
    /// <summary>
    /// Stage 1 bootstrap + drivability referee. Run headless by passing the
    /// -owr-run-stage1 flag; self-exits 0 (PASS) or 2 (FAIL):
    ///   Unity -batchmode -nographics -projectPath &lt;project&gt; -owr-run-stage1
    ///
    /// Authors, in order:
    ///   1. Assets/Physics/Road.physicMaterial (1.6) + OffRoad.physicMaterial (0.5)
    ///   2. Assets/Settings/VehicleTuning_Default.asset (ScriptableObject)
    ///   3. Assets/Prefabs/Car_Primitive.prefab via BuildCar + ConfigureWheel
    ///      (single source of truth — the bootstrap never inlines wheel values)
    ///   4. Assets/Scenes/Sandbox.unity (ground, off-road patch, light, chase cam,
    ///      car prefab instance) + EditorBuildSettings with Sandbox enabled
    /// Then runs a deterministic physics referee in a throwaway scene: settle,
    /// straight-line accel, low-speed vs high-speed steering, braking, handbrake,
    /// reset — driven by VehicleController.SetTestControls + StepTestFrame with
    /// Physics.simulationMode = Manual, so no PlayMode or devices are required.
    /// </summary>
    public static class Stage1Bootstrap
    {
        private const string RequestFlag = "-owr-run-stage1";
        private const string SettingsFolder = "Assets/Settings";
        private const string PhysicsFolder = "Assets/Physics";
        private const string PrefabsFolder = "Assets/Prefabs";
        private const string TuningPath = SettingsFolder + "/VehicleTuning_Default.asset";
        private const string RoadMatPath = PhysicsFolder + "/Road.physicMaterial";
        private const string OffRoadMatPath = PhysicsFolder + "/OffRoad.physicMaterial";
        private const string PrefabPath = PrefabsFolder + "/Car_Primitive.prefab";
        private const string SandboxPath = "Assets/Scenes/Sandbox.unity";
        private const float Dt = 0.02f; // 50 Hz, matches default fixedDeltaTime
        private static readonly Vector3 CarSpawn = new Vector3(0f, 0.6f, 0f);

        [InitializeOnLoadMethod]
        private static void RunIfRequested()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg == RequestFlag)
                {
                    EditorApplication.delayCall += ExecuteAndExit;
                    return;
                }
            }
        }

        private static void ExecuteAndExit()
        {
            int failures;
            try
            {
                failures = Run();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Stage1] UNHANDLED: {e}");
                EditorApplication.Exit(2);
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(failures == 0
                ? "[Stage1] RESULT: PASS"
                : $"[Stage1] RESULT: FAIL ({failures} problem(s))");
            EditorApplication.Exit(failures == 0 ? 0 : 2);
        }

        public static int Run()
        {
            int failures = 0;
            void Fail(string msg) { failures++; Debug.LogError($"[Stage1] FAIL: {msg}"); }
            void Pass(string msg) { Debug.Log($"[Stage1] OK: {msg}"); }

            // 0. Folders.
            foreach (var folder in new[] { SettingsFolder, PhysicsFolder, PrefabsFolder })
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;
                int slash = folder.LastIndexOf('/');
                AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
                Pass($"folder created: {folder}");
            }

            // 1. PhysicsMaterials.
            PhysicsMaterial road = LoadOrCreate(RoadMatPath, () => new PhysicsMaterial("Road")
            { staticFriction = 1.6f, dynamicFriction = 1.4f });
            PhysicsMaterial offRoad = LoadOrCreate(OffRoadMatPath, () => new PhysicsMaterial("OffRoad")
            { staticFriction = 0.5f, dynamicFriction = 0.4f });
            if (road == null) Fail($"could not create/load {RoadMatPath}"); else Pass(RoadMatPath);
            if (offRoad == null) Fail($"could not create/load {OffRoadMatPath}"); else Pass(OffRoadMatPath);

            // 2. Tuning asset (persistent; prefab references it by GUID).
            VehicleTuning tuning = LoadOrCreate(TuningPath, () =>
            {
                var t = ScriptableObject.CreateInstance<VehicleTuning>();
                t.name = "VehicleTuning_Default";
                return t;
            });
            if (tuning == null)
            {
                Fail($"could not create/load {TuningPath}");
                return failures; // Everything below needs tuning.
            }
            Pass($"{TuningPath} (mass {tuning.ChassisMassKg} kg, {tuning.DriveForcePerWheel} N/wheel)");

            // 3. Prefab, built through the runtime code path so physics values
            //    come from ConfigureWheel — never duplicated here.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject carGO = VehicleController.BuildCar(tuning);
            carGO.transform.SetPositionAndRotation(CarSpawn, Quaternion.identity);
            AttachWheelVisuals(carGO);
            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(carGO, PrefabPath, out bool saved);
            if (!saved || prefabAsset == null)
            {
                Fail($"prefab save failed: {PrefabPath}");
            }
            else
            {
                Pass($"prefab authored: {PrefabPath}");
                GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                if (contents == null)
                {
                    Fail("LoadPrefabContents returned null");
                }
                else
                {
                    int controllers = contents.GetComponentsInChildren<VehicleController>(true).Length;
                    int wheels = contents.GetComponentsInChildren<WheelCollider>(true).Length;
                    int visuals = contents.GetComponentsInChildren<WheelVisuals>(true).Length;
                    PrefabUtility.UnloadPrefabContents(contents);
                    if (controllers != 1 || wheels != 4 || visuals != 4)
                    {
                        Fail($"prefab contents wrong: controllers={controllers} wheels={wheels} visuals={visuals}");
                    }
                    else
                    {
                        Pass("prefab integrity: 1 controller, 4 WheelColliders, 4 WheelVisuals");
                    }
                }
            }

            // 4. Sandbox scene.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateGround("Ground_Road", Vector3.zero, new Vector3(40f, 1f, 40f), road);
            CreateGround("Ground_OffRoad", new Vector3(440f, 0f, 0f), new Vector3(48f, 1f, 40f), offRoad);

            var lightGO = new GameObject("Directional Light");
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;

            var camGO = new GameObject("MainCamera");
            camGO.tag = "MainCamera";
            camGO.transform.position = CarSpawn + new Vector3(0f, 3f, -8f);
            camGO.AddComponent<Camera>();
            var chase = camGO.AddComponent<ChaseCamera>();

            GameObject sceneCar = prefabAsset != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset)
                : VehicleController.BuildCar(tuning);
            sceneCar.transform.SetPositionAndRotation(CarSpawn, Quaternion.identity);
            chase.target = sceneCar.transform;

            if (EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), SandboxPath))
            {
                Pass($"scene written: {SandboxPath}");
            }
            else
            {
                Fail($"failed to save scene {SandboxPath}");
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(SandboxPath, true),
                new EditorBuildSettingsScene("Assets/Scenes/Boot.unity", false),
                new EditorBuildSettingsScene("Assets/Scenes/Garage.unity", false),
                new EditorBuildSettingsScene("Assets/Scenes/Track_Proto.unity", false),
            };
            Pass("EditorBuildSettings: Sandbox enabled (first scene)");

            // 5. Drivability referee on a fresh throwaway scene.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateGround("Ground_Road", Vector3.zero, new Vector3(40f, 1f, 40f), road);
            GameObject refGO = VehicleController.BuildCar(tuning);
            refGO.transform.SetPositionAndRotation(CarSpawn, Quaternion.identity);
            VehicleController ctrl = refGO.GetComponent<VehicleController>();
            ctrl.ArmSpawnPoint();
            Debug.Log($"[Stage1] probe@build: body={(ctrl.PhysicsBody != null)} fl={(ctrl.FrontLeftWheel != null)} " +
                      $"fr={(ctrl.FrontRightWheel != null)} rl={(ctrl.RearLeftWheel != null)} rr={(ctrl.RearRightWheel != null)}");

            SimulationMode previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                void Step(int frames, float throttle, float brake, float steer, bool handbrake = false)
                {
                    for (int i = 0; i < frames; i++)
                    {
                        ctrl.SetTestControls(throttle, brake, steer, handbrake);
                        ctrl.StepTestFrame(Dt);
                    }
                }
                void PhaseReset()
                {
                    ctrl.ResetToSpawn();
                    // WheelCollider spin has no public setter: after a reset the
                    // wheels still spin at pre-teleport rpm and friction drags the
                    // car forward. Clamp with service brakes, then let it settle.
                    Step(15, 0f, 1f, 0f);
                    Step(15, 0f, 0f, 0f);
                }

                // A. Settle: hangs under gravity, all four wheels find the road.
                Step(200, 0f, 0f, 0f);
                // Pose checks read the Rigidbody, not the (interpolated) transform.
                float restY = ctrl.PhysicsBody.position.y;
                if (ctrl.GroundedWheelCount != 4)
                    Fail($"settle: grounded wheels = {ctrl.GroundedWheelCount}, want 4");
                else Pass("settle: all 4 wheels grounded");
                // Body-y envelope for this geometry: rest = wheelRadius - hubY + s*travel,
                // i.e. chassis floor rides ~0.25 m; range spans full droop..near bumpstop.
                if (restY < 0.10f || restY > 0.30f)
                    Fail($"settle: ride height y={restY:F3} outside 0.10..0.30");
                else Pass($"settle: ride height y={restY:F3}");
                if (ctrl.CurrentSpeedMs > 0.75f)
                    Fail($"settle: not settling, {ctrl.CurrentSpeedMs:F2} m/s after 4 s");
                else Pass($"settle: calm ({ctrl.CurrentSpeedMs:F2} m/s)");

                // B. Straight-line accel: throttle must actually move it.
                Step(180, 1f, 0f, 0f);
                float vAccel = ctrl.CurrentSpeedMs;
                if (vAccel < 10f) Fail($"accel: only {vAccel:F1} m/s after 3.6 s full throttle");
                else Pass($"accel: {vAccel:F1} m/s after 3.6 s full throttle");
                Debug.Log($"[Stage1] probe@postaccel: body={(ctrl.PhysicsBody != null)} fl={(ctrl.FrontLeftWheel != null)} " +
                          $"grounded={ctrl.GroundedWheelCount} inst={refGO.GetEntityId()}");

                // C. Low-speed steering: rack reaches full lock, front axle only.
                PhaseReset();
                Step(20, 0f, 0f, 1f);
                float aLow = ctrl.CurrentSteerAngle;
                if (aLow < 19f || aLow > 23f) Fail($"low-speed steer = {aLow:F1} deg, want ~22");
                else Pass($"low-speed steer = {aLow:F1} deg");
                if (Mathf.Abs(ctrl.RearLeftWheel.steerAngle) > 0.001f)
                    Fail($"rear axle steers: {ctrl.RearLeftWheel.steerAngle:F3} deg");
                else Pass("rear axle straight (front-wheel steering only)");
                if (Mathf.Abs(ctrl.FrontLeftWheel.steerAngle - aLow) > 0.5f)
                    Fail("FL wheel not tracking rack angle");
                else Pass("FL wheel tracks rack");

                // D. Speed sensitivity: steer must shrink to ~7 deg at 45 m/s.
                PhaseReset();
                ctrl.PhysicsBody.linearVelocity = Vector3.forward * 45f;
                Step(12, 0f, 0f, 1f);
                float aHigh = Mathf.Abs(ctrl.CurrentSteerAngle);
                if (aHigh < 5f || aHigh > 9.5f) Fail($"high-speed steer = {aHigh:F1} deg, want ~7");
                else Pass($"high-speed steer = {aHigh:F1} deg at 45 m/s (vs {aLow:F1} deg low-speed)");

                // E. Braking: full brake from 20 m/s must nearly stop it.
                PhaseReset();
                ctrl.PhysicsBody.linearVelocity = Vector3.forward * 20f;
                Step(150, 0f, 1f, 0f);
                if (ctrl.CurrentSpeedMs > 4f) Fail($"brake: still {ctrl.CurrentSpeedMs:F1} m/s after 3 s");
                else Pass($"brake: {ctrl.CurrentSpeedMs:F2} m/s after 3 s from 20 m/s");

                // F. Handbrake: rear-lock from 15 m/s must slow it hard.
                PhaseReset();
                ctrl.PhysicsBody.linearVelocity = Vector3.forward * 15f;
                Step(200, 0f, 0f, 0f, handbrake: true);
                if (ctrl.CurrentSpeedMs > 6f) Fail($"handbrake: still {ctrl.CurrentSpeedMs:F1} m/s after 4 s");
                else Pass($"handbrake: {ctrl.CurrentSpeedMs:F2} m/s after 4 s from 15 m/s");

                // G. Reset: teleport away, key-reset returns to exact spawn.
                ctrl.PhysicsBody.position += new Vector3(10f, 2f, 10f);
                ctrl.ResetToSpawn();
                float resetErr = Vector3.Distance(ctrl.PhysicsBody.position, CarSpawn);
                if (resetErr > 0.05f) Fail($"reset: {resetErr:F3} m off spawn");
                else Pass($"reset: back on spawn pose ({resetErr:F4} m)");

                // H. Upright sanity: none of the above flipped the chassis.
                float upY = (ctrl.PhysicsBody.rotation * Vector3.up).y;
                if (upY < 0.8f) Fail($"car ended upside down (up.y={upY:F2})");
                else Pass("car stayed upright through the whole run");
            }
            finally
            {
                Physics.simulationMode = previousMode;
            }

            // 6. Sandbox parses and contains its car.
            Scene sandbox = EditorSceneManager.OpenScene(SandboxPath, OpenSceneMode.Single);
            if (!sandbox.IsValid())
            {
                Fail("Sandbox.unity invalid after reopen");
            }
            else if (UnityEngine.Object.FindAnyObjectByType<VehicleController>() == null)
            {
                Fail("Sandbox has no VehicleController instance");
            }
            else if (UnityEngine.Object.FindAnyObjectByType<ChaseCamera>() == null)
            {
                Fail("Sandbox has no ChaseCamera");
            }
            else
            {
                Pass("Sandbox reopens with car instance + chase camera");
            }

            return failures;
        }

        // ---------------------------------------------------------------- utils

        private static T LoadOrCreate<T>(string path, Func<T> create) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            T asset = create();
            if (asset == null) return null;
            AssetDatabase.CreateAsset(asset, path);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static GameObject CreateGround(string name, Vector3 pos, Vector3 scale, PhysicsMaterial mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            var mc = go.GetComponent<MeshCollider>();
            if (mat != null) mc.material = mat;
            return go;
        }

        private static void AttachWheelVisuals(GameObject car)
        {
            foreach (WheelCollider wheel in car.GetComponentsInChildren<WheelCollider>(true))
            {
                var fx = new GameObject(wheel.name + "_Vis");
                fx.transform.SetParent(car.transform, worldPositionStays: false);
                wheel.GetWorldPose(out Vector3 posePos, out Quaternion poseRot);
                fx.transform.SetPositionAndRotation(posePos, poseRot);

                var visuals = fx.AddComponent<WheelVisuals>();
                visuals.Bind(wheel);

                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mesh.name = "Mesh";
                UnityEngine.Object.DestroyImmediate(mesh.GetComponent<Collider>());
                mesh.transform.SetParent(fx.transform, worldPositionStays: false);
                // Cylinder primitive: 2 units tall along Y, radius 0.5. Rotate its
                // Y axis onto the wheel axle (X), scale to wheel radius.
                mesh.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                mesh.transform.localPosition = Vector3.zero;
                float r = wheel.radius;
                mesh.transform.localScale = new Vector3(r * 2f, 0.12f, r * 2f);
            }
        }
    }
}
#endif
