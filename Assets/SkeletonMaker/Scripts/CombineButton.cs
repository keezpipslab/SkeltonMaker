using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// One of the Together Math stage's four buttons, which choose how the
    /// raymarch quad puts your avatar (A) and the other performer's (B)
    /// together - Union (both, merging where they touch), Subtract A (B with
    /// A carved out of it), Subtract B (A with B carved out of it) or
    /// Intersect (only where they overlap): lit while its way is the current
    /// one. The quad goes back to Union when the buttons are switched off, so
    /// every other stage shows both avatars whole. See PushButton for how
    /// it's pressed.
    /// </summary>
    public class CombineButton : PushButton
    {
        [SerializeField] private CombineMode mode;

        private RaymarchQuad quad;

        private RaymarchQuad Quad
        {
            get
            {
                if (quad == null) quad = FindFirstObjectByType<RaymarchQuad>(FindObjectsInactive.Include);
                return quad;
            }
        }

        protected override bool Lit => Quad != null && Quad.combine == mode;

        protected override string Caption
        {
            get
            {
                switch (mode)
                {
                    case CombineMode.SubtractA: return "Subtract A";
                    case CombineMode.SubtractB: return "Subtract B";
                    default: return mode.ToString();
                }
            }
        }

        protected override void Press()
        {
            if (Quad != null) Quad.combine = mode;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (quad != null) quad.combine = CombineMode.Union;
        }
    }
}
