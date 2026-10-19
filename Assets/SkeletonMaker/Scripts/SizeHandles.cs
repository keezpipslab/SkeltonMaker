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
    /// A primitive of the designer scene (one with a WorldElement) gets a
    /// fifth handle: a small dial out past the edge between the side cube and
    /// the top cube, for its own smoothing. Like the cubes it keeps to the
    /// other hand's side, so it can be reached wherever that hand is. Squeeze the other hand's grip at it and twist the wrist, as
    /// with the dials on the panel: 12 o'clock is nothing extra, clockwise
    /// more, the other way less. Its text gives the number.
    ///
    /// One of these in a scene is enough; it finds the hands (HandGrabber)
    /// itself and builds its cubes when the scene starts.
    /// </summary>
    public class SizeHandles : MonoBehaviour
    {
        private const int Corner = 3; // handles 0, 1, 2 are the local X, Y and Z axis
        private const int Dial = 4;   // the smoothing dial, only there for a WorldElement
        private const float DialSweep = 270f; // degrees from one end of the dial to the other

        [SerializeField] private float handleSize = 0.03f;
        [Tooltip("How far (meters) the cubes float off the primitive's surface.")]
        [SerializeField] private float gap = 0.04f;
        [Tooltip("How close (meters) the hand must be to a cube for the grip to take it.")]
        [SerializeField] private float reach = 0.06f;
        [SerializeField] private Color axisColor = new Color(0.9f, 0.9f, 0.9f);
        [SerializeField] private Color cornerColor = new Color(0.9f, 0.25f, 0.2f);
        [SerializeField] private Color dialColor = new Color(0.18f, 0.2f, 0.26f);
        [SerializeField] private Color dialIndicatorColor = new Color(1f, 0.75f, 0.2f);
        [SerializeField] private float hoverScale = 1.4f;
        [Tooltip("How far past the middle of the primitive (0..1) the free hand must be before a cube changes sides to it.")]
        [SerializeField] private float flipMargin = 0.25f;

        private static SizeHandles instance;

        private readonly Transform[] handles = new Transform[5];
        private HandGrabber[] hands;
        private Transform dialSpin;
        private TextMesh dialLabel;

        private RaymarchableElement element;
        private WorldElement blending; // the same primitive, if it has a smoothing of its own
        private HandGrabber freeHand;
        private Vector3 signs = Vector3.one; // which side of the primitive each axis cube is on

        private int dragged = -1;
        private bool wasPressed;
        private Vector3 dragStartLocal;
        private Vector3 dragStartSize;
        private Quaternion dragLastRotation;

        /// <summary>True when a grip squeezed at this point takes one of the cubes,
        /// so the hand shouldn't also pick something up.</summary>
        public static bool Claims(Vector3 worldPoint) =>
            instance != null && instance.element != null && instance.NearestHandle(worldPoint) >= 0;

        private void Awake()
        {
            for (int i = 0; i < Dial; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = i == Corner ? "Size Handle All" : "Size Handle " + "XYZ"[i];
                Destroy(cube.GetComponent<Collider>());
                cube.transform.SetParent(transform, false);

                RuntimeMaterial.Apply(cube.GetComponent<MeshRenderer>(), i == Corner ? cornerColor : axisColor);

                cube.SetActive(false);
                handles[i] = cube.transform;
            }

            handles[Dial] = BuildDial();
        }

        // The panel's dial in small: a disk and an indicator that turn together,
        // and the number above it. Sized in handle sizes, as it is scaled like the cubes.
        private Transform BuildDial()
        {
            var dial = new GameObject("Smoothing Dial").transform;
            dial.SetParent(transform, false);

            dialSpin = new GameObject("Spin").transform;
            dialSpin.SetParent(dial, false);

            var disk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disk.name = "Disk";
            Destroy(disk.GetComponent<Collider>());
            disk.transform.SetParent(dialSpin, false);
            disk.transform.localScale = new Vector3(1.4f, 0.12f, 1.4f);
            RuntimeMaterial.Apply(disk.GetComponent<MeshRenderer>(), dialColor);

            var indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = "Indicator";
            Destroy(indicator.GetComponent<Collider>());
            indicator.transform.SetParent(dialSpin, false);
            indicator.transform.localPosition = new Vector3(0f, 0.15f, 0.48f);
            indicator.transform.localScale = Vector3.one * 0.3f;
            RuntimeMaterial.Apply(indicator.GetComponent<MeshRenderer>(), dialIndicatorColor);

            var labelObject = new GameObject("Value Label");
            labelObject.transform.SetParent(dial, false);
            labelObject.transform.localPosition = new Vector3(0f, 0f, 1f); // past 12 o'clock
            dialLabel = labelObject.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            dialLabel.font = font;
            labelObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            dialLabel.fontSize = 64;
            dialLabel.characterSize = 0.05f;
            dialLabel.anchor = TextAnchor.LowerCenter;
            dialLabel.alignment = TextAlignment.Center;
            dialLabel.color = Color.white;

            dial.gameObject.SetActive(false);
            return dial;
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
            // Until both are found: a controller that isn't tracked yet when the
            // scene starts comes in a little later, and with one hand the cubes never show.
            if (hands == null || hands.Length != 2 || hands[0] == null || hands[1] == null)
                hands = FindObjectsByType<HandGrabber>(FindObjectsSortMode.None);

            Show(FindHeld(out freeHand));
            if (element == null) return;

            bool pressed = freeHand.GripPressed;
            Vector3 handLocal = ToLocal(freeHand.transform.position);

            if (pressed && !wasPressed)
            {
                dragged = NearestHandle(freeHand.transform.position);
                dragStartLocal = handLocal;
                dragStartSize = element.Size;
                dragLastRotation = freeHand.transform.rotation;
            }
            else if (!pressed)
            {
                dragged = -1;
            }
            wasPressed = pressed;

            if (dragged == Dial) TurnDial();
            else if (dragged >= 0) element.Size = DraggedSize(handLocal - dragStartLocal);
            else FaceFreeHand(flipMargin);

            if (blending != null)
            {
                float turned = Mathf.InverseLerp(-WorldElement.SmoothingRange, WorldElement.SmoothingRange, blending.Smoothing);
                dialSpin.localRotation = Quaternion.AngleAxis((turned - 0.5f) * DialSweep, Vector3.up);
                dialLabel.text = blending.Smoothing.ToString("+0.000;-0.000;0.000");
            }

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
                Vector3 corner = HandleLocal(Corner, dragStartSize * Scale);
                float distance = corner.magnitude;
                float factor = (distance + Vector3.Dot(moved, corner / distance)) / distance;
                return size * Mathf.Max(factor, 0.05f);
            }

            // Both faces move, so twice the hand's travel keeps the cube under the hand.
            size[dragged] += 2f * signs[dragged] * moved[dragged] / Scale;
            return size;
        }

        /// <summary>Turns the primitive's own smoothing by as much as the hand has
        /// twisted about the dial's axis since last frame, as a Knob does.</summary>
        private void TurnDial()
        {
            Quaternion current = freeHand.transform.rotation;
            float twist = Knob.TwistDegrees(current * Quaternion.Inverse(dragLastRotation), handles[Dial].up);
            dragLastRotation = current;
            blending.Smoothing += twist / DialSweep * 2f * WorldElement.SmoothingRange;
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
            blending = element != null ? element.GetComponent<WorldElement>() : null;
            dragged = -1;

            // Not taken by a grip that was already down when the cubes appeared.
            wasPressed = true;

            for (int i = 0; i < handles.Length; i++)
                if (handles[i] != null) handles[i].gameObject.SetActive(element != null && (i != Dial || blending != null));
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
            for (int i = 0; i < Dial; i++)
                handles[i].SetPositionAndRotation(t.position + t.rotation * HandleLocal(i, element.Size * Scale), t.rotation);

            if (blending == null) return;

            // The dial faces the viewer, 12 o'clock up, whichever way the primitive is turned.
            Vector3 position = t.position + t.rotation * HandleLocal(Dial, element.Size * Scale);
            var cam = Camera.main;
            Vector3 toViewer = cam != null ? cam.transform.position - position : -t.forward;
            if (toViewer.sqrMagnitude < 1e-6f) toViewer = Vector3.back;
            toViewer.Normalize();
            Vector3 twelve = Vector3.ProjectOnPlane(Vector3.up, toViewer);
            if (twelve.sqrMagnitude < 1e-4f) twelve = Vector3.ProjectOnPlane(Vector3.forward, toViewer);
            handles[Dial].SetPositionAndRotation(position, Quaternion.LookRotation(twelve, toViewer));
            dialLabel.transform.rotation = Quaternion.LookRotation(-toViewer, twelve);
        }

        // A primitive taken out of a designed world that has been made bigger or
        // smaller is scaled as a whole, on top of its Size. 1 everywhere else.
        private float Scale => element.transform.lossyScale.x;

        /// <summary>Where a cube sits, in the primitive's own axes, for a primitive this big in the scene.</summary>
        private Vector3 HandleLocal(int index, Vector3 size)
        {
            if (index == Corner) return Vector3.Scale(signs, size * 0.5f + Vector3.one * (gap * 0.5f));

            // Out past the edge between the side cube and the top cube, level with
            // the middle front to back. It has to be on the free hand's side on
            // every axis it is off-center on: the cubes change sides to wherever
            // that hand is, so a dial on the far side of any axis would jump
            // away from the hand that reaches for it.
            if (index == Dial) return new Vector3(signs.x * (size.x * 0.5f + gap * 2f), signs.y * (size.y * 0.5f + gap * 2f), 0f);

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
                if (!handles[i].gameObject.activeSelf) continue;
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
