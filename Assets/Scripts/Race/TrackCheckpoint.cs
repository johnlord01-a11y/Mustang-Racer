using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// One trigger volume laid across the circuit. It reports the rigid body that
    /// passed through it together with the direction it crossed in, which is the one
    /// piece of information <see cref="LapCounter"/> cannot infer from indices
    /// (see its <c>OnCheckpoint(int, bool)</c>).
    ///
    /// Why the latch: a car is one rigid body wearing several colliders — the chassis
    /// box plus four wheel colliders. Each of them generates its own
    /// <see cref="OnTriggerEnter"/> against this trigger, so an unguarded component
    /// would report a single crossing up to five times. A body is therefore remembered
    /// while it is inside and its further enters are dropped until it has properly
    /// left through <see cref="OnTriggerExit"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TrackCheckpoint : MonoBehaviour
    {
        /// <summary>
        /// Checkpoint index, index 0 being the start/finish line. Assigned by
        /// <see cref="TrackBuilder"/> and handed straight to
        /// <see cref="LapCounter.OnCheckpoint(int, bool)"/>.
        /// </summary>
        public int index;

        /// <summary>
        /// Fires once per entry of a rigid body. The bool is true when the body crossed
        /// in the circuit's driving direction (this trigger's forward axis) and false
        /// when it swept backwards over it.
        /// </summary>
        public event Action<TrackCheckpoint, bool> Crossed;

        /// <summary>
        /// The same crossings as <see cref="Crossed"/>, with the body that made one.
        ///
        /// The body is what a field of cars needs: this volume lies across the whole
        /// road, so every car that comes past reports a crossing here, and a subscriber
        /// that is timing somebody has to be able to tell which one it is being told
        /// about. Raised immediately after <see cref="Crossed"/> with the same two
        /// arguments, so a subscriber of the older event sees exactly the crossings it
        /// always saw and nothing about it changes.
        /// </summary>
        public event Action<TrackCheckpoint, bool, Rigidbody> CrossedBy;

        /// <summary>Rigid bodies currently breaking this trigger, one entry per body.</summary>
        private HashSet<Rigidbody> _inside;

        /// <summary>
        /// Direction test, split out from <see cref="OnTriggerEnter"/> so it can be
        /// asserted without physics: a crossing counts as forward when the body's
        /// velocity has a non-negative component along the checkpoint's forward axis.
        /// </summary>
        /// <param name="velocity">Velocity of the body that crossed.</param>
        /// <param name="checkpointForward">This checkpoint's forward axis (the driving direction).</param>
        /// <returns>
        /// True for a forward crossing. Exactly perpendicular — or a body at rest, whose
        /// velocity is zero — reads as forward, matching the documented rule
        /// <c>dot >= 0</c>; only an actual backwards sweep reads false. A non-finite
        /// velocity reads false, so a body that has gone NaN cannot score.
        /// </returns>
        public static bool IsForward(Vector3 velocity, Vector3 checkpointForward)
        {
            return Vector3.Dot(velocity, checkpointForward) >= 0f;
        }

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody body = other != null ? other.attachedRigidbody : null;
            if (body == null)
            {
                return; // Scenery, the ground, debris: no body, no crossing to judge.
            }

            if (_inside == null)
            {
                _inside = new HashSet<Rigidbody>();
            }

            // False when this body is already inside: the second, third... collider of
            // the same car entering is not another crossing.
            if (!_inside.Add(body))
            {
                return;
            }

            bool forward = IsForward(body.linearVelocity, transform.forward);

            // Both events, one after the other and from the same two readings, so the pair
            // can never disagree about a crossing. Which of them a subscriber listens to is
            // whether it cares who crossed: the session times one car and wants the body to
            // filter on, a lone-car replay does not.
            Crossed?.Invoke(this, forward);
            CrossedBy?.Invoke(this, forward, body);
        }

        private void OnTriggerExit(Collider other)
        {
            Rigidbody body = other != null ? other.attachedRigidbody : null;
            if (body == null)
            {
                return;
            }

            // Only forget the body once its last collider has left; repeated exits of a
            // body that was never latched are simply ignored.
            _inside?.Remove(body);
        }

        /// <summary>
        /// A teleported or despawned car never reports an exit, so the latch is cleared
        /// whenever this checkpoint is switched off — otherwise the next arrival of that
        /// same body would be silently swallowed.
        /// </summary>
        private void OnDisable()
        {
            _inside?.Clear();
        }
    }
}
