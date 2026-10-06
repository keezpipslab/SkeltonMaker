using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The dial for a RaymarchQuad's inflate: a number subtracted from the
    /// distance to the surface everywhere (d - c), which moves the whole
    /// surface outward by that much (meters) - or inward, below zero. Goes
    /// back to zero when it's switched off, so leaving the math stage leaves
    /// the skeleton as it was built. See Knob for how it's turned.
    /// </summary>
    public class InflateKnob : Knob
    {
        protected override float Value
        {
            get => quad.inflate;
            set => quad.inflate = value;
        }

        protected override string Caption => "Inflate\n" + quad.inflate.ToString("+0.000;-0.000;0.000");

        protected override void OnDisable()
        {
            base.OnDisable();
            if (quad != null) quad.inflate = 0f;
        }
    }
}
