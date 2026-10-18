using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// A designed world as plain data: every primitive placed in it (Kind,
    /// Size, its pose under the World object, whether it adds or subtracts,
    /// and its own smoothing) plus the smoothing and inflate it was made
    /// with. The primitives are in the order the raymarch quad blends them
    /// in, which matters once they don't all blend over the same radius. What
    /// SkeletonComposition is for a skeleton; DesignWorld saves and loads it.
    /// </summary>
    [Serializable]
    public class WorldDesign
    {
        public const int CurrentVersion = 1;

        [Serializable]
        public class Element
        {
            public string kind;
            public Vector3 size;
            public Vector3 position; // under the World object
            public Quaternion rotation;
            public float scale = 1f; // of the primitive as a whole, on top of size: 1 unless the world was another size when it was placed
            public bool subtract;
            public float smoothing; // its own, on top of the world's
        }

        public int version = CurrentVersion;
        public string savedAtUtc;
        public float smoothing;
        public float inflate;
        public List<Element> elements = new List<Element>();

        /// <summary>Gathers every primitive placed in the world, and nothing on
        /// the panel or in a hand. quad may be null (smoothing and inflate are
        /// then saved as 0).</summary>
        public static WorldDesign Capture(Transform world, RaymarchQuad quad)
        {
            var design = new WorldDesign
            {
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                smoothing = quad != null ? quad.smoothing : 0f,
                inflate = quad != null ? quad.inflate : 0f,
            };

            foreach (Transform child in world)
            {
                if (!child.TryGetComponent(out WorldElement placed) || !child.TryGetComponent(out RaymarchableElement element)) continue;

                design.elements.Add(new Element
                {
                    kind = element.Kind.ToString(),
                    size = element.Size,
                    position = placed.transform.localPosition,
                    rotation = placed.transform.localRotation,
                    scale = placed.transform.localScale.x,
                    subtract = placed.Subtract,
                    smoothing = placed.Smoothing,
                });
            }

            return design;
        }

        public string ToJson() => JsonUtility.ToJson(this, prettyPrint: true);

        /// <summary>Null if the text isn't a world this version can read.</summary>
        public static WorldDesign FromJson(string json)
        {
            WorldDesign design;
            try
            {
                design = JsonUtility.FromJson<WorldDesign>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }

            if (design == null || design.version > CurrentVersion) return null;
            design.elements ??= new List<Element>();
            return design;
        }
    }
}
