using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Editor-only: exports every primitive currently placed on the (main)
    /// skeleton as JSON - its Kind, Size, which bone or joint anchor it's
    /// parented under, and its local position/rotation offset from that
    /// anchor. That local offset is the same "how far from the joint it
    /// landed on" data AvatarDuplicateManager already relies on to mirror a
    /// duplicate correctly, so it's enough to reconstruct the composition
    /// later or feed it to another tool (e.g. a future raymarch/SDF importer -
    /// RaymarchableElement already exposes Kind + Size as exactly the data an
    /// SDF material would need).
    /// </summary>
    public static class CompositionExporter
    {
        [Serializable]
        private class ExportedElement
        {
            public string kind;
            public Vector3 size;
            public string anchor;
            public Vector3 localPosition;
            public Quaternion localRotation;
        }

        [Serializable]
        private class SkeletonComposition
        {
            public string exportedAtUtc;
            public List<ExportedElement> elements = new List<ExportedElement>();
        }

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

            var composition = new SkeletonComposition { exportedAtUtc = DateTime.UtcNow.ToString("o") };

            foreach (var element in UnityEngine.Object.FindObjectsByType<RaymarchableElement>(FindObjectsSortMode.None))
            {
                var anchor = element.transform.parent;
                // Only elements actually placed on this rig - parented one level
                // under a Bone_/Joint_ anchor that is itself a direct child of the
                // rig - not anything still on the table, currently held, or a
                // stand-in avatar duplicate (those have had RaymarchableElement
                // stripped off already anyway, so they're never even seen here).
                if (anchor == null || anchor.parent != rig.transform) continue;

                composition.elements.Add(new ExportedElement
                {
                    kind = element.Kind.ToString(),
                    size = element.Size,
                    anchor = anchor.name,
                    localPosition = element.transform.localPosition,
                    localRotation = element.transform.localRotation,
                });
            }

            elementCount = composition.elements.Count;
            return JsonUtility.ToJson(composition, prettyPrint: true);
        }
    }
}
