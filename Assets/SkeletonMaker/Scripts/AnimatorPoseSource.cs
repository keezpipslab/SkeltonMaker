using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>The dance: a Humanoid Animator's live bones, with its zero-muscle
    /// T-pose as the rest pose. Poses are in world space.</summary>
    public sealed class AnimatorPoseSource : IAvatarPoseSource
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
