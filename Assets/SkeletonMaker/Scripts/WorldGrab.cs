using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Takes hold of the whole designed world with both hands: squeeze both
    /// grips with nothing in either hand and the World object follows the two
    /// controllers. Moving them apart makes it bigger and together smaller,
    /// moving them both moves it, and turning them about each other turns it
    /// about the vertical, so it stays level. The point between the hands
    /// stays between the hands.
    ///
    /// A grip that took something else doesn't count: a primitive, a dial,
    /// the panel's handle, the raymarch quad. Both grips have to be squeezed
    /// in thin air. One of these on the World object is enough; it finds the
    /// hands (HandGrabber) itself.
    /// </summary>
    public class WorldGrab : MonoBehaviour
    {
        [Tooltip("The smallest and largest the world can be made, as a factor of how it was built.")]
        [SerializeField] private float minScale = 0.02f;
        [SerializeField] private float maxScale = 50f;
        [Tooltip("Hands closer together than this (meters) hold the world still: there is no telling how far apart they moved.")]
        [SerializeField] private float minSpan = 0.03f;

        private HandGrabber[] hands;
        private RaymarchQuadGrab quadGrab;
        private bool holding;
        private bool wasBoth;
        private Vector3 lastA, lastB;

        private void OnDisable() => holding = false;

        private void Update()
        {
            if (hands == null || hands.Length != 2)
            {
                hands = FindObjectsByType<HandGrabber>(FindObjectsSortMode.None);
                if (hands.Length != 2) return;
            }

            bool both = hands[0].GripPressed && hands[1].GripPressed;
            if (!both)
            {
                holding = false;
            }
            else if (holding)
            {
                Follow(hands[0].transform.position, hands[1].transform.position);
            }
            else if (!wasBoth && Free(hands[0]) && Free(hands[1]))
            {
                // Only as the second grip goes down: not when a hand lets go of
                // a dial while the other happens to be squeezed.
                holding = true;
                lastA = hands[0].transform.position;
                lastB = hands[1].transform.position;
            }
            wasBoth = both;
        }

        /// <summary>True if this hand's grip took nothing when it was squeezed.</summary>
        private bool Free(HandGrabber hand)
        {
            if (hand.Held != null) return false;
            if (Knob.Turning.Contains(hand.transform) || PanelGrab.Holding.Contains(hand.transform)) return false;

            // The quad is the left hand's alone.
            if (quadGrab == null) quadGrab = FindFirstObjectByType<RaymarchQuadGrab>();
            return quadGrab == null || !quadGrab.InHand || !hand.name.Contains("Left");
        }

        private void Follow(Vector3 a, Vector3 b)
        {
            Vector3 was = lastB - lastA, now = b - a;
            Vector3 pivotWas = (lastA + lastB) * 0.5f, pivotNow = (a + b) * 0.5f;
            lastA = a;
            lastB = b;

            float factor = 1f;
            if (was.magnitude > minSpan && now.magnitude > minSpan)
            {
                float current = transform.localScale.x;
                factor = Mathf.Clamp(current * now.magnitude / was.magnitude, minScale, maxScale) / current;
            }

            // Only the turn about the vertical; hands one above the other have none.
            Quaternion turn = Quaternion.identity;
            Vector3 flatWas = Vector3.ProjectOnPlane(was, Vector3.up), flatNow = Vector3.ProjectOnPlane(now, Vector3.up);
            if (flatWas.magnitude > minSpan && flatNow.magnitude > minSpan)
                turn = Quaternion.AngleAxis(Vector3.SignedAngle(flatWas, flatNow, Vector3.up), Vector3.up);

            transform.SetPositionAndRotation(pivotNow + turn * ((transform.position - pivotWas) * factor), turn * transform.rotation);
            transform.localScale *= factor;
        }
    }
}
