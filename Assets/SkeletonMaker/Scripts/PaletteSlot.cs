using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// One place on the designer panel that always holds a primitive to pick
    /// up: the table's TableSpawnPoint, but the primitive is a child of the
    /// slot, so it moves with the panel. A fresh one appears as soon as the
    /// last has been placed in the world (or deleted). One that is let go of
    /// right at its slot goes back into it instead.
    /// </summary>
    public class PaletteSlot : MonoBehaviour
    {
        [SerializeField] private GameObject primitivePrefab;
        [Tooltip("Let go of within this far (meters) of the slot, the primitive goes back into it rather than into the world.")]
        [SerializeField] private float returnRadius = 0.08f;

        private Grabbable current;

        private void Update()
        {
            if (current != null)
            {
                if (current.IsHeld || current.transform.parent == transform) return;

                if (Vector3.Distance(current.transform.position, transform.position) <= returnRadius)
                {
                    PutBack();
                    return;
                }
            }

            Spawn();
        }

        private void Spawn()
        {
            current = null;
            if (primitivePrefab == null) return;
            current = Instantiate(primitivePrefab, transform).GetComponent<Grabbable>();
        }

        private void PutBack()
        {
            current.transform.SetParent(transform, false);
            current.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            current.transform.localScale = Vector3.one;

            // As it came: not in whatever shape it was pulled into in the hand.
            if (current.TryGetComponent(out RaymarchableElement element)) element.ResetSize();
            if (current.TryGetComponent(out WorldElement placed))
            {
                placed.Subtract = false;
                placed.Smoothing = 0f;
            }
        }
    }
}
