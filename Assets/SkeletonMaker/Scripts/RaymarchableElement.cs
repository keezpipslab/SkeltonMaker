using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// A primitive shape placed on the table / skeleton. Kind + Size are the
    /// stable, "raymarch-ready" data (shape type, per-axis extents in meters)
    /// that a future SDF material can read directly - only the visual
    /// representation (a plain mesh, for now) is expected to change later.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(BoxCollider))]
    public class RaymarchableElement : MonoBehaviour
    {
        [SerializeField] private PrimitiveKind kind = PrimitiveKind.Sphere;
        [SerializeField] private Vector3 size = new Vector3(0.15f, 0.15f, 0.15f);
        [SerializeField] private Vector3 minSize = new Vector3(0.03f, 0.03f, 0.03f);
        [SerializeField] private Vector3 maxSize = new Vector3(0.6f, 0.6f, 0.6f);
        [SerializeField] private Material material;

        // Persisted (but hidden) so a rebuild can tell "kind changed since the
        // visual was built" apart from "visual just isn't loaded into this
        // field yet after a domain reload" - the latter must reuse the
        // existing child, the former must replace it.
        [SerializeField, HideInInspector] private int builtKindRaw = -1;

        // Persisted for the same reason as builtKindRaw above (survives
        // domain reloads); captures the size a fresh instance started at, so
        // ResetSize() can restore it later regardless of reload timing.
        [SerializeField, HideInInspector] private Vector3 defaultSize;
        [SerializeField, HideInInspector] private bool defaultSizeCaptured;

        private Transform visual;
        private BoxCollider boxCollider;

        public PrimitiveKind Kind => kind;

        public Vector3 Size
        {
            get => size;
            set
            {
                size = Clamp(value);
                Apply();
            }
        }

        private void Awake()
        {
            boxCollider = GetComponent<BoxCollider>();
            boxCollider.isTrigger = true;

            // The size a fresh instance of this prefab starts at, captured
            // once before anything (e.g. HeldElementScaler) has a chance to
            // change it - ResetSize() puts it back to this.
            if (!defaultSizeCaptured)
            {
                defaultSize = size;
                defaultSizeCaptured = true;
            }

            SyncVisual();
        }

        /// <summary>Puts Size back to what this instance started at (its prefab's
        /// authored size), e.g. when an element returns to the table unplaced.</summary>
        public void ResetSize() => Size = defaultSizeCaptured ? defaultSize : size;

        /// <summary>Rebuilds the visual mesh if Kind changed since it was last built,
        /// then reapplies Size. Safe to call any time (editor tooling included).</summary>
        public void SyncVisual()
        {
            BuildVisual();
            Apply();
        }

        private void BuildVisual()
        {
            if (visual != null && builtKindRaw == (int)kind) return;

            // Survive domain reloads in edit mode: the (non-serialized) `visual`
            // field resets to null on recompile even though the child GameObject
            // from a previous build is still there - reuse it instead of creating
            // a duplicate, unless Kind changed since it was built.
            var existing = transform.Find("Visual");
            if (existing != null && builtKindRaw == (int)kind)
            {
                visual = existing;
                return;
            }

            // Kind changed (or this is stale/corrupt state): remove every
            // "Visual" child, not just the first, so this is self-correcting
            // even if a previous rebuild left more than one behind.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name == "Visual") DestroyImmediateOrRuntime(child.gameObject);
            }

            GameObject go;
            switch (kind)
            {
                case PrimitiveKind.Sphere:
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    break;
                case PrimitiveKind.Box:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    break;
                case PrimitiveKind.Capsule:
                    go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    break;
                case PrimitiveKind.Cylinder:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    break;
                default:
                    go = new GameObject("Visual");
                    go.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshFactory.Get(kind);
                    go.AddComponent<MeshRenderer>();
                    break;
            }

            go.name = "Visual";
            var builtInCollider = go.GetComponent<Collider>();
            if (builtInCollider != null) DestroyImmediateOrRuntime(builtInCollider); // the root BoxCollider handles grabbing

            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            visual = go.transform;
            builtKindRaw = (int)kind;
        }

        private void Apply()
        {
            if (visual != null)
            {
                visual.localScale = LocalScaleFor(visual, size);

                // Always reapply (not just right after BuildVisual creates a
                // fresh visual): if Kind's default value happens to match
                // builtKindRaw from the very first Awake-time build (true for
                // Sphere, kind 0), BuildVisual's "reuse the existing visual"
                // path returns before material is ever set on it otherwise.
                if (material != null)
                {
                    var renderer = visual.GetComponent<MeshRenderer>();
                    if (renderer != null && renderer.sharedMaterial != material) renderer.sharedMaterial = material;
                }
            }

            if (boxCollider != null) boxCollider.size = size;
        }

        /// <summary>Converts a desired world-ish Size into localScale by dividing out
        /// the visual's own mesh bounds, so every kind - built-in or procedural,
        /// whatever its native dimensions - maps Size to the same visual extents
        /// without needing a hand-picked factor per kind (built-ins already have
        /// correct bounds, e.g. the capsule's is (1,2,1)).</summary>
        private static Vector3 LocalScaleFor(Transform v, Vector3 s)
        {
            var meshFilter = v.GetComponent<MeshFilter>();
            var meshBounds = meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.bounds.size : Vector3.one;
            return new Vector3(
                meshBounds.x > 1e-5f ? s.x / meshBounds.x : s.x,
                meshBounds.y > 1e-5f ? s.y / meshBounds.y : s.y,
                meshBounds.z > 1e-5f ? s.z / meshBounds.z : s.z);
        }

        private Vector3 Clamp(Vector3 v) => new Vector3(
            Mathf.Clamp(v.x, minSize.x, maxSize.x),
            Mathf.Clamp(v.y, minSize.y, maxSize.y),
            Mathf.Clamp(v.z, minSize.z, maxSize.z));

        private void OnValidate()
        {
            size = Clamp(size);
            if (boxCollider == null) boxCollider = GetComponent<BoxCollider>();

#if UNITY_EDITOR
            // Unity disallows DestroyImmediate from inside OnValidate (a kind
            // change needs one, to replace the old visual) - defer the actual
            // rebuild to just after instead of doing it inline here.
            UnityEditor.EditorApplication.delayCall += DeferredSyncVisual;
#else
            SyncVisual();
#endif
        }

#if UNITY_EDITOR
        private void DeferredSyncVisual()
        {
            if (this == null) return; // destroyed before the deferred call ran
            SyncVisual();
        }
#endif

        private static void DestroyImmediateOrRuntime(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
