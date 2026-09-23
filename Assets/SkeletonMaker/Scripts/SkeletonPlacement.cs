using System.Collections;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// While held, previews where the element would land (SkeletonRig highlights
    /// the nearest hinge joint or bone). On release: a hinge joint (shoulder,
    /// elbow, wrist, hip, knee, ankle) within range takes priority - parenting
    /// under that joint's own anchor - since aiming at a joint is the more
    /// deliberate target; otherwise the nearest bone segment, as before. Too far
    /// from either -> after a grace period (re-grabbing during it cancels this),
    /// it reappears back at its table slot instead of being lost.
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class SkeletonPlacement : MonoBehaviour
    {
        [SerializeField] private float placeDistance = 0.15f;

        // Deliberately tighter than placeDistance: most limb bones here are only
        // 0.3-0.4m long, so reusing placeDistance for joints too was swallowing
        // most (up to all) of a bone's own length in joint-priority territory,
        // making bone placement nearly impossible almost everywhere. A joint is
        // meant to be a small, deliberate target, not a wide catch-all.
        [SerializeField] private float jointPlaceDistance = 0.06f;

        [SerializeField] private float discardDelay = 4f;

        // Kept for the element's whole lifetime (not cleared on placement) so
        // it can always find its way back to the table, even if it's later
        // re-grabbed off the skeleton and dropped away.
        public TableSpawnPoint HomeSpawnPoint { get; set; }

        private Grabbable grabbable;
        private RaymarchableElement element;
        private Coroutine discardRoutine;

        private void Awake()
        {
            grabbable = GetComponent<Grabbable>();
            element = GetComponent<RaymarchableElement>();
        }

        private void OnEnable()
        {
            grabbable.Grabbed += OnGrabbed;
            grabbable.Released += OnReleased;
        }

        private void OnDisable()
        {
            grabbable.Grabbed -= OnGrabbed;
            grabbable.Released -= OnReleased;
        }

        private void Update()
        {
            if (!grabbable.IsHeld) return;
            SkeletonRig.Instance?.UpdateHeldPreview(transform.position, placeDistance, jointPlaceDistance);
        }

        private void OnGrabbed()
        {
            if (discardRoutine == null) return;
            StopCoroutine(discardRoutine);
            discardRoutine = null;
        }

        private void OnReleased()
        {
            var rig = SkeletonRig.Instance;
            rig?.ClearHeldPreview();

            if (rig != null)
            {
                int jointIndex = rig.NearestHingeJointIndex(transform.position, out float jointDistance);
                if (jointIndex >= 0 && jointDistance <= jointPlaceDistance)
                {
                    PlaceOnJoint(rig, jointIndex);
                    return;
                }

                int boneIndex = rig.NearestBoneIndex(transform.position, out float boneDistance);
                if (boneIndex >= 0 && boneDistance <= placeDistance)
                {
                    PlaceOnSkeleton(rig, boneIndex);
                    return;
                }
            }

            discardRoutine = StartCoroutine(DiscardAfterDelay());
        }

        private void PlaceOnJoint(SkeletonRig rig, int jointIndex)
        {
            transform.SetParent(rig.HingeJointAnchor(jointIndex), true);

            if (HomeSpawnPoint != null) HomeSpawnPoint.SpawnReplacement();

            // No-ops until a real avatar (Meta Movement SDK) is wired into an
            // AvatarBodyTarget's joint slots - see AvatarDuplicateManager.
            AvatarDuplicateManager.Instance?.PlaceDuplicate(rig.HingeJointName(jointIndex), element, transform);
        }

        private void PlaceOnSkeleton(SkeletonRig rig, int boneIndex)
        {
            transform.SetParent(rig.BoneAnchor(boneIndex), true);

            if (HomeSpawnPoint != null) HomeSpawnPoint.SpawnReplacement();

            // No-ops until a real avatar (Meta Movement SDK) is wired into an
            // AvatarBodyTarget's joint slots - see AvatarDuplicateManager.
            AvatarDuplicateManager.Instance?.PlaceDuplicate(rig.BoneJointName(boneIndex), element, transform);
        }

        private IEnumerator DiscardAfterDelay()
        {
            yield return new WaitForSeconds(discardDelay);
            discardRoutine = null;
            ReturnToTable();
        }

        private void ReturnToTable()
        {
            if (HomeSpawnPoint != null)
            {
                transform.SetParent(null, true);
                if (element != null) element.ResetSize(); // don't leave it in whatever shape it was resized to
                Vector3 pos = element != null ? HomeSpawnPoint.RestingPosition(element) : HomeSpawnPoint.transform.position;
                transform.SetPositionAndRotation(pos, HomeSpawnPoint.transform.rotation);
            }
            else
            {
                // No known table slot (shouldn't normally happen) - fall back
                // to the old behaviour rather than leaving it stranded.
                Destroy(gameObject);
            }
        }
    }
}
