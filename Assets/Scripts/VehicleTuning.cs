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

        [Tooltip("Spring stiffness. Unity suspension springs are normalized by the " +
                  "rigidbody mass, so this is acceleration per metre of travel error. " +
                  "Equilibrium compression = SuspensionDistance - g / SuspensionSpring.")]
        [Range(5f, 120f)] public float SuspensionSpring = 40f;
        [Range(0.5f, 40f)] public float SuspensionDamper = 6.5f;
        [Tooltip("Suspension position at rest (1 = fully extended, 0 = bump stop).")]
        [Range(0.05f, 1f)] public float SuspensionDistance = 0.7f;

        [Header("Engine (constant-force + power-curve roll-off)")]
        [Tooltip("Peak drive force per driven wheel at low speed (N).")]
        [Min(0f)] public float DriveForcePerWheel = 4200f;

        [Tooltip("Power-band ceiling: force fades toward zero at this vehicle speed (m/s).")]
        [Min(5f)] public float TopSpeedMs = 62f;

        [Tooltip("Engine braking torque applied when throttle is closed (Nm).")]
        [Min(0f)] public float EngineBrakeTorque = 90f;

        [Tooltip("Brake torque per wheel (Nm). ~3500 Nm on 0.33 m wheels locks a 700 kg car easily.")]
        [Min(0f)] public float BrakeTorquePerWheel = 3200f;

        [Header("Steering")]
        [Tooltip("Max steer angle at low speed (deg).")]
        [Range(1f, 35f)] public float MaxSteerAngle = 22f;

        [Tooltip("Steer angle (deg) when fully steering at/above the high-speed threshold.")]
        [Range(1f, 35f)] public float HighSpeedSteerAngle = 7f;

        [Tooltip("Speed (m/s) where steer has fully reduced to HighSpeedSteerAngle.")]
        [Min(5f)] public float HighSpeedSteerThreshold = 40f;

        [Tooltip("How quickly steer angle slews toward the target (deg/s).")]
        [Min(10f)] public float SteerRate = 260f;

        [Header("Grip & Downforce")]
        [Tooltip("Downforce (N) at DownforceRefSpeed; pulls the car onto the road.")]
        [Min(0f)] public float Downforce = 500f;
        [Min(1f)] public float DownforceRefSpeed = 30f;

        [Tooltip("Extra sideways grip multiplier from the WheelColliders themselves.")]
        [Range(0.5f, 3f)] public float LateralGripMultiplier = 1.25f;
        [Range(0.5f, 3f)] public float LongitudinalGripMultiplier = 1.1f;
    }
}
