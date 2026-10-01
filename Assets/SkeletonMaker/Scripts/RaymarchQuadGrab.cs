using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// Lets the left controller's grip pick up and move this quad - a plain
    /// reparent with worldPositionStays=true, same no-snap mechanic as
    /// Grabbable, but kept separate from it and from HandGrabber's shared
    /// proximity search so the right hand (which shares that system's
    /// grabbableLayer with every placeable primitive) never competes for it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class RaymarchQuadGrab : MonoBehaviour
    {
        [SerializeField] private InputActionReference leftGripAction;
        [SerializeField] private Transform leftController;
        [SerializeField] private float grabRadius = 0.2f;

        private Collider col;
        private bool held;

        private void Awake() => col = GetComponent<Collider>();

        private void OnEnable()
        {
            if (leftGripAction == null) return;
            leftGripAction.action.Enable();
            leftGripAction.action.performed += OnGripPerformed;
            leftGripAction.action.canceled += OnGripCanceled;
        }

        private void OnDisable()
        {
            if (leftGripAction != null)
            {
                leftGripAction.action.performed -= OnGripPerformed;
                leftGripAction.action.canceled -= OnGripCanceled;
            }

            Release();
        }

        private void OnGripPerformed(InputAction.CallbackContext ctx)
        {
            if (held || leftController == null) return;

            float dist = Vector3.Distance(leftController.position, col.ClosestPoint(leftController.position));
            if (dist > grabRadius) return;

            held = true;
            transform.SetParent(leftController, true);
        }

        private void OnGripCanceled(InputAction.CallbackContext ctx) => Release();

        private void Release()
        {
            if (!held) return;
            held = false;
            transform.SetParent(null, true);
        }
    }
}
