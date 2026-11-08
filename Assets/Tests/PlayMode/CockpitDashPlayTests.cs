using System.Collections;
using NUnit.Framework;
using OpenWheelRacer;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// <summary>The speed/gear display lives on the steering wheel, low and centred, not at the top of the screen.</summary>
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

        [UnityTest]
        public IEnumerator Dash_IsOnTheWheel_LowerCentreOfView()
        {
            _plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _plane.transform.localScale = new Vector3(30f, 1f, 30f);
            _car = VehicleController.BuildCar(null);
            VehicleController car = _car.GetComponent<VehicleController>();
            car.PhysicsBody.position = new Vector3(0f, 0.6f, 0f);
            _car.transform.position = new Vector3(0f, 0.6f, 0f);
            car.SetTestControls(0f, 0f, 0f);
            _cam = new GameObject("TestCam");
            Camera cam = _cam.AddComponent<Camera>();
            ChaseCamera chase = _cam.AddComponent<ChaseCamera>();
            chase.target = _car.transform;
            CockpitView view = _cam.AddComponent<CockpitView>();
            view.Bind(car, chase);
            yield return null;
            yield return null;

            Assert.IsNotNull(view.Dash, "no dash panel");
            Assert.AreSame(view.SteeringWheel, view.Dash.parent, "the dash must be mounted on the wheel so it turns with it");
            Assert.IsNull(view.Dash.GetComponent<Collider>(), "dash must not collide");

            Vector3 sp = cam.WorldToScreenPoint(view.Dash.position);
            Assert.Greater(sp.z, 0f, "dash is behind the camera");
            Assert.Less(Mathf.Abs(sp.x / cam.pixelWidth - 0.5f), 0.08f, "dash is not horizontally centred");
            Assert.Less(sp.y / cam.pixelHeight, 0.4f, "dash is not in the lower part of the view (screen y from bottom)");
            Assert.IsTrue(view.TryGetDashGuiPoint(out _), "dash readout has no screen anchor");

            view.SetCockpit(false);
            Assert.IsFalse(view.TryGetDashGuiPoint(out _), "dash readout must hide in chase view");
        }

        [Test]
        public void DashText_ShowsGearAndRealSpeed()
        {
            Assert.AreEqual("N  0", CockpitView.DashText(0, "N"));
            Assert.AreEqual("3  97", CockpitView.DashText(97, "3"));
        }
    }
}
