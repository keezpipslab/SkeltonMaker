using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// While the element is held, the left stick's vertical axis scales all
    /// 3 dimensions together (uniform); the remaining 3 stick axes (left
    /// stick X, right stick X and Y) each drive one dimension independently.
    /// </summary>
    [RequireComponent(typeof(Grabbable), typeof(RaymarchableElement))]
    public class HeldElementScaler : MonoBehaviour
    {
        [SerializeField] private InputActionReference leftThumbstick;
        [SerializeField] private InputActionReference rightThumbstick;
        [SerializeField] private float uniformSpeed = 0.25f; // meters/sec at full deflection
        [SerializeField] private float axisSpeed = 0.25f;
        [SerializeField] private float deadzone = 0.15f;

        private Grabbable grabbable;
        private RaymarchableElement element;

        private void Awake()
        {
            grabbable = GetComponent<Grabbable>();
            element = GetComponent<RaymarchableElement>();
        }

        private void Update()
        {
            if (!grabbable.IsHeld || leftThumbstick == null || rightThumbstick == null) return;

            Vector2 left = leftThumbstick.action.ReadValue<Vector2>();
            Vector2 right = rightThumbstick.action.ReadValue<Vector2>();
            float dt = Time.deltaTime;

            Vector3 size = element.Size;
            size += Vector3.one * (WithDeadzone(left.y) * uniformSpeed * dt);
            size.y += WithDeadzone(left.x) * axisSpeed * dt;
            size.x += WithDeadzone(right.x) * axisSpeed * dt;
            size.z += WithDeadzone(right.y) * axisSpeed * dt;

            element.Size = size;
        }

        private float WithDeadzone(float v) => Mathf.Abs(v) < deadzone ? 0f : v;
    }
}
