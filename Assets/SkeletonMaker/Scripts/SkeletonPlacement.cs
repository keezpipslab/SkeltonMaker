using System.Collections;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// While held, previews where the element would land (SkeletonRig highlights
    /// the nearest bone). On release: close enough -> parents under that bone
    /// (estimated as the nearest bone segment) and stays, spawning a replacement
    /// on the table and triggering the avatar-duplicate hook. Too far -> after a
    /// grace period (re-grabbing during it cancels this), it reappears back at
    /// its table slot instead of being lost.
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class SkeletonPlacement : MonoBehaviour
    {
        [SerializeField] private float placeDistance = 0.15f;
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
            SkeletonRig.Instance?.UpdateHeldPreview(transform.position, placeDistance);
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

            int boneIndex = -1;
            float distance = float.MaxValue;
            if (rig != null) boneIndex = rig.NearestBoneIndex(transform.position, out distance);

            if (rig != null && distance <= placeDistance)
            {
                PlaceOnSkeleton(rig, boneIndex);
            }
            else
            {
                discardRoutine = StartCoroutine(DiscardAfterDelay());
            }
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
