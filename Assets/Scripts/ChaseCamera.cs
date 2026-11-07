using UnityEngine;

namespace OpenWheelRacer
{
    /// <summary>
    /// Minimal chase camera so the Stage 1 Sandbox is actually watchable.
    /// Deliberately dumb: lerp to a rear offset, look slightly ahead of the car.
    /// Stage 2 replaces this with real camera work (FOV push, shake, track-side
    /// replay cams). Keep it under ~30 lines until then.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChaseCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0f, 3.0f, -6.0f);
        public float positionLerp = 5f;
        public float lookAhead = 4f;

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desired = target.TransformPoint(offset);
            transform.position = Vector3.Lerp(transform.position, desired, positionLerp * Time.deltaTime);

            Vector3 look = target.position + target.forward * lookAhead + Vector3.up * 0.4f;
            Vector3 delta = look - transform.position;
            if (delta.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(delta);
            }
        }
    }
}
