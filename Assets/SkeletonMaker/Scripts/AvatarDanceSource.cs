using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace SkeletonMaker
{
    [ExecuteAlways]
    public class AvatarDanceSource : MonoBehaviour
    {
        [Tooltip("The Humanoid Animator to read bone poses from (e.g. the instantiated Dancing.fbx rig).")]
        [SerializeField] private Animator sourceAnimator;

        [Tooltip("The player's tracked body (Meta Movement SDK). Tracking mode is skipped while this is unassigned.")]
        [SerializeField] private AvatarBodyTrackingSource bodySource;

        [Tooltip("The stand-in skeleton root whose Bone_/Joint_ children get repositioned. Defaults to this GameObject.")]
        [SerializeField] private Transform standInRoot;

        [Tooltip("Still: the frozen A-pose (matching the main skeleton). Animation: the dance. Tracking: the player's own movement. Cycled at runtime by either controller trigger below, or set it here directly for testing.")]
        [FormerlySerializedAs("isDancing")]
        [SerializeField] private AvatarMode mode = AvatarMode.Animation;

        [Tooltip("Either controller's trigger (XRI's \"Activate\" action) steps to the next mode - unused elsewhere in this project.")]
        [SerializeField] private InputActionReference leftToggleAction;
        [SerializeField] private InputActionReference rightToggleAction;

        // anchorRotation = placement * sourceBone.rotation * restInverse[joint]; identity-in-A-pose by construction.
        private readonly Dictionary<string, Quaternion> restInverse = new Dictionary<string, Quaternion>();
        private IAvatarPoseSource calibratedSource;
        private int calibratedVersion;

        private AnimatorPoseSource animatorSource;
        private AvatarMode activeMode = AvatarMode.Still;

        // Where the source's body goes on the stand-in: p -> rotation * (p - pivot) + target.
        // Fixed once at the start of a mode, so the avatar starts out standing where the still
        // one does and from then on moves exactly as its source does.
        private bool placed;
        private Quaternion placementRotation;
        private Vector3 placementPivot;
        private Vector3 placementTarget;

        public AvatarMode Mode
        {
            get => mode;
            set => mode = value;
        }

        private void Reset() => standInRoot = transform;

        private void OnEnable()
        {
            calibratedSource = null;
            animatorSource = null;
            activeMode = AvatarMode.Still;
            placed = false;
            if (leftToggleAction != null) { leftToggleAction.action.Enable(); leftToggleAction.action.performed += OnToggle; }
            if (rightToggleAction != null) { rightToggleAction.action.Enable(); rightToggleAction.action.performed += OnToggle; }
        }

        private void OnDisable()
        {
            if (leftToggleAction != null) leftToggleAction.action.performed -= OnToggle;
            if (rightToggleAction != null) rightToggleAction.action.performed -= OnToggle;
            if (activeMode == AvatarMode.Tracking && bodySource != null) bodySource.End();
            activeMode = AvatarMode.Still;
        }

        private void OnToggle(InputAction.CallbackContext ctx)
        {
            switch (mode)
            {
                case AvatarMode.Still: mode = AvatarMode.Animation; break;
                case AvatarMode.Animation: mode = bodySource != null ? AvatarMode.Tracking : AvatarMode.Still; break;
                default: mode = AvatarMode.Still; break;
            }
        }

        private void LateUpdate()
        {
            if (standInRoot == null) standInRoot = transform;
            if (mode != activeMode) EnterMode(mode);

            var source = ActiveSource();
            if (source == null || !source.Refresh())
            {
                // Nothing to follow (yet): stand still, unless the source dropped out after the
                // avatar had already moved off - then hold its last pose rather than snap back.
                if (!placed) DriveFromRestPose();
                return;
            }

            if (source != calibratedSource || source.RestVersion != calibratedVersion)
            {
                if (!Calibrate(source)) { DriveFromRestPose(); return; }
                calibratedSource = source;
                calibratedVersion = source.RestVersion;
            }
            if (!placed && !Place(source)) { DriveFromRestPose(); return; }
            DriveFromSource(source);
        }

        // Also runs when the mode is changed straight in the Inspector, not only by the triggers.
        private void EnterMode(AvatarMode next)
        {
            if (bodySource != null && Application.isPlaying)
            {
                if (next == AvatarMode.Tracking) bodySource.Begin();
                else if (activeMode == AvatarMode.Tracking) bodySource.End();
            }
            activeMode = next;
            placed = false;
        }

        private IAvatarPoseSource ActiveSource()
        {
            switch (activeMode)
            {
                case AvatarMode.Animation:
                    if (sourceAnimator == null) return null;
                    if (animatorSource == null || animatorSource.Animator != sourceAnimator)
                        animatorSource = new AnimatorPoseSource(sourceAnimator);
                    return animatorSource;
                case AvatarMode.Tracking:
                    return bodySource;
                default:
                    return null;
            }
        }

        // The main skeleton's "Left" joints sit on +X, which is the humanoid's *Right* side when both
        // face +Z, so every name is looked up on the opposite side to keep primitives on the same limb.
        private static string HumanoidName(string jointName)
        {
            if (jointName.StartsWith("Left")) return "Right" + jointName.Substring(4);
            if (jointName.StartsWith("Right")) return "Left" + jointName.Substring(5);
            return jointName;
        }

        // Rest orientation = the source's T-pose, converted to the A-pose the main skeleton uses
        // (identity anchors) by swinging each limb onto its A-pose direction. Everything is done in
        // the body's own frame (Z forward, Y up), so it holds whichever way the source is facing.
        private bool Calibrate(IAvatarPoseSource source)
        {
            var mainRig = SkeletonRig.Instance != null ? SkeletonRig.Instance : FindFirstObjectByType<SkeletonRig>();
            if (mainRig == null) return false;

            var segments = new List<(string from, string to, Vector3 dir)>();
            foreach (Transform child in mainRig.transform)
            {
                if (!child.name.StartsWith("Bone_")) continue;
                var parts = child.name.Substring("Bone_".Length).Split('_');
                var lr = child.GetComponent<LineRenderer>();
                if (parts.Length != 2 || lr == null) continue;
                var localDir = child.localRotation * (lr.GetPosition(1) - lr.GetPosition(0));
                segments.Add((parts[0], parts[1], localDir.normalized));
            }

            if (!source.TryGetRestPose("Hips", out var restHips, out _) ||
                !source.TryGetRestPose("Head", out var restHead, out _) ||
                !TryGetRight(source, true, out var restRight))
                return false;
            var restUp = restHead - restHips;
            var toBody = Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(restRight, restUp), restUp));

            restInverse.Clear();
            foreach (Transform child in standInRoot)
            {
                foreach (var jointName in NamesFor(child))
                {
                    if (restInverse.ContainsKey(jointName)) continue;
                    if (!source.TryGetRestPose(HumanoidName(jointName), out _, out var rotAtRest)) continue;

                    string a = null, b = null;
                    Vector3 aDir = default;
                    foreach (var s in segments)
                        if (s.from == jointName) { a = s.from; b = s.to; aDir = s.dir; break; }
                    if (a == null)
                        foreach (var s in segments)
                            if (s.to == jointName) { a = s.from; b = s.to; aDir = s.dir; break; }
                    if (a == null) continue;
                    if (!source.TryGetRestPose(HumanoidName(a), out var pa, out _) ||
                        !source.TryGetRestPose(HumanoidName(b), out var pb, out _)) continue;

                    var tDir = toBody * (pb - pa).normalized;
                    var swing = Quaternion.FromToRotation(tDir, aDir);
                    restInverse[jointName] = Quaternion.Inverse(swing * toBody * rotAtRest);
                }
            }
            return true;
        }

        // Puts the source's hips over the still avatar's hips. The dance keeps its own heading and
        // height; the tracked body is also turned to face the way the still avatar does and stood
        // on the stand-in's floor, since the player is somewhere else, facing anywhere, in a
        // tracking space whose floor needn't be the scene's.
        private bool Place(IAvatarPoseSource source)
        {
            var mainRig = SkeletonRig.Instance != null ? SkeletonRig.Instance : FindFirstObjectByType<SkeletonRig>();
            var stillHips = mainRig != null ? mainRig.transform.Find("Bone_Hips_Spine") : null;
            if (stillHips == null || !source.TryGetPose("Hips", out var hips, out _)) return false;

            var up = standInRoot.up;
            var target = standInRoot.TransformPoint(stillHips.localPosition);
            placementPivot = hips;
            placementRotation = Quaternion.identity;
            float height = Vector3.Dot(hips - standInRoot.position, up);

            if (activeMode == AvatarMode.Tracking)
            {
                if (!TryGetRight(source, false, out var right)) return false;
                var forward = Vector3.ProjectOnPlane(Vector3.Cross(right, up), up);
                if (forward.sqrMagnitude > 1e-6f)
                    placementRotation = Quaternion.AngleAxis(Vector3.SignedAngle(forward, standInRoot.forward, up), up);

                // Whichever foot is lowest is taken to be on the ground.
                float lowest = float.MaxValue;
                foreach (var foot in new[] { "LeftFoot", "LeftToes", "RightFoot", "RightToes" })
                    if (source.TryGetPose(foot, out var p, out _)) lowest = Mathf.Min(lowest, Vector3.Dot(p - hips, up));
                float stillLowest = float.MaxValue;
                foreach (Transform child in mainRig.transform)
                {
                    var lr = child.GetComponent<LineRenderer>();
                    if (lr == null) continue;
                    stillLowest = Mathf.Min(stillLowest, (child.localPosition + child.localRotation * lr.GetPosition(1)).y);
                }
                if (lowest < float.MaxValue && stillLowest < float.MaxValue) height = stillLowest - lowest;
            }

            placementTarget = target + up * (height - Vector3.Dot(target - standInRoot.position, up));
            placed = true;
            return true;
        }

        // The body's own right-hand direction, from hip to hip (shoulder to shoulder as a fallback).
        private static bool TryGetRight(IAvatarPoseSource source, bool rest, out Vector3 right)
        {
            foreach (var joint in new[] { "UpperLeg", "UpperArm" })
            {
                Vector3 l, r;
                bool found = rest
                    ? source.TryGetRestPose("Left" + joint, out l, out _) & source.TryGetRestPose("Right" + joint, out r, out _)
                    : source.TryGetPose("Left" + joint, out l, out _) & source.TryGetPose("Right" + joint, out r, out _);
                if (found && (r - l).sqrMagnitude > 1e-6f) { right = r - l; return true; }
            }
            right = default;
            return false;
        }

        private void DriveFromSource(IAvatarPoseSource source)
        {
            foreach (Transform child in standInRoot)
            {
                if (child.name.StartsWith("Joint_"))
                {
                    string jointName = child.name.Substring("Joint_".Length);
                    if (!TryGetPlaced(source, jointName, out var position, out var rotation)) continue;
                    child.SetPositionAndRotation(position, rotation);
                }
                else if (child.name.StartsWith("Bone_"))
                {
                    var parts = child.name.Substring("Bone_".Length).Split('_');
                    if (parts.Length != 2) continue;
                    if (!TryGetPlaced(source, parts[0], out var from, out var rotation) ||
                        !TryGetPlaced(source, parts[1], out var to, out _)) continue;
                    child.SetPositionAndRotation(from, rotation);
                    var lr = child.GetComponent<LineRenderer>();
                    if (lr != null)
                    {
                        lr.SetPosition(0, Vector3.zero);
                        lr.SetPosition(1, child.InverseTransformPoint(to));
                    }
                }
            }
        }

        private void DriveFromRestPose()
        {
            var mainRig = SkeletonRig.Instance;
            if (mainRig == null) return;
            foreach (Transform child in standInRoot)
            {
                var source = mainRig.transform.Find(child.name);
                if (source == null) continue;
                child.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
                var lr = child.GetComponent<LineRenderer>();
                var sourceLr = source.GetComponent<LineRenderer>();
                if (lr != null && sourceLr != null)
                {
                    lr.SetPosition(0, sourceLr.GetPosition(0));
                    lr.SetPosition(1, sourceLr.GetPosition(1));
                }
            }
        }

        private static IEnumerable<string> NamesFor(Transform child)
        {
            if (child.name.StartsWith("Joint_"))
            {
                yield return child.name.Substring("Joint_".Length);
            }
            else if (child.name.StartsWith("Bone_"))
            {
                var parts = child.name.Substring("Bone_".Length).Split('_');
                if (parts.Length == 2) { yield return parts[0]; yield return parts[1]; }
            }
        }

        // A stand-in joint's world pose: the source's matching joint, moved by the placement.
        private bool TryGetPlaced(IAvatarPoseSource source, string jointName, out Vector3 position, out Quaternion rotation)
        {
            if (!source.TryGetPose(HumanoidName(jointName), out position, out rotation)) return false;
            position = placementRotation * (position - placementPivot) + placementTarget;
            rotation = placementRotation * rotation;
            if (restInverse.TryGetValue(jointName, out var inv)) rotation *= inv;
            return true;
        }

        // The dance: a Humanoid Animator's live bones, with its zero-muscle T-pose as the rest pose.
        private sealed class AnimatorPoseSource : IAvatarPoseSource
        {
            public readonly Animator Animator;
            private readonly Dictionary<string, Vector3> restPos = new Dictionary<string, Vector3>();
            private readonly Dictionary<string, Quaternion> restRot = new Dictionary<string, Quaternion>();

            public AnimatorPoseSource(Animator animator) => Animator = animator;

            public int RestVersion { get; private set; } = -1;

            public bool Refresh()
            {
                if (Animator == null || !Animator.isHuman || Animator.avatar == null) return false;
                if (RestVersion < 0) CaptureRestPose();
                return true;
            }

            private void CaptureRestPose()
            {
                var handler = new HumanPoseHandler(Animator.avatar, Animator.transform);
                var current = new HumanPose();
                handler.GetHumanPose(ref current);
                var rest = new HumanPose
                {
                    bodyPosition = current.bodyPosition,
                    bodyRotation = Quaternion.identity,
                    muscles = new float[current.muscles.Length],
                };
                handler.SetHumanPose(ref rest);
                foreach (var name in System.Enum.GetNames(typeof(HumanBodyBones)))
                {
                    var t = Bone(name);
                    if (t == null) continue;
                    restPos[name] = t.position;
                    restRot[name] = t.rotation;
                }
                handler.SetHumanPose(ref current);
                handler.Dispose();
                RestVersion = 0;
            }

            public bool TryGetPose(string humanoidBone, out Vector3 position, out Quaternion rotation)
            {
                var t = Bone(humanoidBone);
                if (t == null) { position = default; rotation = Quaternion.identity; return false; }
                position = t.position;
                rotation = t.rotation;
                return true;
            }

            public bool TryGetRestPose(string humanoidBone, out Vector3 position, out Quaternion rotation)
            {
                rotation = Quaternion.identity;
                return restPos.TryGetValue(humanoidBone, out position) && restRot.TryGetValue(humanoidBone, out rotation);
            }

            private Transform Bone(string humanoidBone) =>
                System.Enum.TryParse(humanoidBone, out HumanBodyBones bone) && bone != HumanBodyBones.LastBone
                    ? Animator.GetBoneTransform(bone)
                    : null;
        }
    }
}
