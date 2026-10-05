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
        Join,
        Record,
    }

    /// <summary>
    /// Splits the one scene into stages by switching whole objects on and
    /// off: an object listed under a stage is active only while one of the
    /// stages listing it is current, and anything listed nowhere (the XR rig,
    /// the table, the managers) is always there. The tutorial is just the
    /// builder with most of it switched off, and the math stage the builder
    /// with a few dials added, so all of them run on the same rig and controls,
    /// and moving on is instant.
    ///
    /// B on the right controller (or Enter) moves on: out of the tutorial
    /// into Build, then round Build, Math and Join (where the other performer
    /// appears). Record is hidden: H goes there from anywhere and back again,
    /// and "next" never passes through it.
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
        [SerializeField] private GameObject[] joinObjects;
        [SerializeField] private GameObject[] recordObjects;

        public Stage Current { get; private set; }

        // Where H came from, to go back to.
        private Stage beforeRecord = Stage.Build;

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

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame) ToggleRecord();
        }

        /// <summary>Tutorial -> Build -> Math -> Join -> Build ... (leaving out
        /// a stage that hasn't been set up), and out of Record back to where
        /// it was entered from.</summary>
        public void Next()
        {
            switch (Current)
            {
                case Stage.Record: Go(beforeRecord); break;
                case Stage.Build: Go(Has(mathObjects) ? Stage.Math : Has(joinObjects) ? Stage.Join : Stage.Build); break;
                case Stage.Math: Go(Has(joinObjects) ? Stage.Join : Stage.Build); break;
                default: Go(Stage.Build); break;
            }
        }

        /// <summary>Into the hidden Record stage, or back out of it.</summary>
        public void ToggleRecord()
        {
            if (Current == Stage.Record) Go(beforeRecord);
            else if (Has(recordObjects)) Go(Stage.Record);
        }

        private static bool Has(GameObject[] objects) => objects != null && objects.Length > 0;

        public void Go(Stage stage)
        {
            if (stage == Current) return;
            if (stage == Stage.Record) beforeRecord = Current;
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
                case Stage.Math: objects = mathObjects; break;
                case Stage.Join: objects = joinObjects; break;
                default: objects = recordObjects; break;
            }
            return objects ?? Array.Empty<GameObject>();
        }
    }
}
