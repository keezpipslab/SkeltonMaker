using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Marks one "home" slot on the table for a given primitive prefab, and
    /// spawns a fresh instance there whenever the previous one is placed
    /// onto the skeleton.
    /// </summary>
    public class TableSpawnPoint : MonoBehaviour
    {
        [SerializeField] private GameObject primitivePrefab;

        private void Start() => Spawn();

        public void SpawnReplacement() => Spawn();

        private void Spawn()
        {
            if (primitivePrefab == null) return;

            var instance = Instantiate(primitivePrefab, transform.position, transform.rotation);

            var element = instance.GetComponent<RaymarchableElement>();
            if (element != null) instance.transform.position = RestingPosition(element);

            var placement = instance.GetComponent<SkeletonPlacement>();
            if (placement != null) placement.HomeSpawnPoint = this;
        }

        /// <summary>Where an element's center should sit so it rests on top of this
        /// spawn point's surface instead of being half-buried in the table - this
        /// point's own transform position is the table surface, not the element's
        /// center.</summary>
        public Vector3 RestingPosition(RaymarchableElement element)
        {
            Vector3 pos = transform.position;
            pos.y += element.Size.y * 0.5f;
            return pos;
        }
    }
}
