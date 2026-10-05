using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The button for mirror placement (SkeletonRig.MirrorEnabled, also on the
    /// M key): lit while it's on, and then placing a shape on one side of the
    /// body places a copy on the other. See PushButton for how it's pressed.
    /// </summary>
    public class MirrorToggle : PushButton
    {
        protected override bool Lit => SkeletonRig.Instance != null && SkeletonRig.Instance.MirrorEnabled;

        protected override string Caption => Lit ? "Mirror\non" : "Mirror\noff";

        protected override void Press()
        {
            var rig = SkeletonRig.Instance;
            if (rig != null) rig.MirrorEnabled = !rig.MirrorEnabled;
        }
    }
}
