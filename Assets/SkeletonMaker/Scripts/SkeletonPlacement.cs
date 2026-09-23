using System.Collections;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// On release: close enough to the skeleton -> parents to it and stays,
    /// spawning a replacement on the table. Too far -> destroyed after a
    /// grace period (re-grabbing during the grace period cancels it).
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class SkeletonPlacement : MonoBehaviour
    {
        [SerializeField] private float placeDistance = 0.15f;
        [SerializeField] private float discardDelay = 4f;

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

            if (HomeSpawnPoint != null)
            {
                HomeSpawnPoint.SpawnReplacement();
                HomeSpawnPoint = null;
            }
        }

        private IEnumerator DiscardAfterDelay()
        {
            yield return new WaitForSeconds(discardDelay);
            discardRoutine = null;
            Destroy(gameObject);
        }
    }
}
