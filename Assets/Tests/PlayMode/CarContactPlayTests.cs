using System.Collections;
using NUnit.Framework;
using OpenWheelRacer;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// Two real cars from VehicleController.BuildCar (the target built exactly as AiField builds an
    /// opponent's physics: humanControlled false) on an isolated flat plane, so no track, grid slot or
    /// AiDriver can move either car. Both cars are placed transform + rigidbody + SyncTransforms, settled,
    /// and their poses asserted before the moving car is launched.
    public sealed class CarContactPlayTests
    {
        private GameObject _plane, _a, _b;

        private sealed class Hits : MonoBehaviour
        {
            public Rigidbody Other; public int Enter, Stay; public float MaxImpulse; public string First = "";
            private void OnCollisionEnter(Collision c) { if (c.rigidbody == Other) { Enter++; MaxImpulse = Mathf.Max(MaxImpulse, c.impulse.magnitude); if (First.Length == 0) { var p = c.GetContact(0); First = $"n={p.normal} pt={p.point} thisCol={p.thisCollider.GetType().Name}:{p.thisCollider.name} otherCol={p.otherCollider.GetType().Name}:{p.otherCollider.name} contacts={c.contactCount} impulse={c.impulse}"; } } }
            private void OnCollisionStay(Collision c) { if (c.rigidbody == Other) Stay++; }
        }

        [TearDown]
        public void TearDown()
        {
            if (_a != null) Object.Destroy(_a);
            if (_b != null) Object.Destroy(_b);
            if (_plane != null) Object.Destroy(_plane);
        }

        private static void Place(GameObject car, Vector3 pos, Quaternion rot)
        {
            Rigidbody rb = car.GetComponent<Rigidbody>();
            car.transform.SetPositionAndRotation(pos, rot);
            rb.position = pos; rb.rotation = rot;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        private static string Wheels(GameObject car)
        {
            var s = "";
            foreach (WheelCollider w in car.GetComponentsInChildren<WheelCollider>())
                s += $"{w.name}:b{w.brakeTorque:F0}/m{w.motorTorque:F0}/{(w.isGrounded ? "G" : "-")} ";
            return s;
        }

        // target parked at origin facing +Z; mover starts at moverPos facing moverYaw, launched at speed.
        private IEnumerator Run(string label, float speed, Vector3 moverPos, float moverYaw, bool targetHandbrake, CollisionDetectionMode? mode = null)
        {
            _plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _plane.transform.localScale = new Vector3(30f, 1f, 30f);

            _b = VehicleController.BuildCar(null, false); // target, built like an AI opponent
            _a = VehicleController.BuildCar(null, false); // mover, driven through SetTestControls only
            VehicleController target = _b.GetComponent<VehicleController>();
            VehicleController mover = _a.GetComponent<VehicleController>();
            Rigidbody tb = target.PhysicsBody, mb = mover.PhysicsBody;
            if (mode.HasValue) { tb.collisionDetectionMode = mode.Value; mb.collisionDetectionMode = mode.Value; }
            target.SetTestControls(0f, 0f, 0f, targetHandbrake);
            mover.SetTestControls(0f, 0f, 0f);

            Vector3 tStart = new Vector3(0f, 0.6f, 0f);
            Quaternion moverRot = Quaternion.Euler(0f, moverYaw, 0f);
            Place(_b, tStart, Quaternion.identity);
            Place(_a, moverPos + Vector3.up * 0.6f, moverRot);
            for (int i = 0; i < 75; i++) yield return new WaitForFixedUpdate(); // 1.5 s settle

            Vector3 tSettled = tb.position, mSettled = mb.position;
            Assert.Less(Vector3.Distance(new Vector3(tSettled.x, 0f, tSettled.z), Vector3.zero), 0.1f, $"{label}: target drifted while settling to {tSettled}");
            Assert.Less(Vector3.Distance(new Vector3(mSettled.x, 0f, mSettled.z), new Vector3(moverPos.x, 0f, moverPos.z)), 0.1f, $"{label}: mover drifted while settling to {mSettled}");
            Assert.Less(Vector3.Distance(tb.position, _b.transform.position), 0.01f, $"{label}: target rigidbody/transform disagree");

            BoxCollider mc = _a.GetComponent<BoxCollider>(), tc = _b.GetComponent<BoxCollider>();
            Hits hits = _a.AddComponent<Hits>(); hits.Other = tb;
            mb.linearVelocity = moverRot * Vector3.forward * speed;
            mover.SetTestControls(0.3f, 0f, 0f);
            Debug.Log($"[Car2] {label} start target={tSettled} mover={mSettled} speed={speed} handbrake={targetHandbrake} mode={tb.collisionDetectionMode} targetWheels {Wheels(_b)}");

            int firstContact = -1; float maxPen = 0f, endPen = 0f, tPeakV = 0f, mVBefore = speed, minCentre = float.PositiveInfinity;
            for (int i = 0; i < 100; i++)
            {
                yield return new WaitForFixedUpdate();
                minCentre = Mathf.Min(minCentre, Vector3.Distance(mb.position, tb.position));
                float pen = 0f;
                if (Physics.ComputePenetration(mc, _a.transform.position, _a.transform.rotation, tc, _b.transform.position, _b.transform.rotation, out _, out float d)) pen = d;
                if (firstContact < 0 && hits.Enter > 0)
                {
                    firstContact = i;
                    Debug.Log($"[Car2] {label} contact step {i} moverV={mb.linearVelocity.magnitude:F2} targetV={tb.linearVelocity.magnitude:F2} rel={_b.transform.InverseTransformPoint(mb.position)} targetWheels {Wheels(_b)}");
                }
                if (firstContact < 0) mVBefore = mb.linearVelocity.magnitude;
                if (firstContact >= 0 && i > firstContact + 5) maxPen = Mathf.Max(maxPen, pen);
                endPen = pen;
                tPeakV = Mathf.Max(tPeakV, tb.linearVelocity.magnitude);
            }
            Vector3 moved = tb.position - tSettled; moved.y = 0f;
            Debug.Log($"[Car2] {label} result enter={hits.Enter} stay={hits.Stay} impulse={hits.MaxImpulse:F0} minCentre={minCentre:F2} moverVBefore={mVBefore:F2} moverVEnd={mb.linearVelocity.magnitude:F2} firstContact[{hits.First}] targetPeakV={tPeakV:F2} targetMoved={moved.magnitude:F2} penAfter0.1s={maxPen:F3} penEnd={endPen:F3}");

            Assert.Greater(hits.Enter, 0, $"{label}: mover and target never produced a collision callback (closest centres {minCentre:F2} m)");
            Assert.Less(maxPen, 0.15f, $"{label}: chassis boxes stayed {maxPen:F2} m inside each other after contact");
            // Equal masses: momentum conservation gives the target ~speed/2 in a perfectly inelastic hit,
            // so a free-rolling target that gains < 15% of the closing speed is being held by something.
            if (!targetHandbrake)
                Assert.Greater(tPeakV, 0.15f * speed, $"{label}: free-rolling target only reached {tPeakV:F2} m/s from a {speed} m/s hit");
        }

        [UnityTest, Ignore("Known issue deferred by the user (Nov 7, 22:00 EST checkpoint): a parked car whose WheelColliders are enabled is held at rest when hit (CarContactControlPlayTests, PlayMode_ctl_20261107_210655: target 0.22 m/s from a 21.6 m/s hit; 11.26 m/s with motorTorque 1e-4, 10.04 m/s with wheels disabled). Response threshold investigation, not a pass-through test; assertions kept unchanged.")] public IEnumerator Diag_RearEnd_Speculative_20ms() { yield return Run("rear-spec", 20f, new Vector3(0f, 0f, -20f), 0f, false, CollisionDetectionMode.ContinuousSpeculative); }
        [UnityTest, Ignore("Known issue deferred by the user (Nov 7, 22:00 EST checkpoint): a parked car whose WheelColliders are enabled is held at rest when hit (CarContactControlPlayTests, PlayMode_ctl_20261107_210655: target 0.22 m/s from a 21.6 m/s hit; 11.26 m/s with motorTorque 1e-4, 10.04 m/s with wheels disabled). Response threshold investigation, not a pass-through test; assertions kept unchanged.")] public IEnumerator Diag_RearEnd_ContinuousDynamic_20ms() { yield return Run("rear-cdyn", 20f, new Vector3(0f, 0f, -20f), 0f, false, CollisionDetectionMode.ContinuousDynamic); }
        [UnityTest, Ignore("Known issue deferred by the user (Nov 7, 22:00 EST checkpoint): a parked car whose WheelColliders are enabled is held at rest when hit (CarContactControlPlayTests, PlayMode_ctl_20261107_210655: target 0.22 m/s from a 21.6 m/s hit; 11.26 m/s with motorTorque 1e-4, 10.04 m/s with wheels disabled). Response threshold investigation, not a pass-through test; assertions kept unchanged.")] public IEnumerator Diag_RearEnd_Discrete_20ms() { yield return Run("rear-disc", 20f, new Vector3(0f, 0f, -20f), 0f, false, CollisionDetectionMode.Discrete); }
        [UnityTest, Ignore("Known issue deferred by the user (Nov 7, 22:00 EST checkpoint): a parked car whose WheelColliders are enabled is held at rest when hit (CarContactControlPlayTests, PlayMode_ctl_20261107_210655: target 0.22 m/s from a 21.6 m/s hit; 11.26 m/s with motorTorque 1e-4, 10.04 m/s with wheels disabled). Response threshold investigation, not a pass-through test; assertions kept unchanged.")] public IEnumerator RearEnd_FreeRolling_20ms() { yield return Run("rear-free", 20f, new Vector3(0f, 0f, -20f), 0f, false); }
        [UnityTest] public IEnumerator RearEnd_Handbraked_20ms() { yield return Run("rear-hb", 20f, new Vector3(0f, 0f, -20f), 0f, true); }
        [UnityTest, Ignore("Known issue deferred by the user (Nov 7, 22:00 EST checkpoint): a parked car whose WheelColliders are enabled is held at rest when hit (CarContactControlPlayTests, PlayMode_ctl_20261107_210655: target 0.22 m/s from a 21.6 m/s hit; 11.26 m/s with motorTorque 1e-4, 10.04 m/s with wheels disabled). Response threshold investigation, not a pass-through test; assertions kept unchanged.")] public IEnumerator HeadOn_FreeRolling_15ms() { yield return Run("headon-free", 15f, new Vector3(0f, 0f, 20f), 180f, false); }
        [UnityTest, Ignore("Known issue deferred by the user (Nov 7, 22:00 EST checkpoint): a parked car whose WheelColliders are enabled is held at rest when hit (CarContactControlPlayTests, PlayMode_ctl_20261107_210655: target 0.22 m/s from a 21.6 m/s hit; 11.26 m/s with motorTorque 1e-4, 10.04 m/s with wheels disabled). Response threshold investigation, not a pass-through test; assertions kept unchanged.")] public IEnumerator Side_FreeRolling_12ms() { yield return Run("side-free", 12f, new Vector3(-15f, 0f, 0f), 90f, false); }
    }
}