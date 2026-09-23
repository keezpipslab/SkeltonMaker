using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Lightweight "which shape is this" tag for placed-element copies that
    /// no longer carry a RaymarchableElement - AvatarDuplicateManager strips
    /// that component (and its grab/scale machinery) from the stand-in's
    /// duplicates, which would otherwise lose Kind along with it.
    /// RaymarchQuad reads Kind from here; the shape's size and orientation
    /// come from the element's "Visual" child transform, so only Kind needs
    /// to be kept.
    /// </summary>
    public class RaymarchShape : MonoBehaviour
    {
        public PrimitiveKind kind;
    }
}
