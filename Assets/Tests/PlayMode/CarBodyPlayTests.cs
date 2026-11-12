using NUnit.Framework;
using OpenWheelRacer;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// <summary>
    /// The muscle-car convertible body is graphics only. VehicleController.BuildCar
    /// grew a mesh body — lofted shell, power-bulged hood, flared fenders, open
    /// cabin with windshield frame, two seats and two roll hoops, tri-bar
    /// taillights and the black trunk panel — and the whole point of this test is
    /// that none of it reached the physics: the car still wears exactly one box
    /// collider and four wheel colliders, and the numbers below are the ones the
    /// car carried before there was any body to look at, recorded here so a future
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
        private const float ClearDistanceM = 4f;

        /// <summary>
        /// The driver sits behind the windshield with the top down and looks over
        /// the long hood at the road: a ray fired straight forward from the cockpit
        /// eye (<see cref="CockpitView.EyeLocal"/>) must miss every opaque body
        /// bound for the first 4 metres — only the windshield glass, excluded by
        /// name, is allowed in front of the eye.
        /// </summary>
        [Test]
        public void EyeBehindWindshield_ForwardFourMetresClearOfOpaqueBody()
        {
            GameObject car = VehicleController.BuildCar(null);
            try
            {
                Transform bodyRoot = car.transform.Find(VehicleController.BodyRootName);
                Assert.IsNotNull(bodyRoot, "the car has no visual body root");

                Transform glass = bodyRoot.Find(MustangMeshBuilder.GlassRootName);
                Assert.IsNotNull(glass, "no windshield group");
                Assert.IsNotNull(glass.Find("WindshieldGlass"), "no windshield glass");

                var sight = new Ray(
                    car.transform.TransformPoint(CockpitView.EyeLocal),
                    car.transform.forward);

                // Inactive renderers (the player's disabled stripe set) cannot
                // block anything; only live opaque pieces are checked.
                Renderer[] renderers = bodyRoot.GetComponentsInChildren<Renderer>(false);
                foreach (Renderer piece in renderers)
                {
                    // The one thing allowed in the sight line is the glass itself.
                    if (piece.name.StartsWith("Windshield")) continue;

                    bool hits = piece.bounds.IntersectRay(sight, out float distance);
                    Assert.IsFalse(hits && distance <= ClearDistanceM,
                        $"{piece.name} blocks the cockpit view: the sight line hits it at " +
                        $"{distance:F2} m (bounds centre {piece.bounds.center}, " +
                        $"size {piece.bounds.size})");
                }
            }
            finally
            {
                Object.DestroyImmediate(car);
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

                // The body is there and reads as a top-down muscle car.
                Transform carBody = car.transform.Find(VehicleController.BodyRootName);
                Assert.IsNotNull(carBody, "the car has no visual body root");
                Assert.GreaterOrEqual(carBody.childCount, 6,
                    "the body is missing its part groups");
                Assert.IsNotNull(carBody.Find("BodyShell"), "no lofted body shell");
                Assert.IsNotNull(carBody.Find("Hood"), "no hood");
                Assert.IsNotNull(carBody.Find("Paint/Spoiler"), "no ducktail spoiler");
                Assert.IsNotNull(carBody.Find("Paint/FuelDoor"), "no fuel door");
                Transform paint = carBody.Find("Paint");
                Assert.IsNotNull(paint.Find("FrontFender_L"), "no front fenders");
                Assert.IsNotNull(paint.Find("RearFender_R"), "no rear fenders");
                Assert.IsNotNull(carBody.Find("Trim/HoopBase_L"), "no left roll hoop");
                Assert.IsNotNull(carBody.Find("Trim/HoopTop_R"), "no right roll hoop");
                Assert.IsNotNull(carBody.Find("Interior/SeatBack_L"), "no left seat");
                Assert.IsNotNull(carBody.Find("Interior/SeatBack_R"), "no right seat");
                Transform lamps = carBody.Find(MustangMeshBuilder.LampsRootName);
                Assert.IsNotNull(lamps, "no lamps group");
                Assert.IsNotNull(lamps.Find("TrunkPanel"), "no black trunk panel");
                Assert.IsNotNull(lamps.Find("ThirdBrakeLight"), "no third brake light");
                foreach (string side in new[] { "_L", "_R" })
                {
                    // tri-bar taillights: three bars per corner
                    Assert.IsNotNull(lamps.Find("TaillightBarIn" + side), "no inner tail bar" + side);
                    Assert.IsNotNull(lamps.Find("TaillightBarMid" + side), "no mid tail bar" + side);
                    Assert.IsNotNull(lamps.Find("TaillightBarOut" + side), "no outboard tail bar" + side);
                    Assert.IsNotNull(lamps.Find("Marker" + side), "no corner marker" + side);
                    // exhaust tips are at the OUTER bumper corners, not a mid quad
                    Transform tip = lamps.Find("ExhaustTip" + side) ?? carBody.Find($"Trim/ExhaustTip{side}");
                    Assert.IsNotNull(tip, "no exhaust tip" + side);
                    Assert.Greater(Mathf.Abs(tip.localPosition.x), 0.7f,
                        "the exhaust tips crept back to the middle instead of the outer corners");
                }
                // round fuel door on the LEFT rear quarter (-X with the nose at +Z)
                Assert.Less(carBody.Find("Paint/FuelDoor").localPosition.x, 0f,
                    "the fuel door is not on the left rear quarter");
                // the player wears no stripes: the group exists and is switched off
                Transform stripes = carBody.Find(MustangMeshBuilder.StripeRootName);
                Assert.IsNotNull(stripes, "no stripe set built for the AI to borrow");
                Assert.IsFalse(stripes.gameObject.activeSelf,
                    "the gloss-white player car is wearing racing stripes");

                int renderers = carBody.GetComponentsInChildren<Renderer>(true).Length;
                Assert.GreaterOrEqual(renderers, 10, "body pieces must be visible");

                // ... and it is made of nothing physics has to know about.
                Collider[] bodyColliders = carBody.GetComponentsInChildren<Collider>(true);
                Assert.AreEqual(0, bodyColliders.Length,
                    "a body piece is wearing a collider");

                Collider[] worn = car.GetComponentsInChildren<Collider>(true);
                Assert.AreEqual(ExpectedColliderCount, worn.Length,
                    "the car wears more colliders than its box and four wheels");

                // Exactly four visual wheels, bound and collider-free.
                Assert.AreEqual(4, carBody.GetComponentsInChildren<WheelVisuals>(true).Length,
                    "the body does not carry its four visual wheels");
            }
            finally
            {
                Object.DestroyImmediate(car);
            }
        }
    }
}
