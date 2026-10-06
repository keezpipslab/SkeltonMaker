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

        // Hold-proximity feedback: while a primitive is held, the nearest
        // bone brightens as it gets within nearHighlightRadius, then flips
        // to placeableColor once inside the actual placement radius.
        [SerializeField] private float nearHighlightRadius = 0.35f;
        [SerializeField] private Color nearColor = Color.yellow;
        [SerializeField] private Color placeableColor = Color.green;

        [Tooltip("A single upright bone instead of the whole body: the tutorial's practice stick. Nothing placed on it is copied to the avatar.")]
        [SerializeField] private bool stick;
        [SerializeField] private float stickLength = 0.5f;

        /// <summary>True for the tutorial's one-bone practice stick.</summary>
        public bool IsStick => stick;

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

            // Knee kicked slightly forward (+Z) so it's a visible kink rather
            // than a dead-straight hip-to-ankle line, the same non-collinear
            // "landmark" treatment the elbow already gets above.
            { "LeftUpperLeg", new Vector3(0.10f, 0.90f, 0f) },
            { "LeftLowerLeg", new Vector3(0.10f, 0.48f, 0.04f) },
            { "LeftFoot", new Vector3(0.10f, 0.08f, 0f) },
            { "LeftToes", new Vector3(0.10f, 0.02f, 0.13f) },

            { "RightUpperLeg", new Vector3(-0.10f, 0.90f, 0f) },
            { "RightLowerLeg", new Vector3(-0.10f, 0.48f, 0.04f) },
            { "RightFoot", new Vector3(-0.10f, 0.08f, 0f) },
            { "RightToes", new Vector3(-0.10f, 0.02f, 0.13f) },
        };

        // The six limb hinges per side that get their own dedicated placement
        // target, distinct from "somewhere along this bone" - the point two
        // bones actually share. Torso joints (hips/spine/chest/neck/head)
        // stay bone-segment-only, matching how the words "shoulder", "elbow",
        // "wrist", "hip", "knee" and "ankle" map onto this joint chain.
        private static readonly string[] HingeJointNames =
        {
            "LeftUpperArm", "LeftLowerArm", "LeftHand",
            "RightUpperArm", "RightLowerArm", "RightHand",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot",
            "RightUpperLeg", "RightLowerLeg", "RightFoot",
        };

        private static readonly (string from, string to)[] Bones =
        {
            ("Hips", "Spine"), ("Spine", "Chest"), ("Chest", "Neck"), ("Neck", "Head"),
            ("Chest", "LeftShoulder"), ("LeftShoulder", "LeftUpperArm"), ("LeftUpperArm", "LeftLowerArm"), ("LeftLowerArm", "LeftHand"),
            ("Chest", "RightShoulder"), ("RightShoulder", "RightUpperArm"), ("RightUpperArm", "RightLowerArm"), ("RightLowerArm", "RightHand"),
            ("Hips", "LeftUpperLeg"), ("LeftUpperLeg", "LeftLowerLeg"), ("LeftLowerLeg", "LeftFoot"), ("LeftFoot", "LeftToes"),
            ("Hips", "RightUpperLeg"), ("RightUpperLeg", "RightLowerLeg"), ("RightLowerLeg", "RightFoot"), ("RightFoot", "RightToes"),
        };

        // The stick's own layout: one bone, no hinges. Its joint names are
        // deliberately not body joints, so AvatarBodyTarget has no slot for them.
        private static readonly (string from, string to)[] StickBones = { ("Stick", "StickTop") };
        private static readonly string[] NoHingeJoints = { };

        // Whichever layout this rig was last built with (see Build).
        private (string from, string to)[] bones = Bones;
        private string[] hingeJointNames = HingeJointNames;

        private Vector3 JointPosition(string jointName)
        {
            if (!stick) return Joints[jointName];
            return jointName == "StickTop" ? Vector3.up * stickLength : Vector3.zero;
        }

        private readonly List<(Vector3 a, Vector3 b)> boneSegmentsLocal = new List<(Vector3, Vector3)>();
        private readonly List<LineRenderer> boneLineRenderers = new List<LineRenderer>();

        // Parallel to the bone lists above, one entry per HingeJointNames
        // entry: its own dedicated anchor GameObject, and which bone(s) (by
        // index into boneSegmentsLocal/boneLineRenderers) touch it - 1 for
        // the wrist (a leaf, only the forearm bone touches it), 2 for the
        // other five (the bone ending there and the bone starting there).
        private readonly List<Transform> hingeJointAnchors = new List<Transform>();
        private readonly List<int[]> hingeJointIncidentBones = new List<int[]>();

        private readonly List<int> highlightedBoneIndices = new List<int>();
        private MaterialPropertyBlock highlightMpb;

        /// <summary>The joint names used both here and by AvatarBodyTarget, so a
        /// future avatar's joint slots can be kept in sync with this rig's bones
        /// without duplicating the name list.</summary>
        public static IReadOnlyCollection<string> JointNames => Joints.Keys;

        public float LineWidth => lineWidth;
        public Material LineMaterial => lineMaterial;

        private static (string from, string to, Vector3 direction)[] bodySegments;

        /// <summary>Every bone of the full body with the direction it points in
        /// the A-pose, whichever rigs happen to be in the scene.</summary>
        public static IReadOnlyList<(string from, string to, Vector3 direction)> BodySegments
        {
            get
            {
                if (bodySegments == null)
                {
                    bodySegments = new (string, string, Vector3)[Bones.Length];
                    for (int i = 0; i < Bones.Length; i++)
                        bodySegments[i] = (Bones[i].from, Bones[i].to, (Joints[Bones[i].to] - Joints[Bones[i].from]).normalized);
                }
                return bodySegments;
            }
        }

        /// <summary>Gives root the full body's Bone_/Joint_ children in the A-pose,
        /// named and laid out exactly like a SkeletonRig's, but with no rig on
        /// it - so nothing can be placed there (a remote performer's body).</summary>
        public static void BuildBodyAnchors(Transform root, float lineWidth, Material lineMaterial, Color lineColor)
        {
            foreach (var bone in Bones)
                CreateBone(root, bone.from, bone.to, Joints[bone.from], Joints[bone.to], lineWidth, lineMaterial, lineColor);
            foreach (var jointName in HingeJointNames)
                CreateJoint(root, jointName, Joints[jointName]);
        }

        private static LineRenderer CreateBone(Transform parent, string from, string to, Vector3 a, Vector3 b,
            float lineWidth, Material lineMaterial, Color lineColor)
        {
            var go = new GameObject($"Bone_{from}_{to}");
            go.transform.SetParent(parent, false);

            // Anchored at the bone's own ("from") joint, not the rig root, so
            // BoneAnchor(index).position is that joint's real world position -
            // an element parented under it (SkeletonPlacement.PlaceOnSkeleton)
            // gets a small, meaningful local offset from the joint it landed
            // on, rather than a large offset from the whole rig's origin. The
            // line's own points are shifted by the same amount so it still
            // renders in exactly the same world place.
            go.transform.localPosition = a;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, Vector3.zero);
            lr.SetPosition(1, b - a);
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.numCapVertices = 4;
            if (lineMaterial != null) lr.sharedMaterial = lineMaterial;
            lr.startColor = lineColor;
            lr.endColor = lineColor;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private static Transform CreateJoint(Transform parent, string jointName, Vector3 position)
        {
            var go = new GameObject($"Joint_{jointName}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        /// <summary>While on (toggled with the M key), placing an element also
        /// places a copy on the other side of the body, and the held preview
        /// lights up both sides.</summary>
        public bool MirrorEnabled { get; set; }

        private readonly List<int> mirroredHighlightScratch = new List<int>();

        private static string MirrorName(string jointName)
        {
            if (jointName.StartsWith("Left")) return "Right" + jointName.Substring(4);
            if (jointName.StartsWith("Right")) return "Left" + jointName.Substring(5);
            return jointName;
        }

        /// <summary>The bone on the opposite side of the body (the bone itself for
        /// the spine/neck ones down the middle).</summary>
        public int MirrorBoneIndex(int index)
        {
            if (index < 0 || index >= bones.Length) return -1;
            string from = MirrorName(bones[index].from), to = MirrorName(bones[index].to);
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i].from == from && bones[i].to == to) return i;
            }
            return -1;
        }

        /// <summary>The same hinge joint on the opposite side of the body.</summary>
        public int MirrorHingeJointIndex(int index)
        {
            if (index < 0 || index >= hingeJointNames.Length) return -1;
            return System.Array.IndexOf(hingeJointNames, MirrorName(hingeJointNames[index]));
        }

        /// <summary>Reflects a world-space pose through the rig's left/right
        /// symmetry plane. The rotation is the reflected one for a shape that is
        /// itself left/right symmetric, which every primitive here is.</summary>
        public void MirrorPose(Vector3 position, Quaternion rotation, out Vector3 mirroredPosition, out Quaternion mirroredRotation)
        {
            Vector3 p = transform.InverseTransformPoint(position);
            Quaternion q = Quaternion.Inverse(transform.rotation) * rotation;
            mirroredPosition = transform.TransformPoint(new Vector3(-p.x, p.y, p.z));
            mirroredRotation = transform.rotation * new Quaternion(q.x, -q.y, -q.z, q.w);
        }

        private void Awake() => Build();

        // Instance is whichever rig is switched on, so nothing can be placed
        // on one that isn't there; the real skeleton wins over the stick.
        private void OnEnable()
        {
            if (Instance == null || !stick) Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame)
            {
                MirrorEnabled = !MirrorEnabled;
                Debug.Log($"SkeletonRig: mirror placement {(MirrorEnabled ? "on" : "off")}");
            }
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
            boneLineRenderers.Clear();
            hingeJointAnchors.Clear();
            hingeJointIncidentBones.Clear();
            highlightedBoneIndices.Clear(); // the old bone GameObjects it referred to are gone

            bones = stick ? StickBones : Bones;
            hingeJointNames = stick ? NoHingeJoints : HingeJointNames;

            foreach (var bone in bones)
            {
                Vector3 a = JointPosition(bone.from);
                Vector3 b = JointPosition(bone.to);
                boneSegmentsLocal.Add((a, b));
                boneLineRenderers.Add(CreateBone(transform, bone.from, bone.to, a, b, lineWidth, lineMaterial, lineColor));
            }

            foreach (var jointName in hingeJointNames)
            {
                hingeJointAnchors.Add(CreateJoint(transform, jointName, JointPosition(jointName)));

                var incident = new List<int>(2);
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i].from == jointName || bones[i].to == jointName) incident.Add(i);
                }
                hingeJointIncidentBones.Add(incident.ToArray());
            }
        }

        /// <summary>Shortest distance from a world-space point to any bone segment.</summary>
        public float DistanceToNearestBone(Vector3 worldPoint)
        {
            NearestBoneIndex(worldPoint, out float distance);
            return distance;
        }

        /// <summary>Index (into the internal bone list) of the bone segment nearest
        /// worldPoint, and its distance - the same "nearest segment" estimate used
        /// both for the placement radius check and for BoneAnchor/BoneJointName.</summary>
        public int NearestBoneIndex(Vector3 worldPoint, out float distance)
        {
            int bestIndex = -1;
            float best = float.MaxValue;
            for (int i = 0; i < boneSegmentsLocal.Count; i++)
            {
                Vector3 a = transform.TransformPoint(boneSegmentsLocal[i].a);
                Vector3 b = transform.TransformPoint(boneSegmentsLocal[i].b);
                float d = DistancePointToSegment(worldPoint, a, b);
                if (d < best)
                {
                    best = d;
                    bestIndex = i;
                }
            }
            distance = best;
            return bestIndex;
        }

        /// <summary>The transform placed elements nearest this bone should parent
        /// under - the bone's own GameObject, so the hierarchy reflects which body
        /// part each element landed on.</summary>
        public Transform BoneAnchor(int index) =>
            index >= 0 && index < boneLineRenderers.Count ? boneLineRenderers[index].transform : transform;

        /// <summary>The joint name (matching AvatarBodyTarget's slots) a bone's
        /// proximal end is named after - e.g. the "LeftShoulder"-to-"LeftUpperArm"
        /// segment is named for its proximal joint, "LeftShoulder".</summary>
        public string BoneJointName(int index) =>
            index >= 0 && index < bones.Length ? bones[index].from : null;

        /// <summary>Index (into the hinge-joint list, HingeJointNames order) of the
        /// hinge joint - shoulder/elbow/wrist/hip/knee/ankle - nearest worldPoint,
        /// and its distance. A point distance, not a segment distance: joints are
        /// single points, not lines.</summary>
        public int NearestHingeJointIndex(Vector3 worldPoint, out float distance)
        {
            int bestIndex = -1;
            float best = float.MaxValue;
            for (int i = 0; i < hingeJointAnchors.Count; i++)
            {
                float d = Vector3.Distance(worldPoint, hingeJointAnchors[i].position);
                if (d < best)
                {
                    best = d;
                    bestIndex = i;
                }
            }
            distance = best;
            return bestIndex;
        }

        /// <summary>The transform placed elements nearest this hinge joint should
        /// parent under - a dedicated GameObject at the joint itself (e.g.
        /// "Joint_LeftLowerArm" for the elbow), not either of its two incident
        /// bones, so the hierarchy has one unambiguous node per joint.</summary>
        public Transform HingeJointAnchor(int index) =>
            index >= 0 && index < hingeJointAnchors.Count ? hingeJointAnchors[index] : transform;

        /// <summary>The joint name (matching AvatarBodyTarget's slots) for this
        /// hinge joint index.</summary>
        public string HingeJointName(int index) =>
            index >= 0 && index < hingeJointNames.Length ? hingeJointNames[index] : null;

        /// <summary>Finds a bone or hinge joint anchor by its GameObject name (how
        /// a saved SkeletonComposition refers to it), along with the joint name
        /// that anchor maps to on the avatar body.</summary>
        public bool TryFindAnchor(string anchorName, out Transform anchor, out string jointName)
        {
            for (int i = 0; i < boneLineRenderers.Count; i++)
            {
                if (boneLineRenderers[i].name != anchorName) continue;
                anchor = boneLineRenderers[i].transform;
                jointName = BoneJointName(i);
                return true;
            }

            for (int i = 0; i < hingeJointAnchors.Count; i++)
            {
                if (hingeJointAnchors[i].name != anchorName) continue;
                anchor = hingeJointAnchors[i];
                jointName = HingeJointName(i);
                return true;
            }

            anchor = null;
            jointName = null;
            return false;
        }

        /// <summary>How many elements are placed on this rig right now.</summary>
        public int PlacedElementCount()
        {
            int count = 0;
            foreach (Transform anchor in transform)
            {
                foreach (Transform child in anchor)
                {
                    if (child.TryGetComponent(out RaymarchableElement _)) count++;
                }
            }
            return count;
        }

        /// <summary>Takes everything placed on this rig off it for good (and its
        /// copies off the avatar body).</summary>
        public void RemovePlacedElements()
        {
            foreach (Transform anchor in transform)
            {
                foreach (Transform child in anchor)
                {
                    if (child.TryGetComponent(out SkeletonPlacement placed)) placed.Remove();
                }
            }
        }

        /// <summary>Called once per frame while a primitive is held, to preview
        /// where it would land. Mirrors SkeletonPlacement.OnReleased's own
        /// priority exactly, so the preview never shows green somewhere release
        /// wouldn't actually place: a hinge joint within jointPlaceDistance always
        /// wins (brightening every bone touching it together - both the upper and
        /// lower segment for an elbow/knee/etc.) even if a bone segment is
        /// technically closer; otherwise a bone segment within placeDistance.
        /// If neither is close enough to place on yet, whichever is nearer gets a
        /// dimmer "approaching" highlight as long as it's within
        /// nearHighlightRadius.</summary>
        public void UpdateHeldPreview(Vector3 worldPoint, float placeDistance, float jointPlaceDistance)
        {
            int jointIndex = NearestHingeJointIndex(worldPoint, out float jointDistance);
            if (jointIndex >= 0 && jointDistance <= jointPlaceDistance)
            {
                SetHighlightedBones(hingeJointIncidentBones[jointIndex], placeableColor);
                return;
            }

            int boneIndex = NearestBoneIndex(worldPoint, out float boneDistance);
            if (boneIndex >= 0 && boneDistance <= placeDistance)
            {
                SetHighlightedBones(new[] { boneIndex }, placeableColor);
                return;
            }

            bool jointNearer = jointIndex >= 0 && (boneIndex < 0 || jointDistance <= boneDistance);
            if (jointNearer && jointDistance <= nearHighlightRadius)
            {
                SetHighlightedBones(hingeJointIncidentBones[jointIndex], nearColor);
            }
            else if (!jointNearer && boneIndex >= 0 && boneDistance <= nearHighlightRadius)
            {
                SetHighlightedBones(new[] { boneIndex }, nearColor);
            }
            else
            {
                ClearHeldPreview();
            }
        }

        /// <summary>Resets whichever bone(s) are currently highlighted, if any. Safe
        /// to call any time, including when nothing is highlighted.</summary>
        public void ClearHeldPreview()
        {
            foreach (int i in highlightedBoneIndices) ResetBoneColor(i);
            highlightedBoneIndices.Clear();
        }

        private void SetHighlightedBones(IReadOnlyList<int> indices, Color color)
        {
            if (MirrorEnabled)
            {
                mirroredHighlightScratch.Clear();
                for (int i = 0; i < indices.Count; i++)
                {
                    mirroredHighlightScratch.Add(indices[i]);
                    int mirror = MirrorBoneIndex(indices[i]);
                    if (mirror >= 0 && mirror != indices[i]) mirroredHighlightScratch.Add(mirror);
                }
                indices = mirroredHighlightScratch;
            }

            // Reset any previously-highlighted bone that isn't part of the new set
            // (e.g. moving from one joint's pair to a single mid-bone highlight).
            for (int i = highlightedBoneIndices.Count - 1; i >= 0; i--)
            {
                if (!Contains(indices, highlightedBoneIndices[i]))
                {
                    ResetBoneColor(highlightedBoneIndices[i]);
                    highlightedBoneIndices.RemoveAt(i);
                }
            }

            foreach (int index in indices)
            {
                SetBoneColor(index, color);
                if (!highlightedBoneIndices.Contains(index)) highlightedBoneIndices.Add(index);
            }
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
            return false;
        }

        private void SetBoneColor(int index, Color color)
        {
            if (index < 0 || index >= boneLineRenderers.Count) return;
            if (highlightMpb == null) highlightMpb = new MaterialPropertyBlock();

            // MaterialPropertyBlock, not LineRenderer.startColor/endColor: this
            // reliably overrides just this instance's color regardless of whether
            // the assigned material's shader honors per-vertex line colors (not
            // guaranteed for a plain URP Unlit material like SkeletonLineMat).
            highlightMpb.SetColor("_BaseColor", color);
            boneLineRenderers[index].SetPropertyBlock(highlightMpb);
        }

        private void ResetBoneColor(int index)
        {
            if (index < 0 || index >= boneLineRenderers.Count) return;
            boneLineRenderers[index].SetPropertyBlock(null);
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
