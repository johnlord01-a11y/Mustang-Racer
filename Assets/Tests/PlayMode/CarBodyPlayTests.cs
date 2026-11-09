using NUnit.Framework;
using OpenWheelRacer;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// <summary>
    /// The placeholder F1 body is graphics only. <see cref="VehicleController.BuildCar"/>
    /// grew a body of primitives — tub, nose, halo, airbox, sidepods, engine cover,
    /// rear wing on two endplates, front wing — and the whole point of this test is
    /// that none of it reached the physics: the car still wears exactly one box
    /// collider and four wheel colliders, and the numbers below are the ones the car
    /// carried before there was any body to look at, recorded here so a future
    /// "improvement" that moves mass, centre of mass or the chassis box trips over
    /// them.
    /// </summary>
    [TestFixture]
    public sealed class CarBodyPlayTests
    {
        /// <summary>Chassis mass the car was built with before the body existed and
        /// must keep (<see cref="VehicleTuning.ChassisMassKg"/> default).</summary>
        private const float ExpectedMassKg = 700f;

        /// <summary>Chassis centre of mass offset, as recorded before the body existed.</summary>
        private static readonly Vector3 ExpectedCentreOfMass = new Vector3(0f, -0.15f, -0.10f);

        /// <summary>The chassis box collider as BuildCar has always cut it.</summary>
        private static readonly Vector3 ExpectedBoxCentre = new Vector3(0f, 0.35f, 0f);
        private static readonly Vector3 ExpectedBoxSize = new Vector3(1.7f, 0.6f, 3.7f);

        /// <summary>One chassis box plus four wheel colliders — the whole car as
        /// physics knows it, body or no body.</summary>
        private const int ExpectedColliderCount = 5;

        /// <summary>How far ahead of the cockpit eye the sight line must stay clear.</summary>
        private const float ClearDistanceM = 3f;

        /// <summary>
        /// The halo and the nose are groups of pieces, not single primitives — a hoop
        /// of thin dark rails around the driver with a centre pillar, and a stack of
        /// shrinking cubes running into the front wing — and neither may stand in the
        /// driver's view: a ray fired straight forward from the cockpit eye
        /// (<see cref="CockpitView.EyeLocal"/>) must miss every one of their bounds
        /// for the first 3 metres, and none of them may have crept into physics.
        /// </summary>
        [Test]
        public void HaloAndNose_Present_AndDoNotBlockCockpitView()
        {
            GameObject car = VehicleController.BuildCar(null);
            try
            {
                Transform bodyRoot = car.transform.Find(VehicleController.BodyRootName);
                Assert.IsNotNull(bodyRoot, "the car has no visual body root");

                Transform halo = bodyRoot.Find(VehicleController.HaloRootName);
                Transform nose = bodyRoot.Find("Nose");
                Assert.IsNotNull(halo, "the body has no Halo");
                Assert.IsNotNull(nose, "the body has no Nose");

                // Groups: the named object itself is an empty with pieces under it.
                Assert.IsNull(halo.GetComponent<Renderer>(),
                    "the halo is one primitive again instead of a group of pieces");
                Assert.IsNull(nose.GetComponent<Renderer>(),
                    "the nose is one primitive again instead of a group of pieces");
                Assert.GreaterOrEqual(halo.GetComponentsInChildren<Renderer>(true).Length, 3,
                    "the halo is not built from several pieces");
                Assert.GreaterOrEqual(nose.GetComponentsInChildren<Renderer>(true).Length, 3,
                    "the nose is not built from several pieces");

                // Visual only, collider destroyed the same way every body piece does.
                Assert.AreEqual(0, halo.GetComponentsInChildren<Collider>(true).Length,
                    "a halo piece is wearing a collider");
                Assert.AreEqual(0, nose.GetComponentsInChildren<Collider>(true).Length,
                    "a nose piece is wearing a collider");

                // The driver's straight-ahead view: fired from the eye the camera
                // actually sits at, along the car's nose. The car is built at the
                // origin with identity rotation, so this is the chassis-local ray.
                var sight = new Ray(
                    car.transform.TransformPoint(CockpitView.EyeLocal),
                    car.transform.forward);

                CheckGroupClearOfSight(halo, sight);
                CheckGroupClearOfSight(nose, sight);
            }
            finally
            {
                Object.DestroyImmediate(car);
            }
        }

        /// <summary>
        /// Fails if any renderer under <paramref name="group"/> stands across
        /// <paramref name="sight"/> within <see cref="ClearDistanceM"/>.
        /// </summary>
        private static void CheckGroupClearOfSight(Transform group, Ray sight)
        {
            Renderer[] renderers = group.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer piece in renderers)
            {
                bool hits = piece.bounds.IntersectRay(sight, out float distance);
                Assert.IsFalse(hits && distance <= ClearDistanceM,
                    $"{piece.name} blocks the cockpit view: the sight line hits it at " +
                    $"{distance:F2} m (bounds centre {piece.bounds.center}, " +
                    $"size {piece.bounds.size})");
            }
        }

        [Test]
        public void CarBody_VisualParts_HaveNoColliders_AndMassUnchanged()
        {
            GameObject car = VehicleController.BuildCar(null);
            try
            {
                Rigidbody body = car.GetComponent<Rigidbody>();

                // Physics exactly as recorded before any bodywork existed.
                Assert.AreEqual(ExpectedMassKg, body.mass, 1e-3f,
                    "the bodywork changed the mass");
                Assert.AreEqual(ExpectedCentreOfMass, body.centerOfMass,
                    "the bodywork moved the centre of mass");

                BoxCollider chassis = car.GetComponent<BoxCollider>();
                Assert.IsNotNull(chassis, "the chassis box must still be the physics body");
                Assert.AreEqual(ExpectedBoxCentre, chassis.center, "the chassis box moved");
                Assert.AreEqual(ExpectedBoxSize, chassis.size, "the chassis box changed");

                // The car was built at the origin with identity rotation, so its
                // recorded world bounds are its local box, centre and size alike.
                Bounds bounds = chassis.bounds;
                Assert.AreEqual(ExpectedBoxCentre.x, bounds.center.x, 1e-3f,
                    "the chassis bounds moved");
                Assert.AreEqual(ExpectedBoxCentre.y, bounds.center.y, 1e-3f,
                    "the chassis bounds moved");
                Assert.AreEqual(ExpectedBoxCentre.z, bounds.center.z, 1e-3f,
                    "the chassis bounds moved");
                Assert.AreEqual(ExpectedBoxSize.x, bounds.size.x, 1e-3f,
                    "the chassis bounds changed");
                Assert.AreEqual(ExpectedBoxSize.y, bounds.size.y, 1e-3f,
                    "the chassis bounds changed");
                Assert.AreEqual(ExpectedBoxSize.z, bounds.size.z, 1e-3f,
                    "the chassis bounds changed");

                // The body is there and reads as an open-wheel car.
                Transform carBody = car.transform.Find(VehicleController.BodyRootName);
                Assert.IsNotNull(carBody, "the car has no visual body root");
                Assert.GreaterOrEqual(carBody.childCount, 10,
                    "the body is missing pieces");
                Assert.IsNotNull(carBody.Find("Nose"), "no nose");
                Assert.IsNotNull(carBody.Find("Halo"), "no halo");
                Assert.IsNotNull(carBody.Find("Airbox"), "no airbox");
                Assert.IsNotNull(carBody.Find("RearWing"), "no rear wing");
                Assert.IsNotNull(carBody.Find("FrontWing"), "no front wing");

                int renderers = carBody.GetComponentsInChildren<Renderer>(true).Length;
                Assert.GreaterOrEqual(renderers, 10, "body pieces must be visible");

                // ... and it is made of nothing physics has to know about.
                Collider[] bodyColliders = carBody.GetComponentsInChildren<Collider>(true);
                Assert.AreEqual(0, bodyColliders.Length,
                    "a body piece is wearing a collider");

                Collider[] worn = car.GetComponentsInChildren<Collider>(true);
                Assert.AreEqual(ExpectedColliderCount, worn.Length,
                    "the car wears more colliders than its box and four wheels");
            }
            finally
            {
                Object.DestroyImmediate(car);
            }
        }
    }
}
