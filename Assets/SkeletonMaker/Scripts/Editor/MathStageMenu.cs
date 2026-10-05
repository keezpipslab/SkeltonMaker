using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Add Math Stage: builds the math stage's dial next to
    /// the smoothing knob - Inflate (d - c) - and makes the Math stage
    /// "everything in Build, plus the dial". Needs the raymarch quad, the
    /// smoothing knob and the Stages object to be there already. An Inflate
    /// Knob that's already there is left where it is.
    ///
    /// Repeat (mod p) has no dial for now: RepeatKnob and RaymarchQuad.repeat
    /// still work, but this takes any Repeat Knob out of the scene.
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

            foreach (var old in Object.FindObjectsByType<RepeatKnob>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Undo.DestroyObjectImmediate(old.gameObject);

            var inflate = Object.FindFirstObjectByType<InflateKnob>(FindObjectsInactive.Include);
            if (inflate == null)
            {
                // Beside the smoothing knob, on its side away from the table.
                Transform first = smoothing.transform;
                Vector3 side = first.right;
                var table = GameObject.Find("Table");
                if (table != null && Vector3.Dot(side, first.position - table.transform.position) < 0f) side = -side;

                inflate = SmoothingKnobMenu.BuildKnob<InflateKnob>("Inflate Knob", first.position + side * Spacing, first.rotation, quad, -0.15f, 0.15f);
            }

            // The quad keeps whatever spacing the Repeat Knob last left it at.
            if (quad.repeat != 0f)
            {
                Undo.RecordObject(quad, "Add Math Stage");
                quad.repeat = 0f;
                EditorUtility.SetDirty(quad);
            }

            WireStage(controller);

            Selection.activeGameObject = inflate.gameObject;
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
