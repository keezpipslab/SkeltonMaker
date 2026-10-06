using System;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Takes the shapes lying about - on the table, or dropped and on their
    /// way back to it - out of the stages listed here, and brings them back
    /// afterwards. They are spawned at runtime and belong to no object, so
    /// StageController's lists can't switch them off the way they do the
    /// table. A shape placed on a skeleton or held in a hand is not loose.
    /// </summary>
    public class LooseShapes : MonoBehaviour
    {
        [Tooltip("The stages without loose shapes.")]
        [SerializeField] private Stage[] hiddenIn = { Stage.Join };

        private StageController stages;
        private int pending;

        private void Start()
        {
            stages = StageController.Instance;
            if (stages == null) return;
            stages.Changed += OnStageChanged;
            pending = 2;
        }

        private void OnDestroy()
        {
            if (stages != null) stages.Changed -= OnStageChanged;
        }

        // Again a frame later: a table slot that was just switched on spawns its shape in its Start.
        private void OnStageChanged(Stage stage) => pending = 2;

        private void LateUpdate()
        {
            if (pending <= 0) return;
            pending--;

            bool show = Array.IndexOf(hiddenIn, stages.Current) < 0;
            foreach (var element in FindObjectsByType<RaymarchableElement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (element.transform.parent != null) continue;
                if (element.gameObject.activeSelf != show) element.gameObject.SetActive(show);
            }
        }
    }
}
