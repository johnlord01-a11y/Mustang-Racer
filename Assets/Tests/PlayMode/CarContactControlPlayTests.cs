using System;
using System.Collections;
using NUnit.Framework;
using OpenWheelRacer;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// Controlled response experiment: one variable at a time, normal FixedUpdate, flat plane.
    /// Mover is launched at 20 m/s straight into the rear of a target at rest.
    public sealed class CarContactControlPlayTests
    {
        private GameObject _plane, _mover, _target;

        [TearDown]
        public void TearDown()
        {
            if (_mover != null) Object.Destroy(_mover);
            if (_target != null) Object.Destroy(_target);
            if (_plane != null) Object.Destroy(_plane);
        }

        private static GameObject PlainBox()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = new Vector3(1.7f, 0.6f, 3.7f);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 700f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            return go;
        }

        private static void Place(GameObject go, Vector3 p)
        {
            Rigidbody rb = go.GetComponent<Rigidbody>();
            go.transform.SetPositionAndRotation(p, Quaternion.identity);
            rb.position = p; rb.rotation = Quaternion.identity;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        private static void FreeWheels(GameObject car, float frictionStiffness)
        {
            var vc = car.GetComponent<VehicleController>();
            if (vc != null) vc.enabled = false;
            foreach (WheelCollider w in car.GetComponentsInChildren<WheelCollider>())
            {
                w.brakeTorque = 0f; w.motorTorque = 0f;
                if (frictionStiffness >= 0f)
                {
                    WheelFrictionCurve f = w.forwardFriction; f.stiffness = frictionStiffness; w.forwardFriction = f;
                    WheelFrictionCurve s = w.sidewaysFriction; s.stiffness = frictionStiffness; w.sidewaysFriction = s;
                }
            }
        }
        private IEnumerator Run(string label, bool moverIsCar, bool targetIsCar, Action<GameObject> configureTarget)
        {
            _plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _plane.transform.localScale = new Vector3(30f, 1f, 30f);
            _target = targetIsCar ? VehicleController.BuildCar(null, false) : PlainBox();
            _mover = moverIsCar ? VehicleController.BuildCar(null, false) : PlainBox();
            Rigidbody tb = _target.GetComponent<Rigidbody>(), mb = _mover.GetComponent<Rigidbody>();
            if (targetIsCar) _target.GetComponent<VehicleController>().SetTestControls(0f, 0f, 0f);
            if (moverIsCar) _mover.GetComponent<VehicleController>().SetTestControls(0f, 0f, 0f);
            Place(_target, new Vector3(0f, targetIsCar ? 0.6f : 0.3f, 0f));
            Place(_mover, new Vector3(0f, moverIsCar ? 0.6f : 0.3f, -20f));
            for (int i = 0; i < 75; i++) yield return new WaitForFixedUpdate();
            configureTarget?.Invoke(_target);
            yield return new WaitForFixedUpdate();

            Vector3 tStart = tb.position;
            float tMass = tb.mass, mMass = mb.mass;
            mb.linearVelocity = Vector3.forward * 20f;
            if (moverIsCar) _mover.GetComponent<VehicleController>().SetTestControls(0.3f, 0f, 0f); // same launch as CarContactPlayTests (reached 21.6 m/s)
            float vBefore = 0f, mAfter = 0f, tPeak = 0f; int contact = -1;
            for (int i = 0; i < 150; i++)
            {
                float mvPrev = mb.linearVelocity.z;
                yield return new WaitForFixedUpdate();
                float gap = (tb.position.z - mb.position.z);
                if (contact < 0 && tb.linearVelocity.z > 0.05f) { contact = i; vBefore = mvPrev; }
                if (contact >= 0 && i <= contact + 10)
                {
                    tPeak = Mathf.Max(tPeak, tb.linearVelocity.z);
                    if (i == contact + 2) mAfter = mb.linearVelocity.z;
                }
            }
            float pBefore = mMass * vBefore, pAfter = mMass * mAfter + tMass * tPeak;
            Debug.Log($"[Ctl] {label}: contactStep={contact} moverBefore={vBefore:F2} moverAfter={mAfter:F2} targetPeak={tPeak:F2} " +
                      $"momentumRatio={(pBefore > 0f ? pAfter / pBefore : 0f):F2} targetMoved={(tb.position - tStart).magnitude:F2} moverEndZ={mb.position.z:F2} moverEndV={mb.linearVelocity} targetEndZ={tb.position.z:F2} dt={Time.fixedDeltaTime:F4} timeScale={Time.timeScale:F1} " +
                      $"targetMass={tMass:F0} moverMass={mMass:F0} targetCoM={tb.centerOfMass} targetY={tStart.y:F2} solverIter={tb.solverIterations}");
            if (moverIsCar && contact >= 0 && vBefore < 12f) Assert.Inconclusive($"{label}: impact only {vBefore:F2} m/s, fixture invalid");
            Assert.GreaterOrEqual(contact, 0, $"{label}: target never started moving");
            // Ctl4 (default car-into-car): same 15%-of-impact-speed rule as CarContactPlayTests.
            if (moverIsCar && targetIsCar && configureTarget == null)
                Assert.Greater(tPeak, 0.15f * vBefore, $"{label}: free-rolling target only reached {tPeak:F2} m/s from a {vBefore:F2} m/s hit");
        }

        [UnityTest] public IEnumerator Ctl1_BoxIntoBox() { yield return Run("box->box", false, false, null); }
        [UnityTest] public IEnumerator Ctl2_CarIntoBox() { yield return Run("car->box", true, false, null); }
        [UnityTest, Ignore("Invalid fixture, deferred with the contact known issue: the sliding plain-box mover loses most of its launch speed to ground friction (3.5 m/s at contact in Ctl1) and does not reach a valid impact speed against the car.")] public IEnumerator Ctl3_BoxIntoCar_Default() { yield return Run("box->car default", false, true, null); }
        [UnityTest] public IEnumerator Ctl4_CarIntoCar_Default() { yield return Run("car->car default", true, true, null); }
        [UnityTest] public IEnumerator Ctl5_CarIntoCar_TargetNoDrag() { yield return Run("car->car target controller off, brake 0", true, true, t => FreeWheels(t, -1f)); }
        [UnityTest] public IEnumerator Ctl6_CarIntoCar_TargetNoFriction() { yield return Run("car->car target brake 0, wheel friction 0.01", true, true, t => FreeWheels(t, 0.01f)); }
        [UnityTest] public IEnumerator Ctl8_CarIntoCar_TargetTinyMotorTorque()
        {
            yield return Run("car->car target brake 0, motorTorque 1e-4", true, true, t =>
            {
                FreeWheels(t, -1f);
                foreach (WheelCollider w in t.GetComponentsInChildren<WheelCollider>()) w.motorTorque = 0.0001f;
            });
        }
        [UnityTest] public IEnumerator Ctl7_CarIntoCar_TargetWheelsDisabled()
        {
            yield return Run("car->car target wheelcolliders disabled", true, true, t =>
            {
                FreeWheels(t, -1f);
                foreach (WheelCollider w in t.GetComponentsInChildren<WheelCollider>()) w.enabled = false;
            });
        }
    }
}
