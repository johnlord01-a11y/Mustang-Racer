using System.Collections;
using NUnit.Framework;
using OpenWheelRacer;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    public sealed class CockpitViewPlayTests
    {
        private GameObject _plane, _car, _cam;

        [TearDown]
        public void TearDown()
        {
            if (_car != null) Object.Destroy(_car);
            if (_cam != null) Object.Destroy(_cam);
            if (_plane != null) Object.Destroy(_plane);
        }

        private CockpitView Build(out VehicleController car, out ChaseCamera chase)
        {
            _plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _plane.transform.localScale = new Vector3(30f, 1f, 30f);
            _car = VehicleController.BuildCar(null);
            car = _car.GetComponent<VehicleController>();
            car.PhysicsBody.position = new Vector3(0f, 0.6f, 0f);
            _car.transform.position = new Vector3(0f, 0.6f, 0f);
            car.SetTestControls(0f, 0f, 0f);
            _cam = new GameObject("TestCam");
            _cam.AddComponent<Camera>();
            chase = _cam.AddComponent<ChaseCamera>();
            chase.target = _car.transform;
            CockpitView view = _cam.AddComponent<CockpitView>();
            view.Bind(car, chase);
            return view;
        }

        [UnityTest]
        public IEnumerator Default_IsCockpit_RigOnPlayer_NoColliders_ToggleRestoresChase()
        {
            CockpitView view = Build(out VehicleController car, out ChaseCamera chase);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsTrue(view.CockpitActive, "seated view must be the default");
            Assert.IsFalse(chase.enabled, "chase camera must be off in the cockpit");
            Assert.IsNotNull(view.Rig); Assert.IsTrue(view.Rig.gameObject.activeSelf);
            Assert.AreEqual(_car.transform, view.Rig.parent, "rig belongs to the player car");
            Assert.AreEqual(0, view.Rig.GetComponentsInChildren<Collider>(true).Length, "cockpit pieces must not collide");
            Assert.Greater(view.Rig.GetComponentsInChildren<Renderer>(true).Length, 5);
            Assert.That(_cam.GetComponent<Camera>().nearClipPlane, Is.EqualTo(CockpitView.NearClip).Within(1e-4f));
            Vector3 eye = _car.transform.TransformPoint(CockpitView.EyeLocal);
            Assert.Less(Vector3.Distance(_cam.transform.position, eye), 0.05f, "camera sits at the driver's eye");

            view.Toggle();
            Assert.IsFalse(view.CockpitActive);
            Assert.IsTrue(chase.enabled, "C returns to the chase camera");
            Assert.IsFalse(view.Rig.gameObject.activeSelf);
            view.Toggle();
            Assert.IsTrue(view.CockpitActive);
        }

        [UnityTest]
        public IEnumerator SteeringWheel_FollowsActualSteer()
        {
            CockpitView view = Build(out VehicleController car, out ChaseCamera chase);
            car.SetTestControls(0f, 0f, 1f);
            for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
            yield return null;
            float steer = car.CurrentSteerAngle;
            Assert.Greater(Mathf.Abs(steer), 1f, "car should be steering");
            float z = Mathf.DeltaAngle(0f, view.SteeringWheel.localEulerAngles.z);
            Debug.Log($"[Cockpit] steer {steer:F2} deg -> wheel z {z:F1} deg");
            Assert.That(z, Is.EqualTo(-CockpitView.WheelAngleFor(steer)).Within(2f));
        }

        [Test]
        public void VirtualGear_IsDerivedFromSpeed()
        {
            Assert.AreEqual(0, CockpitView.VirtualGear(0f, false));
            Assert.AreEqual(-1, CockpitView.VirtualGear(-2f, true));
            Assert.AreEqual(1, CockpitView.VirtualGear(10f, false));
            Assert.AreEqual(2, CockpitView.VirtualGear(20f, false));
            Assert.AreEqual(8, CockpitView.VirtualGear(90f, false));
            Assert.AreEqual("R", CockpitView.GearLabel(-1));
            Assert.AreEqual("N", CockpitView.GearLabel(0));
        }
    }
}
