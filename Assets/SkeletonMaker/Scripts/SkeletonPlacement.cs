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

        // The copy of this element on the avatar body while it's placed; taken
        // away again when the element is picked back up, so moving a placed
        // element moves its copy too instead of leaving the old one behind.
        private GameObject avatarDuplicate;

        // Set once the table slot no longer belongs to this element: either its
        // replacement has been spawned there, or it's a copy (duplicate/mirror)
        // of an element that has a slot of its own.
        private bool leftTable;

        // The element on the other side of the body that was placed together
        // with this one in mirror mode (each points at the other).
        private SkeletonPlacement mirrorPartner;

        /// <summary>Leaves this element where it is and returns a fresh,
        /// independent copy of it in the same pose, for the hand to take
        /// instead.</summary>
        public Grabbable DuplicateForGrab() => Copy(transform.parent).GetComponent<Grabbable>();

        private SkeletonPlacement Copy(Transform parent)
        {
            var copy = Instantiate(gameObject, parent).GetComponent<SkeletonPlacement>();
            copy.name = name;
            copy.HomeSpawnPoint = HomeSpawnPoint;
            copy.leftTable = true;
            return copy;
        }

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
            if (avatarDuplicate != null) Destroy(avatarDuplicate);
            avatarDuplicate = null;

            // In mirror mode the pair moves as one: the other side goes away
            // now and is placed afresh wherever this one is released. With
            // mirror off they simply become two unrelated elements.
            var partner = mirrorPartner;
            mirrorPartner = null;
            if (partner != null)
            {
                partner.mirrorPartner = null;
                var rig = SkeletonRig.Instance;
                if (rig != null && rig.MirrorEnabled && !partner.grabbable.IsHeld)
                {
                    if (partner.avatarDuplicate != null) Destroy(partner.avatarDuplicate);
                    Destroy(partner.gameObject);
                }
            }

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
            Place(rig.HingeJointAnchor(jointIndex), rig.HingeJointName(jointIndex));

            if (!rig.MirrorEnabled) return;
            int mirrorIndex = rig.MirrorHingeJointIndex(jointIndex);
            if (mirrorIndex >= 0) PlaceMirrored(rig, rig.HingeJointAnchor(mirrorIndex), rig.HingeJointName(mirrorIndex));
        }

        private void PlaceOnSkeleton(SkeletonRig rig, int boneIndex)
        {
            Place(rig.BoneAnchor(boneIndex), rig.BoneJointName(boneIndex));

            if (!rig.MirrorEnabled) return;
            int mirrorIndex = rig.MirrorBoneIndex(boneIndex);
            if (mirrorIndex >= 0) PlaceMirrored(rig, rig.BoneAnchor(mirrorIndex), rig.BoneJointName(mirrorIndex));
        }

        private void Place(Transform anchor, string jointName)
        {
            transform.SetParent(anchor, true);

            // Only the first time: once it has been placed, the slot already
            // holds its replacement, and re-placing it mustn't stack another.
            if (!leftTable && HomeSpawnPoint != null) HomeSpawnPoint.SpawnReplacement();
            leftTable = true;

            // No-ops until a real avatar (Meta Movement SDK) is wired into an
            // AvatarBodyTarget's joint slots - see AvatarDuplicateManager.
            avatarDuplicate = AvatarDuplicateManager.Instance?.PlaceDuplicate(jointName, element, transform);
        }

        // The same element again on the other side of the body: its own,
        // separately grabbable copy on the opposite limb's anchor.
        private void PlaceMirrored(SkeletonRig rig, Transform mirrorAnchor, string mirrorJointName)
        {
            rig.MirrorPose(transform.position, transform.rotation, out Vector3 position, out Quaternion rotation);

            // A centered element on the spine would just land on top of itself.
            if (Vector3.Distance(position, transform.position) < MinMirrorSeparation) return;

            var mirrored = Copy(mirrorAnchor);
            mirrored.transform.SetPositionAndRotation(position, rotation);
            mirrored.mirrorPartner = this;
            mirrorPartner = mirrored;
            mirrored.avatarDuplicate = AvatarDuplicateManager.Instance?.PlaceDuplicate(mirrorJointName, mirrored.element, mirrored.transform);
        }

        private const float MinMirrorSeparation = 0.01f;

        private IEnumerator DiscardAfterDelay()
        {
            yield return new WaitForSeconds(discardDelay);
            discardRoutine = null;
            ReturnToTable();
        }

        private void ReturnToTable()
        {
            if (HomeSpawnPoint != null && !leftTable)
            {
                transform.SetParent(null, true);
                if (element != null) element.ResetSize(); // don't leave it in whatever shape it was resized to
                Vector3 pos = element != null ? HomeSpawnPoint.RestingPosition(element) : HomeSpawnPoint.transform.position;
                transform.SetPositionAndRotation(pos, HomeSpawnPoint.transform.rotation);
            }
            else
            {
                // Its table slot is already taken by its replacement (or it
                // never had one), so there's nowhere to put it back.
                Destroy(gameObject);
            }
        }
    }
}
