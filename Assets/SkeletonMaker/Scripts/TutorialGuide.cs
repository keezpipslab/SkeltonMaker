using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The tutorial stage: a floating text that walks through the controls one
    /// at a time - pick a shape up, scale it, reshape it, color it, place it,
    /// look at it through the raymarch window, move the window, wear it on the
    /// head and take it off again - moving on as soon as the
    /// player has actually done each one, then hands over to the Build stage.
    /// StageController's "next stage" button (B, or Enter) skips it.
    ///
    /// Each new thing is switched on when its step starts: the color baths,
    /// then a one-bone practice stick (a SkeletonRig of its own, so placing
    /// works exactly as on the skeleton), then the window. The guide only
    /// watches the outcome of each step (what's in a hand, its Size and
    /// color, how many shapes are on the stick, whether the window has moved) and never talks to the things
    /// it reveals, so those can change without touching this.
    /// </summary>
    public class TutorialGuide : MonoBehaviour
    {
        private enum Step { Grab, Scale, Reshape, Color, Place, Window, MoveWindow, HeadOn, HeadOff, Done }

        private static readonly string[] Instructions =
        {
            "Reach into a shape on the table\nand squeeze the grip to pick it up.",
            "Keep holding it.\nSmall cubes float around it.\nReach for the red one with your other hand,\nsqueeze the grip and pull:\nthe shape gets bigger or smaller.",
            "Now change its shape.\nTake one of the white cubes\nthe same way and pull:\neach one stretches the shape\nin its own direction.",
            "Give it a color.\nDip it into one of the colored discs\nbeside the table.",
            "Carry it to the upright stick\nbehind the table. When the stick\nturns green, let go to leave it there.\nDropped anywhere else, a shape\nfinds its way back to the table.",
            "This window shows your shapes\nas one smooth surface.\nPut a second shape on the stick,\nclose to the first, and watch them merge.",
            "You can move the window.\nReach into it with your left hand\nand squeeze the grip to pick it up.",
            "You can wear the window too.\nWith your left hand, bring it\nup to your head and let go:\nit stays on like a pair of glasses.",
            "Now everything you look at\nis seen through the window.\nTo take it off, squeeze the left grip\nright next to your head\nand put the window down again.",
            "That's all you need.\nNow build a skeleton.",
        };

        [SerializeField] private TextMesh label;

        [Header("Revealed step by step")]
        [Tooltip("Switched on when the color step starts (the color baths).")]
        [SerializeField] private GameObject[] colorObjects;
        [Tooltip("The practice stick, switched on when the place step starts.")]
        [SerializeField] private SkeletonRig stick;
        [Tooltip("Switched on when the window step starts (the raymarch quad).")]
        [SerializeField] private GameObject[] windowObjects;

        [Header("When a step counts as done")]
        [Tooltip("How much every side has to grow or shrink (as a fraction) for the scale step.")]
        [SerializeField] private float scaleChange = 0.12f;
        [Tooltip("How much one side has to change against another (as a fraction) for the reshape step.")]
        [SerializeField] private float reshapeChange = 0.15f;
        [Tooltip("How long the closing text stays up before the Build stage starts.")]
        [SerializeField] private float finishDelay = 3f;

        private Step step;
        private RaymarchableElement held;
        private Vector3 baseline; // the held element's size when this step (or this hold) began
        private float doneAt;

        // Where the window objects were when the move step began.
        private Transform[] windowParents;
        private Vector3[] windowPositions;
        private const float WindowMoved = 0.05f; // meters

        // The window's own grab, for whether it is being worn on the head.
        // None among the window objects: those two steps are passed over.
        private RaymarchQuadGrab windowGrab;

        private void OnEnable()
        {
            // The stick is part of the tutorial, so it starts out switched on
            // along with it; the rest belongs to the Build stage and is off.
            if (stick != null) stick.gameObject.SetActive(false);

            held = null;
            SetStep(Step.Grab);
        }

        private void OnDisable()
        {
            // The practice shapes go with the tutorial, rather than lingering
            // switched off on the stick.
            if (stick != null) stick.RemovePlacedElements();
        }

        private void Update()
        {
            var nowHeld = Grabbable.Held.Count > 0 ? Grabbable.Held[0].GetComponent<RaymarchableElement>() : null;
            if (nowHeld != held)
            {
                held = nowHeld;
                if (held != null) baseline = held.Size;
                Refresh();
            }

            switch (step)
            {
                case Step.Grab:
                    if (held != null) SetStep(Step.Scale);
                    break;
                case Step.Scale:
                    if (held != null && SmallestChange(held.Size) >= scaleChange) SetStep(Step.Reshape);
                    break;
                case Step.Reshape:
                    if (held != null && ShapeChange(held.Size) >= reshapeChange) SetStep(Step.Color);
                    break;
                case Step.Color:
                    if (held != null && held.TryGetComponent(out ElementColor _)) SetStep(Step.Place);
                    break;
                case Step.Place:
                    if (PlacedOnStick() >= 1) SetStep(Step.Window);
                    break;
                case Step.Window:
                    if (PlacedOnStick() >= 2) SetStep(Step.MoveWindow);
                    break;
                case Step.MoveWindow:
                    if (WindowPickedUp()) SetStep(Step.HeadOn);
                    break;
                case Step.HeadOn:
                    if (windowGrab == null || windowGrab.OnHead) SetStep(Step.HeadOff);
                    break;
                case Step.HeadOff:
                    if (windowGrab == null || !windowGrab.OnHead) SetStep(Step.Done);
                    break;
                case Step.Done:
                    if (Time.time >= doneAt) Finish();
                    break;
            }
        }

        private void LateUpdate()
        {
            if (label == null) return;

            // Readable from wherever the viewer is.
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 away = label.transform.position - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-6f) label.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        private void SetStep(Step next)
        {
            step = next;
            if (held != null) baseline = held.Size;

            if (step == Step.Color) Show(colorObjects);
            if (step == Step.Place && stick != null) stick.gameObject.SetActive(true);
            if (step == Step.Window) Show(windowObjects);
            if (step == Step.MoveWindow) RememberWindow();
            if (step == Step.Done) doneAt = Time.time + finishDelay;

            Refresh();
        }

        private static void Show(GameObject[] objects)
        {
            if (objects == null) return;
            foreach (var go in objects)
            {
                if (go != null) go.SetActive(true);
            }
        }

        private void Refresh()
        {
            if (label == null) return;

            if (step == Step.Done)
            {
                label.text = Instructions[(int)step];
                return;
            }

            // These steps only work on a shape that's in a hand.
            bool needsHeld = step == Step.Scale || step == Step.Reshape || step == Step.Color;
            string text = needsHeld && held == null ? "Pick a shape up again first." : Instructions[(int)step];
            label.text = $"{(int)step + 1} / {(int)Step.Done}\n\n{text}\n\n(B skips the tutorial)";
        }

        private void RememberWindow()
        {
            int count = windowObjects != null ? windowObjects.Length : 0;
            windowParents = new Transform[count];
            windowPositions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                if (windowObjects[i] == null) continue;
                windowParents[i] = windowObjects[i].transform.parent;
                windowPositions[i] = windowObjects[i].transform.position;
                if (windowGrab == null) windowGrab = windowObjects[i].GetComponentInChildren<RaymarchQuadGrab>(true);
            }
        }

        // Taken into a hand (it hangs off something else now) or simply
        // somewhere else than it was - however the window ends up being moved.
        private bool WindowPickedUp()
        {
            if (windowPositions.Length == 0) return true; // no window to move: nothing to wait for

            for (int i = 0; i < windowPositions.Length; i++)
            {
                if (windowObjects[i] == null) continue;
                Transform t = windowObjects[i].transform;
                if (t.parent != windowParents[i]) return true;
                if (Vector3.Distance(t.position, windowPositions[i]) >= WindowMoved) return true;
            }
            return false;
        }

        private int PlacedOnStick() => stick != null && stick.isActiveAndEnabled ? stick.PlacedElementCount() : 0;

        private static void Finish()
        {
            if (StageController.Instance != null) StageController.Instance.Go(Stage.Build);
        }

        // Uniform scaling is the only control that changes all three sides at
        // once, so "the side that changed least" tells it apart from reshaping.
        private float SmallestChange(Vector3 size) => Mathf.Min(
            Mathf.Abs(size.x / baseline.x - 1f),
            Mathf.Abs(size.y / baseline.y - 1f),
            Mathf.Abs(size.z / baseline.z - 1f));

        // And reshaping is any one side changing against another, whichever
        // white cube was pulled: scaling keeps all three in step.
        private float ShapeChange(Vector3 size)
        {
            float x = size.x / baseline.x, y = size.y / baseline.y, z = size.z / baseline.z;
            return Mathf.Max(Mathf.Abs(x / y - 1f), Mathf.Abs(y / z - 1f), Mathf.Abs(z / x - 1f));
        }
    }
}
