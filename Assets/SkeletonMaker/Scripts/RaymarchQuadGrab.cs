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
    ///
    /// Let go of it at your head and it sticks there, as the head view: the
    /// quad rides just in front of the eyes, big enough to fill both of them,
    /// so everything you look at is raymarched. That is the same window as
    /// before, only worn like glasses - no extra pass, and a pixel that looks
    /// at no shape still costs next to nothing. It draws only the shapes
    /// then, so the table, the dials and the line skeletons stay in view.
    /// Squeeze the left grip next to your head to take it off again: it comes
    /// back into the hand, as it was held when it went on.
    ///
    /// In the stages listed under Head Stages (Join, Together Math) it goes onto the head by
    /// itself when the stage begins, and back to where it stood when it ends.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class RaymarchQuadGrab : MonoBehaviour
    {
        [SerializeField] private InputActionReference leftGripAction;
        [SerializeField] private Transform leftController;
        [SerializeField] private float grabRadius = 0.2f;

        [Header("Head view")]
        [Tooltip("The headset camera. Camera.main if left empty.")]
        [SerializeField] private Transform head;
        [Tooltip("How close (meters) to the head the hand must be for letting go to put the quad on, and for the grip to take it off again.")]
        [SerializeField] private float headRadius = 0.25f;
        [Tooltip("How far (meters) in front of the eyes the quad rides. Anything nearer than this is not raymarched over.")]
        [SerializeField] private float headDistance = 0.15f;
        [Tooltip("The quad's width and height (meters) while on the head: enough to fill both eyes at Head Distance.")]
        [SerializeField] private float headSize = 1.2f;
        [Tooltip("Draw only the shapes while on the head, so the rest of the scene stays visible around them.")]
        [SerializeField] private bool headClipsBackground = true;
        [Tooltip("While on the head every pixel is a raymarched one, so shadows and occlusion - most of the cost of a pixel that hits a shape - are left out, and rays are cut off at Head Max Steps.")]
        [SerializeField] private bool headCheapShading = true;
        [Range(8, 128)]
        [SerializeField] private int headMaxSteps = 48;
        [Tooltip("The stages that begin with the quad on the head. It can still be taken off there.")]
        [SerializeField] private Stage[] headStages = { Stage.Join, Stage.TogetherMath };

        private Collider col;
        private RaymarchQuad quad;
        private bool held;
        private bool inHeadStage;

        // Where the quad stood before it was last picked up: where it goes
        // back to if it's switched off while on the head.
        private Vector3 freePosition;
        private Quaternion freeRotation;
        private Vector3 freeScale;

        // How it sat in the hand when it went onto the head.
        private Vector3 handPosition;
        private Quaternion handRotation;
        private Vector3 handScale;

        private bool savedClip;
        private int savedSteps;
        private float savedShadow, savedOcclusion;

        /// <summary>True while the quad is worn as the head view.</summary>
        public bool OnHead { get; private set; }

        private Transform Head
        {
            get
            {
                if (head == null && Camera.main != null) head = Camera.main.transform;
                return head;
            }
        }

        private void Awake()
        {
            col = GetComponent<Collider>();
            quad = GetComponent<RaymarchQuad>();
        }

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

            if (OnHead) LeaveHead(false);
            else Release();
            inHeadStage = false;
        }

        // Looked at here rather than when the stage changes: the quad itself is
        // being switched on by that change, and can't be moved under the head yet.
        private void Update()
        {
            var stages = StageController.Instance;
            bool headStage = stages != null && System.Array.IndexOf(headStages, stages.Current) >= 0;
            if (headStage == inHeadStage) return;
            inHeadStage = headStage;

            if (headStage) EnterHead();
            else LeaveHead(false);
        }

        private void OnGripPerformed(InputAction.CallbackContext ctx)
        {
            if (held || leftController == null) return;

            if (OnHead)
            {
                if (NearHead()) LeaveHead(true);
                return;
            }

            float dist = Vector3.Distance(leftController.position, col.ClosestPoint(leftController.position));
            if (dist > grabRadius) return;

            freePosition = transform.position;
            freeRotation = transform.rotation;
            freeScale = transform.localScale;
            held = true;
            transform.SetParent(leftController, true);
        }

        private void OnGripCanceled(InputAction.CallbackContext ctx)
        {
            if (held && NearHead()) EnterHead();
            else Release();
        }

        private void Release()
        {
            if (!held) return;
            held = false;
            transform.SetParent(null, true);
        }

        private bool NearHead() =>
            leftController != null && Head != null &&
            Vector3.Distance(leftController.position, Head.position) <= headRadius;

        /// <summary>Puts the quad on the head, from wherever it is.</summary>
        public void EnterHead()
        {
            if (OnHead || Head == null) return;

            if (held)
            {
                handPosition = transform.localPosition;
                handRotation = transform.localRotation;
                handScale = transform.localScale;
            }
            else
            {
                // Not carried there: when it's taken off it has to appear in the
                // hand somehow - upright, a forearm's length ahead of it.
                freePosition = transform.position;
                freeRotation = transform.rotation;
                freeScale = transform.localScale;
                handPosition = new Vector3(0f, 0f, 0.3f);
                handRotation = Quaternion.identity;
                handScale = freeScale;
            }
            held = false;
            OnHead = true;

            transform.SetParent(Head, false);
            transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, headDistance), Quaternion.identity);
            Vector3 headScale = Head.lossyScale;
            transform.localScale = new Vector3(headSize / headScale.x, headSize / headScale.y, 1f);

            if (quad == null) return;
            savedClip = quad.clipBackground;
            savedSteps = quad.maxSteps;
            savedShadow = quad.shadowStrength;
            savedOcclusion = quad.occlusionStrength;
            if (headClipsBackground) quad.clipBackground = true;
            if (headCheapShading)
            {
                quad.maxSteps = Mathf.Min(quad.maxSteps, headMaxSteps);
                quad.shadowStrength = 0f;
                quad.occlusionStrength = 0f;
            }
        }

        /// <summary>Takes the quad off the head: into the left hand, as it was
        /// held when it went on, or back to where it stood before that.</summary>
        public void LeaveHead(bool intoHand)
        {
            if (!OnHead) return;
            OnHead = false;

            if (quad != null)
            {
                if (headClipsBackground) quad.clipBackground = savedClip;
                if (headCheapShading)
                {
                    quad.maxSteps = savedSteps;
                    quad.shadowStrength = savedShadow;
                    quad.occlusionStrength = savedOcclusion;
                }
            }

            if (intoHand && leftController != null)
            {
                held = true;
                transform.SetParent(leftController, false);
                transform.SetLocalPositionAndRotation(handPosition, handRotation);
                transform.localScale = handScale;
            }
            else
            {
                transform.SetParent(null, false);
                transform.SetPositionAndRotation(freePosition, freeRotation);
                transform.localScale = freeScale;
            }
        }
    }
}
