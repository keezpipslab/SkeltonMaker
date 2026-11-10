using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// In the Designer Before Labels scene: puts the designer back the way it
    /// was before the controller labels and the depth fix, to compare the two.
    /// Carving primitives keep their solid material, only tinted red
    /// (WorldElement.SolidSubtract). The raymarch quad there has the
    /// "RaymarchQuad Before Labels" shader, which draws over everything in
    /// front of the quad rather than writing the depth of what it hit.
    /// </summary>
    [DefaultExecutionOrder(-1000)] // before any WorldElement tints itself
    public class BeforeLabelsMode : MonoBehaviour
    {
        private void Awake() => WorldElement.SolidSubtract = true;

        private void OnDestroy() => WorldElement.SolidSubtract = false;
    }
}
