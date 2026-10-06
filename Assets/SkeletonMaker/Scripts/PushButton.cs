using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// A flat cube the size of a dial that does one thing when pressed - what,
    /// is up to the subclass (MirrorToggle, FinishedButton, ModeButton). It is
    /// pressed with the trigger: pull a hand's trigger while that hand is at
    /// the cube (the grip is for picking things up and turning the dials).
    /// The cube sinks in for as long as the trigger is held, and is lit while
    /// Lit says so, which is how a toggle shows that it's on. Its caption is
    /// shown in a text below it.
    ///
    /// Orientation as for a Knob: this object's up axis points at the viewer.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public abstract class PushButton : MonoBehaviour
    {
        [Serializable]
        public class Hand
        {
            public InputActionReference trigger;
            public Transform controller;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Hand[] hands;

        [Tooltip("The cube itself. Sinks along its local Y while pressed.")]
        [SerializeField] private Transform cap;
        [SerializeField] private TextMesh label;
        [SerializeField] private float labelDrop = 0.085f;

        [SerializeField] private Color litColor = new Color(1f, 0.75f, 0.2f);
        [Tooltip("How far (meters) the cube sinks in while pressed.")]
        [SerializeField] private float travel = 0.008f;
        [Tooltip("How close (meters) the hand must be to the cube for the trigger to press it.")]
        [SerializeField] private float pressRadius = 0.08f;

        private Collider col;
        private Renderer capRenderer;
        private MaterialPropertyBlock block;
        private Vector3 capRest;
        private int presser = -1;
        private bool? shownLit;
        private Action<InputAction.CallbackContext>[] performed;
        private Action<InputAction.CallbackContext>[] canceled;

        /// <summary>What a press does.</summary>
        protected abstract void Press();

        /// <summary>Whether the cube is lit just now.</summary>
        protected virtual bool Lit => false;

        /// <summary>What the text below the cube says.</summary>
        protected abstract string Caption { get; }

        private void Awake()
        {
            col = GetComponent<Collider>();
            if (cap != null)
            {
                capRest = cap.localPosition;
                capRenderer = cap.GetComponent<Renderer>();
            }
        }

        private void OnEnable()
        {
            if (hands == null) return;
            performed = new Action<InputAction.CallbackContext>[hands.Length];
            canceled = new Action<InputAction.CallbackContext>[hands.Length];
            for (int i = 0; i < hands.Length; i++)
            {
                if (hands[i].trigger == null) continue;
                int index = i;
                performed[i] = _ => OnTrigger(index);
                canceled[i] = _ => OnLetGo(index);
                hands[i].trigger.action.Enable();
                hands[i].trigger.action.performed += performed[i];
                hands[i].trigger.action.canceled += canceled[i];
            }
        }

        private void OnDisable()
        {
            if (hands != null)
            {
                for (int i = 0; i < hands.Length && performed != null; i++)
                {
                    if (hands[i].trigger == null || performed[i] == null) continue;
                    hands[i].trigger.action.performed -= performed[i];
                    hands[i].trigger.action.canceled -= canceled[i];
                }
            }
            presser = -1;
            if (cap != null) cap.localPosition = capRest;
        }

        private void OnTrigger(int index)
        {
            if (presser >= 0 || hands[index].controller == null) return;
            if (StageCalibrator.IsCapturing) return; // the trigger is marking a floor point just now

            Vector3 hand = hands[index].controller.position;
            if (Vector3.Distance(hand, col.ClosestPoint(hand)) > pressRadius) return;

            presser = index;
            Press();
        }

        private void OnLetGo(int index)
        {
            if (presser == index) presser = -1;
        }

        private void Update()
        {
            if (cap != null) cap.localPosition = capRest + Vector3.down * (presser >= 0 ? travel : 0f);

            bool lit = Lit;
            if (capRenderer != null && lit != shownLit)
            {
                shownLit = lit;
                if (lit)
                {
                    block ??= new MaterialPropertyBlock();
                    block.SetColor(BaseColorId, litColor);
                    capRenderer.SetPropertyBlock(block);
                }
                else
                {
                    capRenderer.SetPropertyBlock(null);
                }
            }

            if (label != null) label.text = Caption;
        }

        private void LateUpdate()
        {
            if (label == null) return;

            // Hanging below the cube, readable from wherever the viewer is.
            label.transform.position = transform.position + Vector3.down * labelDrop;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 away = label.transform.position - cam.transform.position;
                if (away.sqrMagnitude > 1e-6f) label.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }
    }
}
