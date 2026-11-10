using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Small words on the controllers by the buttons of the designer scene,
    /// saying what each will do: while a primitive (WorldElement) is in a
    /// hand, B "Delete" and A "Copy" on the right controller, and Y
    /// "Subtract" or "Add" on the left - whichever the primitive would be
    /// switched to. Hidden while nothing is held, when the buttons do nothing.
    ///
    /// The words are children of the controllers and turn with them. To place
    /// them: in Play, hold a primitive, select "A Label" (and the others)
    /// under the controller and move and turn it onto its button. The pose
    /// is kept here as it changes, so afterwards Copy Component on this,
    /// stop Play, and Paste Component Values.
    /// </summary>
    public class ControllerButtonLabels : MonoBehaviour
    {
        [System.Serializable]
        private class Place
        {
            public Vector3 position;
            public Vector3 rotation;
            [System.NonSerialized] public TextMesh label;
            [System.NonSerialized] public Vector3 appliedPosition, appliedRotation;
        }

        // In the controller's own axes. Lying on the top, read from above.
        [SerializeField] private Place a = new Place { position = new Vector3(0f, 0.02f, 0f), rotation = new Vector3(90f, 0f, 0f) };
        [SerializeField] private Place b = new Place { position = new Vector3(0f, 0.02f, 0.02f), rotation = new Vector3(90f, 0f, 0f) };
        [SerializeField] private Place y = new Place { position = new Vector3(0f, 0.02f, 0.02f), rotation = new Vector3(90f, 0f, 0f) };
        [SerializeField] private float characterSize = 0.0025f;
        [SerializeField] private Color color = new Color(1f, 1f, 1f, 0.9f);

        private HandGrabber left, right;

        private void Update()
        {
            // Until both are found: a controller that isn't tracked yet when the
            // scene starts comes in a little later.
            if (left == null || right == null)
            {
                foreach (var hand in FindObjectsByType<HandGrabber>(FindObjectsSortMode.None))
                    if (hand.IsLeft) left = hand;
                    else right = hand;

                if (right != null && a.label == null) a.label = Label("A Label", "Copy", right, a);
                if (right != null && b.label == null) b.label = Label("B Label", "Delete", right, b);
                if (left != null && y.label == null) y.label = Label("Y Label", "Subtract", left, y);
            }

            var held = Held();
            Show(a, held != null);
            Show(b, held != null);
            Show(y, held != null);
            if (held != null && y.label != null) y.label.text = held.Subtract ? "Add" : "Subtract";
        }

        /// <summary>The primitive in a hand, the one the buttons act on. Null when there is none.</summary>
        private WorldElement Held()
        {
            foreach (var hand in new[] { right, left })
                if (hand != null && hand.Held != null && hand.Held.TryGetComponent(out WorldElement element)) return element;
            return null;
        }

        private void Show(Place place, bool shown)
        {
            if (place.label == null) return;
            place.label.gameObject.SetActive(shown);
            place.label.characterSize = characterSize;
            place.label.color = color;

            // Whichever was changed last wins: the numbers here, or the label
            // moved in the scene.
            Transform t = place.label.transform;
            if (place.position != place.appliedPosition || place.rotation != place.appliedRotation)
            {
                Apply(place);
            }
            else if (t.localPosition != place.appliedPosition || t.localEulerAngles != place.appliedRotation)
            {
                place.position = place.appliedPosition = t.localPosition;
                place.rotation = place.appliedRotation = t.localEulerAngles;
            }
        }

        private static void Apply(Place place)
        {
            Transform t = place.label.transform;
            t.localPosition = place.position;
            t.localEulerAngles = place.rotation;
            place.appliedPosition = t.localPosition;
            place.appliedRotation = t.localEulerAngles; // as Unity reads it back, so it isn't taken for a move
        }

        private TextMesh Label(string name, string text, HandGrabber hand, Place place)
        {
            var go = new GameObject(name);
            go.transform.SetParent(hand.transform, false);
            var label = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            label.text = text;
            label.fontSize = 64;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            place.label = label;
            Apply(place);
            go.SetActive(false);
            return label;
        }
    }
}
