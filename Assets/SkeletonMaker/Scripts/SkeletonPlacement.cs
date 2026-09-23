using System.Collections;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// On release: close enough to the skeleton -> parents to it and stays,
    /// spawning a replacement on the table. Too far -> after a grace period
    /// (re-grabbing during it cancels this), it reappears back at its table
    /// slot instead of being lost.
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
        private Coroutine discardRoutine;

        private void Awake() => grabbable = GetComponent<Grabbable>();

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

        private void OnGrabbed()
        {
            if (discardRoutine == null) return;
            StopCoroutine(discardRoutine);
            discardRoutine = null;
        }

        private void OnReleased()
        {
            var rig = SkeletonRig.Instance;
            float distance = rig != null ? rig.DistanceToNearestBone(transform.position) : float.MaxValue;

            if (distance <= placeDistance)
            {
                PlaceOnSkeleton(rig);
            }
            else
            {
                discardRoutine = StartCoroutine(DiscardAfterDelay());
            }
        }

        private void PlaceOnSkeleton(SkeletonRig rig)
        {
            transform.SetParent(rig.transform, true);

            if (HomeSpawnPoint != null) HomeSpawnPoint.SpawnReplacement();
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
                transform.SetPositionAndRotation(HomeSpawnPoint.transform.position, HomeSpawnPoint.transform.rotation);
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
