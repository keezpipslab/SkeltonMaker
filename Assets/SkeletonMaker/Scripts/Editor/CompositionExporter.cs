using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Editor-only: exports every primitive currently placed on the (main)
    /// skeleton to a JSON file of your choosing - the same SkeletonComposition
    /// format CompositionStore saves and loads at runtime, so an exported file
    /// can be dropped in as a save, or fed to another tool.
    /// </summary>
    public static class CompositionExporter
    {
        [MenuItem("SkeletonMaker/Export Composition...")]
        private static void Export()
        {
            string json = BuildCompositionJson(out int count);
            if (json == null)
            {
                EditorUtility.DisplayDialog("Export Composition", "No SkeletonRig found in the open scene.", "OK");
                return;
            }

            string defaultName = "SkeletonComposition_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
            string path = EditorUtility.SaveFilePanel("Export Composition", Application.dataPath, defaultName, "json");
            if (string.IsNullOrEmpty(path)) return;

            File.WriteAllText(path, json);
            Debug.Log($"Exported {count} placed element(s) to {path}");
        }

        /// <summary>Gathers every element placed on the (main) skeleton and returns
        /// it as pretty-printed JSON, or null if no SkeletonRig exists in the open
        /// scene. Split out from Export() so the gathering logic is testable
        /// without going through the (blocking, interactive) save dialog.</summary>
        public static string BuildCompositionJson(out int elementCount)
        {
            elementCount = 0;
            var rig = SkeletonRig.Instance != null ? SkeletonRig.Instance : UnityEngine.Object.FindFirstObjectByType<SkeletonRig>();
            if (rig == null) return null;

            var composition = SkeletonComposition.Capture(rig, UnityEngine.Object.FindFirstObjectByType<RaymarchQuad>());
            elementCount = composition.elements.Count;
            return composition.ToJson();
        }
    }
}
