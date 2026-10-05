using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The dial for a RaymarchQuad's repeat: wraps space around every so many
    /// meters (mod p), so the one skeleton shows up again and again across
    /// the floor. All the way down is off; turning it up brings the copies in
    /// from Far Spacing to Near Spacing. Goes back to off when it's switched
    /// off. See Knob for how it's turned.
    /// </summary>
    public class RepeatKnob : Knob
    {
        [Header("Repeat")]
        [Tooltip("Spacing (meters) just after the dial leaves 'off'.")]
        [SerializeField] private float farSpacing = 4f;
        [Tooltip("Spacing (meters) with the dial all the way up. Anything wider than this gets cut off at the edge of its cell.")]
        [SerializeField] private float nearSpacing = 1.2f;
        [Tooltip("Below this much of the dial's travel (0..1), repeating is off.")]
        [SerializeField] private float offBelow = 0.05f;

        private float amount; // the dial's own 0..1; the quad only knows the spacing

        protected override float Value
        {
            get => amount;
            set
            {
                amount = value;
                quad.repeat = amount < offBelow
                    ? 0f
                    : Mathf.Lerp(farSpacing, nearSpacing, Mathf.InverseLerp(offBelow, 1f, amount));
            }
        }

        protected override string Caption => quad.repeat > 0f ? $"Repeat\nevery {quad.repeat:F2} m" : "Repeat\noff";

        protected override void OnDisable()
        {
            base.OnDisable();
            amount = 0f;
            if (quad != null) quad.repeat = 0f;
        }
    }
}
