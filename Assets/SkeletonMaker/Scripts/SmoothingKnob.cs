using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The dial for a RaymarchQuad's smoothing: how far apart two shapes
    /// still blend into each other (meters). See Knob for how it's turned.
    /// </summary>
    public class SmoothingKnob : Knob
    {
        protected override float Value
        {
            get => quad.smoothing;
            set => quad.smoothing = value;
        }

        protected override string Caption => "Smoothing\n" + quad.smoothing.ToString("F3");
    }
}
