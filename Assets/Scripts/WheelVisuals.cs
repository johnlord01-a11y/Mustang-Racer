using UnityEngine;

namespace OpenWheelRacer
{
    /// <summary>
    /// Visual-only wheel object: copies the simulated pose of a WheelCollider
    /// (GetWorldPose) onto its own transform each LateUpdate.
    ///
    /// Lives on a dedicated FX child (e.g. "Wheel_FL_Vis"), NEVER on the
    /// WheelCollider anchor itself — moving a WheelCollider's transform from
    /// its own pose feeds physics back into itself and destabilizes suspension.
    ///
    /// The cylinder mesh is a child rotated 90° so the primitive's Y axis
    /// lies along the wheel axle (X of the wheel pose).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WheelVisuals : MonoBehaviour
    {
        [SerializeField] private WheelCollider source;

        public void Bind(WheelCollider wheelCollider)
        {
            source = wheelCollider;
        }

        private void LateUpdate()
        {
            if (source == null) return;

            source.GetWorldPose(out Vector3 position, out Quaternion rotation);
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
