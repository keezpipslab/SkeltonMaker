using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    public enum Stage
    {
        Tutorial,
        Build,
        Math,
    }

    /// <summary>
    /// Splits the one scene into stages by switching whole objects on and
    /// off: an object listed under a stage is active only while one of the
    /// stages listing it is current, and anything listed nowhere (the XR rig,
    /// the table, the managers) is always there. The tutorial is just the
    /// builder with most of it switched off, and the math stage the builder
    /// with a few dials added, so all three run on the same rig and controls,
    /// and moving on is instant.
    ///
    /// B on the right controller (or Enter) moves on: out of the tutorial
    /// into Build, then back and forth between Build and Math.
    /// </summary>
    [DefaultExecutionOrder(-100)] // before anything it switches off gets to wake up
    public class StageController : MonoBehaviour
    {
        public static StageController Instance { get; private set; }

        [Tooltip("The stage Play starts in. Set to Build to skip the tutorial while working on the builder.")]
        [SerializeField] private Stage startStage = Stage.Tutorial;

        [SerializeField] private GameObject[] tutorialObjects;
        [SerializeField] private GameObject[] buildObjects;
        [SerializeField] private GameObject[] mathObjects;

        public Stage Current { get; private set; }

        public event Action<Stage> Changed;

        private InputAction nextAction;

        private void Awake()
        {
            Instance = this;
            Apply(startStage);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            if (nextAction == null)
            {
                nextAction = new InputAction("Next Stage", InputActionType.Button);
                nextAction.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
                nextAction.AddBinding("<Keyboard>/enter");
            }
            nextAction.Enable();
        }

        private void OnDisable() => nextAction.Disable();

        private void Update()
        {
            if (nextAction.WasPressedThisFrame()) Next();
        }

        /// <summary>Tutorial -> Build -> Math -> Build -> Math ... (staying in
        /// Build while no math stage has been set up).</summary>
        public void Next()
        {
            bool hasMath = mathObjects != null && mathObjects.Length > 0;
            Go(Current == Stage.Build && hasMath ? Stage.Math : Stage.Build);
        }

        public void Go(Stage stage)
        {
            if (stage == Current) return;
            Apply(stage);
            Changed?.Invoke(stage);
        }

        private void Apply(Stage stage)
        {
            Current = stage;
            var current = ObjectsOf(stage);

            // Off first, then on: an object that's in both the old and the
            // new stage's list stays on throughout.
            foreach (Stage other in Enum.GetValues(typeof(Stage)))
            {
                if (other == stage) continue;
                foreach (var go in ObjectsOf(other))
                {
                    if (go != null && Array.IndexOf(current, go) < 0) go.SetActive(false);
                }
            }

            foreach (var go in current)
            {
                if (go != null) go.SetActive(true);
            }
        }

        private GameObject[] ObjectsOf(Stage stage)
        {
            GameObject[] objects;
            switch (stage)
            {
                case Stage.Tutorial: objects = tutorialObjects; break;
                case Stage.Build: objects = buildObjects; break;
                default: objects = mathObjects; break;
            }
            return objects ?? Array.Empty<GameObject>();
        }
    }
}
