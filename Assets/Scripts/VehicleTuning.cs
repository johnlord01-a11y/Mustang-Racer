using UnityEngine;

namespace OpenWheelRacer
{
    /// <summary>
    /// All drivability numbers in one ScriptableObject so tuning never requires
    /// touching code. Create via Assets > Create > OpenWheelRacer > Vehicle Tuning.
    /// Defaults model a ~700 kg open-wheel formula car per PLAN.md Stage 1.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenWheelRacer/Vehicle Tuning", fileName = "VehicleTuning_Default")]
    public sealed class VehicleTuning : ScriptableObject
    {
        [Header("Chassis")]
        [Tooltip("Total chassis mass in kg (wheels are not simulated masses here).")]
        [Min(50f)] public float ChassisMassKg = 700f;

        [Tooltip("Wheel hub (full-bump) offsets from chassis transform: FL, FR, RL, RR. " +
                  "Hub sits slightly below mid-height so the chassis floor rides ~0.25 m up " +
                  "with the suspension at rest.")]
        public Vector3 WheelFrontLeft = new Vector3(-0.75f, 0.25f, 1.20f);
        public Vector3 WheelFrontRight = new Vector3(0.75f, 0.25f, 1.20f);
        public Vector3 WheelRearLeft = new Vector3(-0.75f, 0.25f, -1.25f);
        public Vector3 WheelRearRight = new Vector3(0.75f, 0.25f, -1.25f);

        [Tooltip("Center of mass offset; low + slightly rearward for rotation stability.")]
        public Vector3 CenterOfMassOffset = new Vector3(0f, -0.15f, -0.10f);

        [Tooltip("Suspension travel (m) per wheel; tuned with the springs below.")]
        [Range(0.05f, 0.5f)] public float SuspensionTravel = 0.28f;

        [Tooltip("Wheels per driven axle (4 = AWD bias for Stage 1 playability).")]
        public bool AllWheelDrive = true;

        [Header("Wheels")]
        [Min(0.1f)] public float WheelRadius = 0.33f;
        [Min(0.05f)] public float RideHeight = 0.22f;

        [Tooltip("Spring stiffness in N/m. A WheelCollider works in newtons per metre of " +
                  "compression, not in the mass-normalised figure it is easy to read one as: this " +
                  "chassis carries 1613-1822 N per wheel at rest, so 40 N/m sank it 81-92% of the " +
                  "way to the bump stops and dragged the floor on the tarmac in a turn. 35 kN/m " +
                  "takes about 0.05 m of the 0.28 m of travel, which is the ride height the wheels " +
                  "are meant to sit at.")]
        [Range(5000f, 120000f)] public float SuspensionSpring = 35000f;

        [Tooltip("Suspension damping in N*s/m, tracking the spring's newtons rather than the " +
                  "mass-normalised coefficient it was read as before. 3500 against 35 kN/m is about " +
                  "0.7 of critical damping on a quarter car of this mass; the 6.5 the field used to " +
                  "hold damped nothing at all.")]
        [Range(500f, 10000f)] public float SuspensionDamper = 3500f;
        [Tooltip("Suspension position at rest (1 = fully extended, 0 = bump stop).")]
        [Range(0.05f, 1f)] public float SuspensionDistance = 0.7f;

        [Header("Engine (constant-force + power-curve roll-off)")]
        [Tooltip("Peak drive force per driven wheel at low speed (N). Four driven wheels on a " +
                  "700 kg chassis: this is what puts the wheels on the road off the line, so it " +
                  "is tractive-force-limited rather than a launch control number.")]
        [Min(0f)] public float DriveForcePerWheel = 2500f;

        [Tooltip("Power-band ceiling: force fades toward zero at this vehicle speed (m/s).")]
        [Min(5f)] public float TopSpeedMs = 62f;

        [Tooltip("Engine braking torque applied when throttle is closed (Nm).")]
        [Min(0f)] public float EngineBrakeTorque = 90f;

        [Tooltip("Brake torque per wheel (Nm). ~3500 Nm on 0.33 m wheels locks a 700 kg car easily.")]
        [Min(0f)] public float BrakeTorquePerWheel = 3200f;

        [Tooltip("Forward speed (m/s) below which the brake key drives the car backwards instead of "
                 + "slowing it. It has to be a speed rather than a key because one key does both jobs: "
                 + "held at speed the brake stops the car first, and only once the car has been stopped "
                 + "— or once it is parked nose-first against a barrier with nowhere left to go, which "
                 + "makes no speed at all — is there anything left for it to do but reverse.")]
        [Min(0f)] public float ReverseEngageSpeedMs = 1.0f;

        [Tooltip("Reverse drive force per driven wheel as a fraction of DriveForcePerWheel. Half is "
                 + "enough to walk the car off a wall and still too little to be a gear to run in "
                 + "against the traffic.")]
        [Range(0f, 1f)] public float ReverseForceScale = 0.5f;

        [Tooltip("Speed (m/s) the reverse force winds off to zero at, so the reverse is a recovery "
                 + "manoeuvre with a ceiling of its own rather than a fourth gear pointed the wrong way.")]
        [Min(1f)] public float ReverseTopSpeedMs = 8f;

        [Header("Steering")]
        [Tooltip("Max steer angle at low speed (deg).")]
        [Range(1f, 35f)] public float MaxSteerAngle = 20f;

        [Tooltip("Steer angle (deg) when fully steering at/above the high-speed threshold.")]
        [Range(1f, 35f)] public float HighSpeedSteerAngle = 4f;

        [Tooltip("Speed (m/s) where steer has fully reduced to HighSpeedSteerAngle.")]
        [Min(5f)] public float HighSpeedSteerThreshold = 30f;

        [Tooltip("How quickly steer angle slews toward MORE lock (deg/s). Deliberately slow: a " +
                  "keyboard is a step input, and a rack that arrives at full lock in one or two " +
                  "frames puts the nose round before the corner has been read.")]
        [Min(10f)] public float SteerRate = 80f;

        [Tooltip("How quickly the rack unwinds toward centre, or crosses to the other lock (deg/s). " +
                  "Faster than SteerRate, so releasing the key takes the lock off promptly.")]
        [Min(10f)] public float SteerReturnRate = 160f;

        [Header("Grip & Downforce")]
        [Tooltip("Downforce (N) at DownforceRefSpeed; pulls the car onto the road.")]
        [Min(0f)] public float Downforce = 500f;
        [Min(1f)] public float DownforceRefSpeed = 30f;

        [Tooltip("Front axle sideways grip multiplier from the WheelColliders themselves " +
                  "(rear axles use RearLateralGripMultiplier).")]
        [Range(0.5f, 3f)] public float LateralGripMultiplier = 1.25f;

        [Tooltip("Rear axle sideways grip multiplier. Held above the front figure so the rear " +
                  "bites harder than the nose: with identical grip on both axles nothing keeps the " +
                  "tail planted and any corner becomes a slide.")]
        [Range(0.5f, 3f)] public float RearLateralGripMultiplier = 1.6f;

        [Tooltip("What the rear sideways grip is scaled to while the handbrake is held, so locking " +
                  "the rear axle puts the rear end out instead of only slowing the car down.")]
        [Range(0.1f, 1f)] public float HandbrakeRearGripScale = 0.45f;

        [Tooltip("Longitudinal (drive/brake) grip multiplier on all four WheelColliders.")]
        [Range(0.5f, 3f)] public float LongitudinalGripMultiplier = 1.1f;
    }
}
