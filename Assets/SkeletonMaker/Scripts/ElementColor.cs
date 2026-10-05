using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// An element's own color, given to it by dipping it in a ColorBath. Only
    /// painted elements carry this; unpainted ones keep their material's color
    /// (and, in the RaymarchQuad, the color of whichever group they're in).
    /// Lives on the element root next to its "Visual" child, and is copied
    /// along with it - so duplicates, mirror copies and the avatar body's
    /// copies all come out the same color.
    /// </summary>
    public class ElementColor : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Color color = Color.white;

        private MaterialPropertyBlock block;

        public Color Color => color;

        /// <summary>Gives the element this color, adding the component if it
        /// hasn't been painted before.</summary>
        public static void Paint(GameObject element, Color color)
        {
            if (!element.TryGetComponent(out ElementColor own)) own = element.AddComponent<ElementColor>();
            else if (own.color == color) return;

            own.color = color;
            own.Apply();
        }

        private void OnEnable() => Apply();

        /// <summary>Tints the visual's mesh. Call again after swapping its
        /// material: the tint keeps that material's own transparency.</summary>
        public void Apply()
        {
            var renderer = VisualRenderer();
            if (renderer == null) return;

            Color tint = color;
            var material = renderer.sharedMaterial;
            if (material != null && material.HasProperty(BaseColorId)) tint.a = material.GetColor(BaseColorId).a;

            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColorId, tint);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>Back to unpainted.</summary>
        public void Clear()
        {
            var renderer = VisualRenderer();
            if (renderer != null) renderer.SetPropertyBlock(null);
            Destroy(this);
        }

        private MeshRenderer VisualRenderer()
        {
            var visual = transform.Find("Visual");
            return visual != null ? visual.GetComponent<MeshRenderer>() : null;
        }
    }
}
