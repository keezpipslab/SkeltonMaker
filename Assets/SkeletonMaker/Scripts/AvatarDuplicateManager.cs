using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// On each placement, mirrors a very transparent, non-interactive duplicate
    /// of the placed element onto the corresponding joint of the player's own
    /// avatar body (once one exists - see AvatarBodyTarget). Until a real avatar
    /// is wired in (Meta Movement SDK), every call simply no-ops: there's no
    /// AvatarBodyTarget in the scene, or the relevant joint slot isn't assigned
    /// yet, both of which are the expected/normal state for now.
    /// </summary>
    [ExecuteAlways]
    public class AvatarDuplicateManager : MonoBehaviour
    {
        [SerializeField] private Material ghostMaterial;

        public static AvatarDuplicateManager Instance { get; private set; }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Called right after an element is placed on the skeleton (already
        /// reparented under its bone anchor). Clones it onto the matching avatar
        /// joint, preserving the same local offset it has relative to its bone, as
        /// a transparent, non-interactive decoration. Returns the copy (null if
        /// there was nowhere to put it) so the caller can remove it again once
        /// the element is picked back up off the skeleton.</summary>
        public GameObject PlaceDuplicate(string jointName, RaymarchableElement sourceElement, Transform placedTransform)
        {
            var target = AvatarBodyTarget.Instance;
            if (target == null) return null;

            Transform jointTransform = target.GetJoint(jointName);
            if (jointTransform == null) return null; // slot not wired up yet

            var duplicate = Instantiate(placedTransform.gameObject, jointTransform);
            duplicate.name = placedTransform.name + " (Avatar Duplicate)";
            MakeGhost(duplicate, sourceElement.Kind);
            return duplicate;
        }

        /// <summary>Builds the same kind of decoration from saved data instead of
        /// from a live element: one primitive of a remote performer's skeleton,
        /// under the anchor it was placed on there. Null if there is no prefab
        /// for its kind.</summary>
        public GameObject BuildGhost(SkeletonComposition.Element saved, Transform anchor)
        {
            var store = CompositionStore.Instance;
            if (store == null || !System.Enum.TryParse(saved.kind, out PrimitiveKind kind)) return null;
            if (!store.TryGetPrefab(kind, out GameObject prefab)) return null;

            var ghost = Instantiate(prefab, anchor);
            ghost.name = prefab.name + " (Remote)";
            ghost.transform.SetLocalPositionAndRotation(saved.localPosition, saved.localRotation);
            // The anchor may be switched off, and then the element's own Awake hasn't found its visual yet.
            var element = ghost.GetComponent<RaymarchableElement>();
            element.Size = saved.size;
            element.SyncVisual();
            if (saved.painted) ElementColor.Paint(ghost, saved.color);
            MakeGhost(ghost, kind);
            return ghost;
        }

        private void MakeGhost(GameObject ghost, PrimitiveKind kind)
        {
            // Keep Kind on the copy (StripInteractivity removes the
            // RaymarchableElement it lives on) so RaymarchQuad can draw it.
            ghost.AddComponent<RaymarchShape>().kind = kind;
            StripInteractivity(ghost);
            ApplyGhostMaterial(ghost);
            if (ghost.TryGetComponent(out ElementColor ownColor)) ownColor.Apply(); // re-tint: the ghost material is a different one
        }

        private static void StripInteractivity(GameObject duplicate)
        {
            // Order matters: RequireComponent blocks Destroy() on a component
            // while anything still present depends on it (Destroy is deferred in
            // Play mode, so the dependent has to actually be gone first, not just
            // "also about to be destroyed"). So dependents go first - both
            // HeldElementScaler and SkeletonPlacement require Grabbable,
            // HeldElementScaler also requires RaymarchableElement - then Grabbable,
            // then RaymarchableElement, then the BoxCollider RaymarchableElement
            // itself requires.
            DestroyImmediateOrRuntime(duplicate.GetComponent<HeldElementScaler>());
            DestroyImmediateOrRuntime(duplicate.GetComponent<SkeletonPlacement>());
            DestroyImmediateOrRuntime(duplicate.GetComponent<Grabbable>());
            DestroyImmediateOrRuntime(duplicate.GetComponent<RaymarchableElement>());
            DestroyImmediateOrRuntime(duplicate.GetComponent<BoxCollider>());
        }

        private void ApplyGhostMaterial(GameObject duplicate)
        {
            if (ghostMaterial == null) return;

            var visual = duplicate.transform.Find("Visual");
            var renderer = visual != null ? visual.GetComponent<MeshRenderer>() : null;
            if (renderer != null) renderer.sharedMaterial = ghostMaterial;
        }

        private static void DestroyImmediateOrRuntime(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
