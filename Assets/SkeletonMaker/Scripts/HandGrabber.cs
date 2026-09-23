using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// Lives on each controller's grab point. Watches the grip ("Select")
    /// button and picks up the nearest free Grabbable within reach, or lets
    /// go of whatever it's currently holding.
    /// </summary>
    public class HandGrabber : MonoBehaviour
    {
        [SerializeField] private InputActionReference selectAction;
        [SerializeField] private float grabRadius = 0.1f;
        [SerializeField] private LayerMask grabbableLayer = ~0;

        private Grabbable held;

        private void OnEnable()
        {
            if (selectAction == null) return;
            selectAction.action.performed += OnSelectPerformed;
            selectAction.action.canceled += OnSelectCanceled;
        }

        private void OnDisable()
        {
            if (selectAction != null)
            {
                selectAction.action.performed -= OnSelectPerformed;
                selectAction.action.canceled -= OnSelectCanceled;
            }

            ReleaseHeld();
        }

        private void OnSelectPerformed(InputAction.CallbackContext ctx)
        {
            if (held != null) return;

            var hits = Physics.OverlapSphere(transform.position, grabRadius, grabbableLayer, QueryTriggerInteraction.Collide);
            Grabbable best = null;
            float bestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                var candidate = hit.GetComponentInParent<Grabbable>();
                if (candidate == null || candidate.IsHeld) continue;

                float dist = Vector3.Distance(transform.position, hit.ClosestPoint(transform.position));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }

            if (best != null)
            {
                held = best;
                held.Grab(transform);
            }
        }

        private void OnSelectCanceled(InputAction.CallbackContext ctx) => ReleaseHeld();

        private void ReleaseHeld()
        {
            if (held == null) return;
            held.Release();
            held = null;
        }
    }
}
