using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// An experiment in resizing without the thumbsticks: while one hand holds
    /// a primitive, four small cubes float around it - one past its top, one
    /// past its side and one past its front (whichever of each is on the other hand's side), and a reddish one off the corner
    /// between them. Squeeze the other hand's grip at a cube and pull: the
    /// three change the size along their own axis, the corner one all three
    /// together. The primitive grows about its middle, so a cube follows the
    /// hand that pulls it.
    ///
    /// One of these in a scene is enough; it finds the hands (HandGrabber)
    /// itself and builds its cubes when the scene starts.
    /// </summary>
    public class SizeHandles : MonoBehaviour
    {
        private const int Corner = 3; // handles 0, 1, 2 are the local X, Y and Z axis

        [SerializeField] private float handleSize = 0.03f;
        [Tooltip("How far (meters) the cubes float off the primitive's surface.")]
        [SerializeField] private float gap = 0.04f;
        [Tooltip("How close (meters) the hand must be to a cube for the grip to take it.")]
        [SerializeField] private float reach = 0.06f;
        [SerializeField] private Color axisColor = new Color(0.9f, 0.9f, 0.9f);
        [SerializeField] private Color cornerColor = new Color(0.9f, 0.25f, 0.2f);
        [SerializeField] private float hoverScale = 1.4f;
        [Tooltip("How far past the middle of the primitive (0..1) the free hand must be before a cube changes sides to it.")]
        [SerializeField] private float flipMargin = 0.25f;

        private static SizeHandles instance;

        private readonly Transform[] handles = new Transform[4];
        private HandGrabber[] hands;

        private RaymarchableElement element;
        private HandGrabber freeHand;
        private Vector3 signs = Vector3.one; // which side of the primitive each axis cube is on

        private int dragged = -1;
        private bool wasPressed;
        private Vector3 dragStartLocal;
        private Vector3 dragStartSize;

        /// <summary>True when a grip squeezed at this point takes one of the cubes,
        /// so the hand shouldn't also pick something up.</summary>
        public static bool Claims(Vector3 worldPoint) =>
            instance != null && instance.element != null && instance.NearestHandle(worldPoint) >= 0;

        private void Awake()
        {
            for (int i = 0; i < handles.Length; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = i == Corner ? "Size Handle All" : "Size Handle " + "XYZ"[i];
                Destroy(cube.GetComponent<Collider>());
                cube.transform.SetParent(transform, false);

                var material = cube.GetComponent<MeshRenderer>().material; // the pipeline's default, so it draws in URP
                material.color = i == Corner ? cornerColor : axisColor;

                cube.SetActive(false);
                handles[i] = cube.transform;
            }
        }

        private void OnEnable()
        {
            instance = this;
            Application.onBeforeRender += PlaceHandles;
        }

        private void OnDisable()
        {
            if (instance == this) instance = null;
            Application.onBeforeRender -= PlaceHandles;
            Show(null);
        }

        private void Update()
        {
            if (hands == null || hands.Length == 0) hands = FindObjectsByType<HandGrabber>(FindObjectsSortMode.None);

            Show(FindHeld(out freeHand));
            if (element == null) return;

            bool pressed = freeHand.GripPressed;
            Vector3 handLocal = ToLocal(freeHand.transform.position);

            if (pressed && !wasPressed)
            {
                dragged = NearestHandle(freeHand.transform.position);
                dragStartLocal = handLocal;
                dragStartSize = element.Size;
            }
            else if (!pressed)
            {
                dragged = -1;
            }
            wasPressed = pressed;

            if (dragged >= 0) element.Size = DraggedSize(handLocal - dragStartLocal);
            else FaceFreeHand(flipMargin);

            int hovered = dragged >= 0 ? dragged : NearestHandle(freeHand.transform.position);
            for (int i = 0; i < handles.Length; i++)
                handles[i].localScale = Vector3.one * (handleSize * (i == hovered ? hoverScale : 1f));

            PlaceHandles();
        }

        /// <summary>The size the primitive gets when the hand has moved this far (in
        /// the primitive's own axes) since it took the cube.</summary>
        private Vector3 DraggedSize(Vector3 moved)
        {
            Vector3 size = dragStartSize;

            if (dragged == Corner)
            {
                // Multiplied, so the shape keeps its proportions.
                Vector3 corner = HandleLocal(Corner, dragStartSize);
                float distance = corner.magnitude;
                float factor = (distance + Vector3.Dot(moved, corner / distance)) / distance;
                return size * Mathf.Max(factor, 0.05f);
            }

            // Both faces move, so twice the hand's travel keeps the cube under the hand.
            size[dragged] += 2f * signs[dragged] * moved[dragged];
            return size;
        }

        /// <summary>The primitive whose size the cubes set: the one in a hand while
        /// the other hand holds nothing. Null when there is none.</summary>
        private RaymarchableElement FindHeld(out HandGrabber other)
        {
            other = null;
            if (hands.Length != 2) return null;

            for (int i = 0; i < 2; i++)
            {
                var held = hands[i].Held;
                if (held == null || hands[1 - i].Held != null) continue;
                if (!held.TryGetComponent(out RaymarchableElement found)) continue;
                other = hands[1 - i];
                return found;
            }
            return null;
        }

        private void Show(RaymarchableElement shown)
        {
            if (shown == element) return;
            element = shown;
            dragged = -1;

            // Not taken by a grip that was already down when the cubes appeared.
            wasPressed = true;

            foreach (var handle in handles)
                if (handle != null) handle.gameObject.SetActive(element != null);
            if (element != null) FaceFreeHand(0f);
        }

        /// <summary>Puts each cube on the side of the primitive the free hand is on.
        /// A cube only changes sides once the hand is past the middle by more
        /// than the margin, so it doesn't flicker while the hand is level with it.</summary>
        private void FaceFreeHand(float margin)
        {
            Vector3 toHand = ToLocal(freeHand.transform.position).normalized;
            for (int i = 0; i < 3; i++)
                if (signs[i] * toHand[i] < -margin) signs[i] = -signs[i];
        }

        // Again just before rendering, when the controllers get their latest pose.
        private void PlaceHandles()
        {
            if (element == null) return;

            Transform t = element.transform;
            for (int i = 0; i < handles.Length; i++)
                handles[i].SetPositionAndRotation(t.position + t.rotation * HandleLocal(i, element.Size), t.rotation);
        }

        /// <summary>Where a cube sits, in the primitive's own axes, for a primitive of this size.</summary>
        private Vector3 HandleLocal(int index, Vector3 size)
        {
            if (index == Corner) return Vector3.Scale(signs, size * 0.5f + Vector3.one * (gap * 0.5f));

            Vector3 local = Vector3.zero;
            local[index] = signs[index] * (size[index] * 0.5f + gap);
            return local;
        }

        private int NearestHandle(Vector3 worldPoint)
        {
            int best = -1;
            float bestDistance = reach;
            for (int i = 0; i < handles.Length; i++)
            {
                float distance = Vector3.Distance(worldPoint, handles[i].position);
                if (distance > bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            return best;
        }

        private Vector3 ToLocal(Vector3 worldPoint) =>
            Quaternion.Inverse(element.transform.rotation) * (worldPoint - element.transform.position);
    }
}
