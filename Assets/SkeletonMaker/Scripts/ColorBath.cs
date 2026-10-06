using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// A disc of color: dip a held element into it and the element takes the
    /// color (see ElementColor). The dip zone is the cylinder standing on the
    /// disc - this object's up axis - and it's the element's center that has
    /// to enter it. Checked by distance rather than physics triggers, since
    /// nothing here carries a Rigidbody.
    /// </summary>
    [ExecuteAlways]
    public class ColorBath : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Color color = Color.red;

        [Tooltip("The disc that shows the bath's color.")]
        [SerializeField] private Renderer surface;

        [Tooltip("Radius of the dip zone (meters).")]
        [SerializeField] private float radius = 0.08f;

        [Tooltip("How far above the disc the dip zone reaches (meters).")]
        [SerializeField] private float height = 0.15f;

        private MaterialPropertyBlock block;

        private void OnEnable() => ApplySurfaceColor();

        private void OnValidate() => ApplySurfaceColor();

        private void ApplySurfaceColor()
        {
            if (surface == null) return;
            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            surface.SetPropertyBlock(block);
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            foreach (var grabbable in Grabbable.Held)
            {
                if (grabbable == null || !grabbable.TryGetComponent(out RaymarchableElement _)) continue;

                Vector3 local = transform.InverseTransformPoint(grabbable.transform.position);
                if (local.y < -0.05f || local.y > height) continue;
                if (local.x * local.x + local.z * local.z > radius * radius) continue;

                ElementColor.Paint(grabbable.gameObject, color);
            }
        }
    }
}
