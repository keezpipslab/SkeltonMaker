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

        /// <summary>What this hand is holding, or null.</summary>
        public Grabbable Held => held;

        public bool GripPressed => selectAction != null && selectAction.action.IsPressed();

        // A on the right controller, held while grabbing with either hand:
        // take a copy and leave the original where it is. Built here rather
        // than wired in the scene, as both hands share the one modifier.
        private static InputAction duplicateAction;

        private static InputAction DuplicateAction
        {
            get
            {
                if (duplicateAction == null)
                {
                    duplicateAction = new InputAction("Duplicate", InputActionType.Button);
                    duplicateAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
                }
                return duplicateAction;
            }
        }

        private void OnEnable()
        {
            DuplicateAction.Enable();
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
            if (SizeHandles.Claims(transform.position)) return; // the grip is pulling a size cube instead

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
                if (DuplicateAction.IsPressed() && best.TryGetComponent(out SkeletonPlacement placement))
                    best = placement.DuplicateForGrab();

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
