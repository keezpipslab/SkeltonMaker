using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// One skeleton's configuration as plain data: every primitive placed on
    /// it (Kind, Size, color, which bone or joint anchor it's parented under
    /// and its local pose there) plus the smoothing it was built with. That
    /// local pose is the same "how far from the joint it landed on" data
    /// AvatarDuplicateManager relies on, so it's enough to rebuild the
    /// skeleton exactly. CompositionStore saves and loads these at runtime;
    /// CompositionExporter writes the same JSON from the editor.
    /// </summary>
    [Serializable]
    public class SkeletonComposition
    {
        public const int CurrentVersion = 1;

        [Serializable]
        public class Element
        {
            public string kind;
            public Vector3 size;
            public string anchor;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public bool painted; // false = never dipped in a ColorBath, color is unused
            public Color color;
        }

        public int version = CurrentVersion;
        public string savedAtUtc;
        public float smoothing;
        public List<Element> elements = new List<Element>();

        /// <summary>Gathers every element placed on the rig - parented one level
        /// under a Bone_/Joint_ anchor - and nothing still on the table,
        /// currently held, or on the avatar body. quad may be null (smoothing
        /// is then saved as 0).</summary>
        public static SkeletonComposition Capture(SkeletonRig rig, RaymarchQuad quad)
        {
            var composition = new SkeletonComposition
            {
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                smoothing = quad != null ? quad.smoothing : 0f,
            };

            foreach (Transform anchor in rig.transform)
            {
                foreach (Transform child in anchor)
                {
                    if (!child.TryGetComponent(out RaymarchableElement element)) continue;

                    bool painted = child.TryGetComponent(out ElementColor ownColor);
                    composition.elements.Add(new Element
                    {
                        kind = element.Kind.ToString(),
                        size = element.Size,
                        anchor = anchor.name,
                        localPosition = child.localPosition,
                        localRotation = child.localRotation,
                        painted = painted,
                        color = painted ? ownColor.Color : Color.white,
                    });
                }
            }

            return composition;
        }

        public string ToJson() => JsonUtility.ToJson(this, prettyPrint: true);

        /// <summary>Null if the text isn't a composition this version can read.</summary>
        public static SkeletonComposition FromJson(string json)
        {
            SkeletonComposition composition;
            try
            {
                composition = JsonUtility.FromJson<SkeletonComposition>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }

            if (composition == null || composition.version > CurrentVersion) return null;
            composition.elements ??= new List<Element>();
            return composition;
        }
    }
}
