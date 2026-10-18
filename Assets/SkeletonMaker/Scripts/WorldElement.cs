using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// A primitive of the designer scene: it is placed wherever it is let go
    /// of, with no bone to land on - it simply becomes a child of the
    /// DesignWorld. Takes the place of SkeletonPlacement on the designer's
    /// own element prefabs.
    ///
    /// While it is in a hand: B (right controller) deletes it, A (right
    /// controller) leaves a copy where it is just now, and Y (left
    /// controller) switches it between adding to the world and being carved
    /// out of it. A carving primitive's mesh has the Subtract Color.
    ///
    /// Each has a smoothing of its own, which comes on top of the raymarch
    /// quad's: how much further (or less far, below zero) this primitive
    /// blends into the others than they all do. Two primitives meet over the
    /// quad's smoothing plus the own smoothing of each of the two. The small
    /// dial that floats by a held primitive sets it (SizeHandles).
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class WorldElement : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Every primitive there is, the ones still on the panel included.</summary>
        public static readonly List<WorldElement> All = new List<WorldElement>();

        /// <summary>While set, the meshes of the placed primitives are not drawn
        /// (PreviewHideToggle), leaving only what the raymarch quad shows of them.</summary>
        public static bool PreviewHidden;

        /// <summary>How far either side of zero Smoothing goes (meters): the range of
        /// the quad's own smoothing, so all the way down this primitive has a
        /// hard edge against any other that adds nothing itself.</summary>
        public const float SmoothingRange = 0.3f;

        [SerializeField] private bool subtract;
        [Range(-SmoothingRange, SmoothingRange)]
        [Tooltip("Added to the raymarch quad's smoothing for this primitive alone (meters).")]
        [SerializeField] private float smoothing;
        [SerializeField] private Color subtractColor = new Color(0.95f, 0.2f, 0.15f);

        private Grabbable grabbable;
        private MeshRenderer visual;
        private MaterialPropertyBlock block;

        // Built here rather than wired in the scene, as HandGrabber's is: the
        // buttons belong to whichever primitive is in a hand, not to a hand.
        private static InputAction deleteAction, duplicateAction, subtractAction;

        /// <summary>Carved out of the other primitives instead of added to them.</summary>
        public bool Subtract
        {
            get => subtract;
            set
            {
                subtract = value;
                ApplyTint();
            }
        }

        /// <summary>This primitive's own smoothing, on top of everyone's.</summary>
        public float Smoothing
        {
            get => smoothing;
            set => smoothing = Mathf.Clamp(value, -SmoothingRange, SmoothingRange);
        }

        /// <summary>True once it has been let go of in the world (not on the panel, not in a hand).</summary>
        public bool Placed => DesignWorld.Instance != null && transform.parent == DesignWorld.Instance.transform;

        /// <summary>Leaves this primitive where it is and returns a fresh copy of
        /// it in the same pose, for the hand to take instead.</summary>
        public Grabbable DuplicateForGrab() => Copy(transform.parent).GetComponent<Grabbable>();

        private void Awake() => grabbable = GetComponent<Grabbable>();

        private void OnEnable()
        {
            All.Add(this);
            grabbable.Released += OnReleased;

            deleteAction ??= Button("Delete", "<XRController>{RightHand}/{SecondaryButton}");
            duplicateAction ??= Button("Duplicate", "<XRController>{RightHand}/{PrimaryButton}");
            subtractAction ??= Button("Subtract", "<XRController>{LeftHand}/{SecondaryButton}");
            deleteAction.Enable();
            duplicateAction.Enable();
            subtractAction.Enable();
        }

        private void OnDisable()
        {
            All.Remove(this);
            grabbable.Released -= OnReleased;
        }

        private void Start() => ApplyTint(); // a copy of a carving primitive starts out as one

        private void Update()
        {
            if (!grabbable.IsHeld) return;

            if (deleteAction.WasPressedThisFrame())
            {
                Destroy(gameObject);
                return;
            }

            if (duplicateAction.WasPressedThisFrame() && DesignWorld.Instance != null) Copy(DesignWorld.Instance.transform);
            if (subtractAction.WasPressedThisFrame()) Subtract = !subtract;
        }

        private void LateUpdate()
        {
            var renderer = Visual();
            if (renderer == null) return;

            bool show = !(PreviewHidden && Placed);
            if (renderer.enabled != show) renderer.enabled = show;
        }

        private void OnReleased()
        {
            if (DesignWorld.Instance != null) transform.SetParent(DesignWorld.Instance.transform, true);
        }

        private WorldElement Copy(Transform parent)
        {
            var copy = Instantiate(gameObject, transform.position, transform.rotation, parent).GetComponent<WorldElement>();
            copy.name = name;

            // As big as this one, also when the world it goes into isn't the size the hand is.
            float parentScale = parent != null ? parent.lossyScale.x : 1f;
            copy.transform.localScale = transform.lossyScale / parentScale;
            return copy;
        }

        private void ApplyTint()
        {
            var renderer = Visual();
            if (renderer == null) return;

            if (!subtract)
            {
                renderer.SetPropertyBlock(null);
                return;
            }

            // The tint keeps the material's own transparency, as ElementColor's does.
            Color tint = subtractColor;
            var material = renderer.sharedMaterial;
            if (material != null && material.HasProperty(BaseColorId)) tint.a = material.GetColor(BaseColorId).a;

            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColorId, tint);
            renderer.SetPropertyBlock(block);
        }

        private MeshRenderer Visual()
        {
            if (visual == null)
            {
                var child = transform.Find("Visual");
                if (child != null) visual = child.GetComponent<MeshRenderer>();
            }
            return visual;
        }

        private static InputAction Button(string name, string binding)
        {
            var action = new InputAction(name, InputActionType.Button);
            action.AddBinding(binding);
            return action;
        }
    }
}
