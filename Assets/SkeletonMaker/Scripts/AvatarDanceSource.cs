using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Drives the "Avatar Stand-In (Preview)" line skeleton from a Humanoid
    /// Animator every frame - the same pattern the rayMarchVR project's
    /// RaymarchAvatarSource uses to feed a raymarched skeleton from mocap.
    /// Reads each stand-in bone/joint child's name (the same "Bone_{from}_{to}"
    /// / "Joint_{name}" convention SkeletonRig.Build() creates) to look up the
    /// matching HumanBodyBones transform, so it needs no separate joint list of
    /// its own - it just mirrors whatever SkeletonRig already named things.
    /// </summary>
    [ExecuteAlways]
    public class AvatarDanceSource : MonoBehaviour
    {
        [Tooltip("The Humanoid Animator to read bone poses from (e.g. the instantiated Dancing.fbx rig).")]
        [SerializeField] private Animator sourceAnimator;

        [Tooltip("The stand-in skeleton root whose Bone_/Joint_ children get repositioned. Defaults to this GameObject.")]
        [SerializeField] private Transform standInRoot;

        private void Reset() => standInRoot = transform;

        private void LateUpdate()
        {
            if (sourceAnimator == null || !sourceAnimator.isHuman) return;
            if (standInRoot == null) standInRoot = transform;

            foreach (Transform child in standInRoot)
            {
                if (child.name.StartsWith("Joint_"))
                {
                    var bone = BoneTransform(child.name.Substring("Joint_".Length));
                    if (bone == null) continue;

                    // Rotation matters here even though a bare joint has no line
                    // of its own to orient: anything placed on this joint is a
                    // child of it, so its world rotation rides along with
                    // whatever rotation we give the joint (AvatarDuplicateManager
                    // only ever captured a *local* offset, so without this the
                    // limb could swing through a whole dance move while a
                    // decoration stayed pointed the same fixed way in world space).
                    child.SetPositionAndRotation(bone.position, bone.rotation);
                }
                else if (child.name.StartsWith("Bone_"))
                {
                    // "Bone_{from}_{to}" - joint names themselves never contain
                    // underscores, so splitting the remainder on '_' always
                    // yields exactly the two names.
                    var parts = child.name.Substring("Bone_".Length).Split('_');
                    if (parts.Length != 2) continue;

                    var from = BoneTransform(parts[0]);
                    var to = BoneTransform(parts[1]);
                    if (from == null || to == null) continue;

                    // Same reasoning as above - the "from" bone's rotation, not
                    // just its position, so anything placed mid-limb tracks that
                    // limb's current swing instead of staying world-locked.
                    child.SetPositionAndRotation(from.position, from.rotation);

                    var lr = child.GetComponent<LineRenderer>();
                    if (lr != null)
                    {
                        lr.SetPosition(0, Vector3.zero);
                        lr.SetPosition(1, child.InverseTransformPoint(to.position));
                    }
                }
            }
        }

        private Transform BoneTransform(string jointName) =>
            System.Enum.TryParse(jointName, out HumanBodyBones bone) ? sourceAnimator.GetBoneTransform(bone) : null;
    }
}
