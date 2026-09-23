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
            var placement = instance.GetComponent<SkeletonPlacement>();
            if (placement != null) placement.HomeSpawnPoint = this;
        }
    }
}
