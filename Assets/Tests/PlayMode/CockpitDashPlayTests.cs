using System.Collections;
using NUnit.Framework;
using OpenWheelRacer;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// <summary>
    /// Round 3 removed the projected speed/gear readout from the cockpit
    /// entirely — the simplest acceptable outcome for the brief is NO readout
    /// (the projected "N 0" text read as a plate pasted below the wheel).
    /// These tests pin that removal, and check the centre screen it replaced:
    /// a plain tilted touchscreen panel in the body's Interior, always under
    /// the cabin silhouette line and facing the driver, with no text of any
    /// kind anchored anywhere in the rig.
    /// </summary>
    public sealed class CockpitDashPlayTests
    {
        private GameObject _plane, _car, _cam;

        [TearDown]
        public void TearDown()
        {
            if (_car != null) Object.Destroy(_car);
            if (_cam != null) Object.Destroy(_cam);
            if (_plane != null) Object.Destroy(_plane);
        }

        private CockpitView Build()
        {
            _plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _plane.transform.localScale = new Vector3(30f, 1f, 30f);
            _car = VehicleController.BuildCar(null);
            VehicleController car = _car.GetComponent<VehicleController>();
            car.PhysicsBody.position = new Vector3(0f, 0.6f, 0f);
            _car.transform.position = new Vector3(0f, 0.6f, 0f);
            car.SetTestControls(0f, 0f, 0f);
            _cam = new GameObject("TestCam");
            _cam.AddComponent<Camera>();
            ChaseCamera chase = _cam.AddComponent<ChaseCamera>();
            chase.target = _car.transform;
            CockpitView view = _cam.AddComponent<CockpitView>();
            view.Bind(car, chase);
            return view;
        }

        [UnityTest]
        public IEnumerator Cockpit_HasNoReadoutAnchorAnywhere()
        {
            CockpitView view = Build();
            yield return null;
            yield return null;

            Assert.IsNotNull(view.Rig, "cockpit rig missing");
            Transform[] all = _car.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
                Assert.IsFalse(t.name.Contains("Readout"),
                    $"round 3 bans the dash readout; found anchor '{t.name}'");
        }

        [UnityTest]
        public IEnumerator CentreScreen_IsFlushWellUnderTheSilhouetteLine()
        {
            Build();
            yield return null;

            Transform screen = Find(_car.transform, "TouchScreen");
            Transform ui = Find(_car.transform, "TouchUI");
            Assert.IsNotNull(screen, "body must keep a centre touchscreen");
            Assert.IsNotNull(ui, "touchscreen must keep its lit UI panel");

            // Round-3b contract: the screen is a FLUSH recessed well — the
            // bezel plate lies level on the dash cap (tilting it buries the
            // panel under the cap front edge at this eye height), and the
            // lit UI plate floats proud of the bezel so the black frame
            // rings it from the driver's grazing angle.
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0f, screen.localEulerAngles.x)), 2f,
                "touchscreen bezel must lie flush with the dash cap");
            Bounds sb = screen.GetComponent<Renderer>().bounds;
            Bounds ub = ui.GetComponent<Renderer>().bounds;
            Assert.Greater(ub.max.y, sb.max.y, "UI plate must float proud of its bezel");
            Assert.LessOrEqual(Mathf.Abs(ub.center.x - sb.center.x), 0.005f,
                "UI plate must sit inside its bezel");
            Assert.LessOrEqual(Mathf.Abs(ub.center.z - sb.center.z), 0.005f,
                "UI plate must sit inside its bezel");

            // Never pokes above the cabin deck the chase cameras silhouette.
            float top = 0f;
            foreach (Renderer r in ui.GetComponentsInChildren<Renderer>(true))
                foreach (Vector3 c in Corners(r.bounds))
                    top = Mathf.Max(top, c.y);
            Assert.Less(top, 0.641f, "screen tops must stay under the deck line");

            // Shifter stack: knob + boot exist and stay under the same deck.
            Transform knob = Find(_car.transform, "ShifterKnob");
            Assert.IsNotNull(knob, "cabin must keep a shifter knob");
            foreach (Renderer r in knob.GetComponentsInChildren<Renderer>(true))
                foreach (Vector3 c in Corners(r.bounds))
                    top = Mathf.Max(top, c.y);
            Assert.Less(top, 0.641f, "shifter knob must stay under the deck line");
            Debug.Log($"[Cockpit] deck check: TouchUI/ShifterKnob top y {top:F4}");
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static Vector3[] Corners(Bounds b)
        {
            return new[]
            {
                new Vector3(b.min.x, b.min.y, b.min.z),
                new Vector3(b.max.x, b.min.y, b.min.z),
                new Vector3(b.min.x, b.max.y, b.min.z),
                new Vector3(b.max.x, b.max.y, b.min.z),
                new Vector3(b.min.x, b.min.y, b.max.z),
                new Vector3(b.max.x, b.min.y, b.max.z),
                new Vector3(b.min.x, b.max.y, b.max.z),
                b.max,
            };
        }
    }
}
