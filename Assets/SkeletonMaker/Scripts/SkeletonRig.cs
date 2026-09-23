using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Minimal life-size humanoid skeleton drawn as thin lines - a placement
    /// guide only, not raymarched and carrying no mesh geometry of its own.
    /// The joint set and connectivity mirror Unity's HumanBodyBones (the
    /// same 21-bone chain a live Humanoid Animator would drive - see the
    /// rayMarchVR project's RaymarchAvatarSource for that live version),
    /// but frozen into a fixed A-pose (standing, arms angled down and out
    /// from the shoulders) instead of being posed by an Animator.
    /// </summary>
    [ExecuteAlways]
    public class SkeletonRig : MonoBehaviour
    {
        public static SkeletonRig Instance { get; private set; }

        [SerializeField] private float lineWidth = 0.012f;
        [SerializeField] private Material lineMaterial;
        [SerializeField] private Color lineColor = Color.white;

        private static readonly Dictionary<string, Vector3> Joints = new Dictionary<string, Vector3>
        {
            { "Hips", new Vector3(0f, 0.95f, 0f) },
            { "Spine", new Vector3(0f, 1.08f, 0f) },
            { "Chest", new Vector3(0f, 1.25f, 0f) },
            { "Neck", new Vector3(0f, 1.45f, 0f) },
            { "Head", new Vector3(0f, 1.60f, 0f) },

            // A-pose: the upper arm angles ~35 degrees down and out from the
            // shoulder; the forearm then bends back toward vertical (~15
            // degrees) instead of continuing in a straight line, so there's
            // a visible kink at the elbow marking where upper arm ends and
            // forearm begins.
            { "LeftShoulder", new Vector3(0.09f, 1.42f, 0f) },
            { "LeftUpperArm", new Vector3(0.17f, 1.38f, 0f) },
            { "LeftLowerArm", new Vector3(0.377f, 1.085f, 0f) },
            { "LeftHand", new Vector3(0.470f, 0.737f, 0f) },

            { "RightShoulder", new Vector3(-0.09f, 1.42f, 0f) },
            { "RightUpperArm", new Vector3(-0.17f, 1.38f, 0f) },
            { "RightLowerArm", new Vector3(-0.377f, 1.085f, 0f) },
            { "RightHand", new Vector3(-0.470f, 0.737f, 0f) },

            { "LeftUpperLeg", new Vector3(0.10f, 0.90f, 0f) },
            { "LeftLowerLeg", new Vector3(0.10f, 0.48f, 0f) },
            { "LeftFoot", new Vector3(0.10f, 0.08f, 0f) },
            { "LeftToes", new Vector3(0.10f, 0.02f, 0.13f) },

            { "RightUpperLeg", new Vector3(-0.10f, 0.90f, 0f) },
            { "RightLowerLeg", new Vector3(-0.10f, 0.48f, 0f) },
            { "RightFoot", new Vector3(-0.10f, 0.08f, 0f) },
            { "RightToes", new Vector3(-0.10f, 0.02f, 0.13f) },
        };

        private static readonly (string from, string to)[] Bones =
        {
            ("Hips", "Spine"), ("Spine", "Chest"), ("Chest", "Neck"), ("Neck", "Head"),
            ("Chest", "LeftShoulder"), ("LeftShoulder", "LeftUpperArm"), ("LeftUpperArm", "LeftLowerArm"), ("LeftLowerArm", "LeftHand"),
            ("Chest", "RightShoulder"), ("RightShoulder", "RightUpperArm"), ("RightUpperArm", "RightLowerArm"), ("RightLowerArm", "RightHand"),
            ("Hips", "LeftUpperLeg"), ("LeftUpperLeg", "LeftLowerLeg"), ("LeftLowerLeg", "LeftFoot"), ("LeftFoot", "LeftToes"),
            ("Hips", "RightUpperLeg"), ("RightUpperLeg", "RightLowerLeg"), ("RightLowerLeg", "RightFoot"), ("RightFoot", "RightToes"),
        };

        private readonly List<(Vector3 a, Vector3 b)> boneSegmentsLocal = new List<(Vector3, Vector3)>();

        private void Awake()
        {
            Instance = this;
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            // Unity disallows DestroyImmediate/AddComponent's side effects
            // from inside OnValidate (Build() needs both) - defer to just
            // after instead of rebuilding inline here.
            if (isActiveAndEnabled) UnityEditor.EditorApplication.delayCall += DeferredBuild;
#else
            if (isActiveAndEnabled) Build();
#endif
        }

#if UNITY_EDITOR
        private void DeferredBuild()
        {
            if (this == null) return; // destroyed before the deferred call ran
            if (isActiveAndEnabled) Build();
        }
#endif

        private void Build()
        {
            // Idempotent: a domain reload resets boneSegmentsLocal but the
            // previous run's bone GameObjects are still children - clear them
            // first so repeated (edit-mode) rebuilds don't duplicate bones.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            boneSegmentsLocal.Clear();

            foreach (var bone in Bones)
            {
                Vector3 a = Joints[bone.from];
                Vector3 b = Joints[bone.to];
                boneSegmentsLocal.Add((a, b));

                var go = new GameObject($"Bone_{bone.from}_{bone.to}");
                go.transform.SetParent(transform, false);

                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = 2;
                lr.SetPosition(0, a);
                lr.SetPosition(1, b);
                lr.startWidth = lineWidth;
                lr.endWidth = lineWidth;
                lr.numCapVertices = 4;
                if (lineMaterial != null) lr.sharedMaterial = lineMaterial;
                lr.startColor = lineColor;
                lr.endColor = lineColor;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
            }
        }

        /// <summary>Shortest distance from a world-space point to any bone segment.</summary>
        public float DistanceToNearestBone(Vector3 worldPoint)
        {
            float best = float.MaxValue;
            foreach (var segment in boneSegmentsLocal)
            {
                Vector3 a = transform.TransformPoint(segment.a);
                Vector3 b = transform.TransformPoint(segment.b);
                float d = DistancePointToSegment(worldPoint, a, b);
                if (d < best) best = d;
            }
            return best;
        }

        private static float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            float t = lenSq > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSq) : 0f;
            Vector3 closest = a + ab * t;
            return Vector3.Distance(p, closest);
        }
    }
}
