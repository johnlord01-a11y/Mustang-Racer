using System.Collections;
using NUnit.Framework;
using OpenWheelRacer;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// <summary>
    /// The speed/gear readout is anchored to the centre touchscreen (round 2):
    /// it projects onto the lit UI panel low-right of the driver's view and
    /// never rides the steering wheel, so it cannot spin away (round-1 review:
    /// "floating text stuck in mid-frame").
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

        [UnityTest]
        public IEnumerator Dash_ReadoutIsAnchoredToTheCentreScreen()
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

            Assert.IsNotNull(view.Dash, "no dash readout anchor");
            Assert.AreNotSame(view.SteeringWheel, view.Dash.parent,
                "the readout must NOT ride the wheel (it would spin with the steer)");
            Assert.IsNull(view.Dash.GetComponent<Collider>(), "anchor must not collide");
            Assert.IsNull(view.Dash.GetComponent<Renderer>(), "anchor is invisible; the body's TouchUI panel is the screen");
            Assert.Less(view.Dash.localPosition.y, 0.641f,
                "readout anchor must stay under the cabin silhouette (deck) line");

            Vector3 sp = cam.WorldToScreenPoint(view.Dash.position);
            Assert.Greater(sp.z, 0f, "readout is behind the camera");
            float xf = sp.x / cam.pixelWidth, yf = sp.y / cam.pixelHeight;
            Assert.Greater(xf, 0.55f, "readout is not right-of-centre over the centre stack");
            Assert.Less(xf, 0.80f, "readout has drifted off the right of the screen");
            Assert.Greater(yf, 0.08f, "readout is off the bottom edge");
            Assert.Less(yf, 0.35f, "readout is not in the lower part of the view (screen y from bottom)");
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
