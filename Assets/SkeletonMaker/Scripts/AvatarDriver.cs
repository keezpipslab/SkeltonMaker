using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Poses one line skeleton (a root with SkeletonRig's Bone_/Joint_ children)
    /// from an IAvatarPoseSource: calibrates the source's T-pose against the
    /// rig's A-pose, then every frame moves each anchor to its source joint
    /// through a placement the owner sets. Shared by the stand-in
    /// (AvatarDanceSource) and each remote performer (RemoteSkeleton).
    /// </summary>
    public sealed class AvatarDriver
    {
        // anchorRotation = placement * sourceBone.rotation * restInverse[joint]; identity-in-A-pose by construction.
        private readonly Dictionary<string, Quaternion> restInverse = new Dictionary<string, Quaternion>();
        private IAvatarPoseSource calibratedSource;
        private int calibratedVersion;

        // Where the source's body goes: p -> rotation * (p - pivot) + target.
        private Quaternion placementRotation = Quaternion.identity;
        private Vector3 placementPivot;
        private Vector3 placementTarget;

        public void Place(Quaternion rotation, Vector3 pivot, Vector3 target)
        {
            placementRotation = rotation;
            placementPivot = pivot;
            placementTarget = target;
        }

        /// <summary>Drops the calibration, so the next EnsureCalibrated redoes it.</summary>
        public void Forget() => calibratedSource = null;

        /// <summary>Recalibrates if the source or its rest pose changed. False while
        /// the source has no usable rest pose yet.</summary>
        public bool EnsureCalibrated(IAvatarPoseSource source, Transform anchorRoot)
        {
            if (source == calibratedSource && source.RestVersion == calibratedVersion) return true;
            if (!Calibrate(source, anchorRoot)) return false;
            calibratedSource = source;
            calibratedVersion = source.RestVersion;
            return true;
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
        private bool Calibrate(IAvatarPoseSource source, Transform anchorRoot)
        {
            if (!source.TryGetRestPose("Hips", out var restHips, out _) ||
                !source.TryGetRestPose("Head", out var restHead, out _) ||
                !TryGetRight(source, true, out var restRight))
                return false;
            var restUp = restHead - restHips;
            var toBody = Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(restRight, restUp), restUp));

            var segments = SkeletonRig.BodySegments;
            restInverse.Clear();
            foreach (Transform child in anchorRoot)
            {
                foreach (var jointName in NamesFor(child))
                {
                    if (restInverse.ContainsKey(jointName)) continue;
                    if (!source.TryGetRestPose(HumanoidName(jointName), out _, out var rotAtRest)) continue;

                    string a = null, b = null;
                    Vector3 aDir = default;
                    foreach (var s in segments)
                        if (s.from == jointName) { a = s.from; b = s.to; aDir = s.direction; break; }
                    if (a == null)
                        foreach (var s in segments)
                            if (s.to == jointName) { a = s.from; b = s.to; aDir = s.direction; break; }
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

        /// <summary>The body's own right-hand direction, from hip to hip (shoulder
        /// to shoulder as a fallback).</summary>
        public static bool TryGetRight(IAvatarPoseSource source, bool rest, out Vector3 right)
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

        public void Drive(IAvatarPoseSource source, Transform anchorRoot)
        {
            foreach (Transform child in anchorRoot)
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

        // An anchor's world pose: the source's matching joint, moved by the placement.
        private bool TryGetPlaced(IAvatarPoseSource source, string jointName, out Vector3 position, out Quaternion rotation)
        {
            if (!source.TryGetPose(HumanoidName(jointName), out position, out rotation)) return false;
            position = placementRotation * (position - placementPivot) + placementTarget;
            rotation = placementRotation * rotation;
            if (restInverse.TryGetValue(jointName, out var inv)) rotation *= inv;
            return true;
        }
    }
}
