using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// A small dial that sets one number on a RaymarchQuad - which one is up
    /// to the subclass (SmoothingKnob, InflateKnob, RepeatKnob). Hold a hand's
    /// grip near it and twist the wrist around the dial's axis (the way you'd
    /// turn a real knob): the dial itself never moves, only its indicator
    /// turns, clockwise (as seen from the front) = more. The value is shown
    /// in a text above it.
    ///
    /// Orientation: this object's up axis is the dial's axis (point it at the
    /// viewer) and its forward axis is the indicator's "12 o'clock". Kept
    /// separate from Grabbable/HandGrabber (which only pick up a Grabbable)
    /// so a hand near the knob never also picks something up by accident.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public abstract class Knob : MonoBehaviour
    {
        [Serializable]
        public class Hand
        {
            public InputActionReference grip;
            public Transform controller;
        }

        [SerializeField] protected RaymarchQuad quad;
        [SerializeField] private Hand[] hands;

        [Tooltip("The turning part (disk + indicator). Rotated about its local Y.")]
        [SerializeField] private Transform spin;
        [SerializeField] private TextMesh label;
        [SerializeField] private float labelHeight = 0.12f;

        [Header("Range")]
        [SerializeField] private float minValue = 0f;
        [SerializeField] private float maxValue = 0.3f;
        [Tooltip("How far the indicator travels from min to max, centred on 12 o'clock.")]
        [SerializeField] private float sweepDegrees = 270f;
        [Tooltip("How close (meters) the hand must be to the knob for the grip to take it.")]
        [SerializeField] private float grabRadius = 0.12f;

        private Collider col;
        private int holder = -1;
        private Quaternion prevRotation;
        private float angle; // 0..sweepDegrees
        private Action<InputAction.CallbackContext>[] performed;
        private Action<InputAction.CallbackContext>[] canceled;

        /// <summary>The number this dial sets, between Min Value and Max Value.</summary>
        protected abstract float Value { get; set; }

        /// <summary>What the text above the dial says.</summary>
        protected abstract string Caption { get; }

        private void Awake() => col = GetComponent<Collider>();

        private void OnEnable()
        {
            if (hands == null) return;
            performed = new Action<InputAction.CallbackContext>[hands.Length];
            canceled = new Action<InputAction.CallbackContext>[hands.Length];
            for (int i = 0; i < hands.Length; i++)
            {
                if (hands[i].grip == null) continue;
                int index = i;
                performed[i] = _ => OnGrip(index);
                canceled[i] = _ => OnLetGo(index);
                hands[i].grip.action.Enable();
                hands[i].grip.action.performed += performed[i];
                hands[i].grip.action.canceled += canceled[i];
            }
        }

        protected virtual void OnDisable()
        {
            if (hands != null)
            {
                for (int i = 0; i < hands.Length && performed != null; i++)
                {
                    if (hands[i].grip == null || performed[i] == null) continue;
                    hands[i].grip.action.performed -= performed[i];
                    hands[i].grip.action.canceled -= canceled[i];
                }
            }
            holder = -1;
        }

        private void OnGrip(int index)
        {
            if (holder >= 0 || hands[index].controller == null) return;

            Vector3 hand = hands[index].controller.position;
            if (Vector3.Distance(hand, col.ClosestPoint(hand)) > grabRadius) return;

            holder = index;
            prevRotation = hands[index].controller.rotation;
        }

        private void OnLetGo(int index)
        {
            if (holder == index) holder = -1;
        }

        private void Update()
        {
            if (quad == null) return;

            if (holder >= 0)
            {
                Quaternion current = hands[holder].controller.rotation;
                float twist = TwistDegrees(current * Quaternion.Inverse(prevRotation), transform.up);
                prevRotation = current;

                angle = Mathf.Clamp(angle + twist, 0f, sweepDegrees);
                Value = Mathf.Lerp(minValue, maxValue, angle / sweepDegrees);
            }
            else
            {
                // Follow the value if something else changed it (Inspector, code).
                angle = Mathf.InverseLerp(minValue, maxValue, Value) * sweepDegrees;
            }

            if (spin != null) spin.localRotation = Quaternion.AngleAxis(angle - sweepDegrees * 0.5f, Vector3.up);
            if (label != null) label.text = Caption;
        }

        private void LateUpdate()
        {
            if (label == null) return;

            // Hover above the knob, readable from wherever the viewer is.
            label.transform.position = transform.position + Vector3.up * labelHeight;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 away = label.transform.position - cam.transform.position;
                if (away.sqrMagnitude > 1e-6f) label.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }

        /// <summary>The part of rotation q that is a turn about the given world axis, in degrees (-180..180).</summary>
        private static float TwistDegrees(Quaternion q, Vector3 axis)
        {
            if (q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            float along = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            return 2f * Mathf.Atan2(along, q.w) * Mathf.Rad2Deg;
        }
    }
}
