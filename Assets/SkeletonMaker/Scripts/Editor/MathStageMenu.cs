using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Add Math Stage: builds the math stage's dials next to
    /// the smoothing knob - Inflate (d - c) and Repeat (mod p) - and makes the
    /// Math stage "everything in Build, plus the dials". Needs the raymarch
    /// quad, the smoothing knob and the Stages object to be there already.
    /// Replaces the dials a previous run added.
    /// </summary>
    public static class MathStageMenu
    {
        private const float Spacing = 0.2f;

        [MenuItem("SkeletonMaker/Add Math Stage")]
        public static void AddMathStage()
        {
            var quad = Object.FindFirstObjectByType<RaymarchQuad>(FindObjectsInactive.Include);
            var smoothing = Object.FindFirstObjectByType<SmoothingKnob>(FindObjectsInactive.Include);
            var controller = Object.FindFirstObjectByType<StageController>(FindObjectsInactive.Include);
            if (quad == null || smoothing == null || controller == null)
            {
                EditorUtility.DisplayDialog("Add Math Stage",
                    "This needs a RaymarchQuad, a Smoothing Knob and the Stages object in the open scene - run 'Add Raymarch Quad', 'Add Smoothing Knob' and 'Add Stages' first.", "OK");
                return;
            }

            foreach (var old in Object.FindObjectsByType<InflateKnob>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Undo.DestroyObjectImmediate(old.gameObject);
            foreach (var old in Object.FindObjectsByType<RepeatKnob>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Undo.DestroyObjectImmediate(old.gameObject);

            // In a row beside the smoothing knob, on its side away from the table.
            Transform first = smoothing.transform;
            Vector3 side = first.right;
            var table = GameObject.Find("Table");
            if (table != null && Vector3.Dot(side, first.position - table.transform.position) < 0f) side = -side;

            SmoothingKnobMenu.BuildKnob<InflateKnob>("Inflate Knob", first.position + side * Spacing, first.rotation, quad, -0.15f, 0.15f);
            var repeat = SmoothingKnobMenu.BuildKnob<RepeatKnob>("Repeat Knob", first.position + side * (Spacing * 2f), first.rotation, quad, 0f, 1f);

            WireStage(controller);

            Selection.activeGameObject = repeat.gameObject;
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        }

        /// <summary>Sets the Math stage's objects: everything the Build stage has,
        /// plus the math dials (if there are any yet).</summary>
        internal static void WireStage(StageController controller)
        {
            var dials = new List<GameObject>();
            foreach (var knob in Object.FindObjectsByType<InflateKnob>(FindObjectsInactive.Include, FindObjectsSortMode.None)) dials.Add(knob.gameObject);
            foreach (var knob in Object.FindObjectsByType<RepeatKnob>(FindObjectsInactive.Include, FindObjectsSortMode.None)) dials.Add(knob.gameObject);

            var so = new SerializedObject(controller);
            var math = so.FindProperty("mathObjects");
            if (dials.Count == 0)
            {
                math.arraySize = 0; // no math stage yet: the "next stage" button stays in Build
            }
            else
            {
                var build = so.FindProperty("buildObjects");
                math.arraySize = build.arraySize + dials.Count;
                for (int i = 0; i < build.arraySize; i++)
                    math.GetArrayElementAtIndex(i).objectReferenceValue = build.GetArrayElementAtIndex(i).objectReferenceValue;
                for (int i = 0; i < dials.Count; i++)
                    math.GetArrayElementAtIndex(build.arraySize + i).objectReferenceValue = dials[i];
            }
            so.ApplyModifiedProperties();
        }
    }
}
