using System.Collections.Generic;
using Meta.XR.Movement.Retargeting;
using UnityEngine;
using BoneId = Meta.XR.Movement.Retargeting.SkeletonData.FullBodyTrackingBoneId;

namespace SkeletonMaker
{
    /// <summary>
    /// The player's own tracked body (Meta Movement SDK body tracking) as a pose
    /// source for the stand-in. Reads the full-body skeleton off the Movement
    /// SDK's MetaSourceDataProvider on this GameObject: the live joints in
    /// tracking space, and the provider's T-pose as the rest reference.
    /// Body tracking only runs between Begin() and End(), so nothing is tracked
    /// (and no permission is asked for) until the mode is actually used.
    /// </summary>
    [RequireComponent(typeof(MetaSourceDataProvider))]
    public class AvatarBodyTrackingSource : MonoBehaviour, IAvatarPoseSource
    {
        // HumanBodyBones name -> tracked joint, the same pairing Meta's own
        // humanoid retargeting uses.
        private static readonly Dictionary<string, BoneId> Bones = new Dictionary<string, BoneId>
        {
            { "Hips", BoneId.Hips },
            { "Spine", BoneId.SpineLower },
            { "Chest", BoneId.SpineUpper },
            { "Neck", BoneId.Neck },
            { "Head", BoneId.Head },

            { "LeftShoulder", BoneId.LeftShoulder },
            { "LeftUpperArm", BoneId.LeftArmUpper },
            { "LeftLowerArm", BoneId.LeftArmLower },
            { "LeftHand", BoneId.LeftHandWrist },

            { "RightShoulder", BoneId.RightShoulder },
            { "RightUpperArm", BoneId.RightArmUpper },
            { "RightLowerArm", BoneId.RightArmLower },
            { "RightHand", BoneId.RightHandWrist },

            { "LeftUpperLeg", BoneId.LeftUpperLeg },
            { "LeftLowerLeg", BoneId.LeftLowerLeg },
            { "LeftFoot", BoneId.LeftFootAnkle },
            { "LeftToes", BoneId.LeftFootBall },

            { "RightUpperLeg", BoneId.RightUpperLeg },
            { "RightLowerLeg", BoneId.RightLowerLeg },
            { "RightFoot", BoneId.RightFootAnkle },
            { "RightToes", BoneId.RightFootBall },
        };

        private MetaSourceDataProvider provider;
        private OVRSkeleton.SkeletonPoseData pose;
        private bool poseValid;
        private bool wanted;

        private Vector3[] restPositions;
        private Quaternion[] restRotations;
        private int skeletonChangedCount = -1;

        public int RestVersion { get; private set; } = -1;

        private void Awake()
        {
            provider = GetComponent<MetaSourceDataProvider>();
            provider.ProvidedSkeletonType = OVRPlugin.BodyJointSet.FullBody;
            provider.enabled = false;
        }

        private void OnEnable() => OVRPermissionsRequester.PermissionGranted += OnPermissionGranted;

        private void OnDisable()
        {
            OVRPermissionsRequester.PermissionGranted -= OnPermissionGranted;
            End();
        }

        /// <summary>Starts body tracking, asking for the permission first if the
        /// player hasn't granted it yet. Safe to call again to retry.</summary>
        public void Begin()
        {
            wanted = true;
            const OVRPermissionsRequester.Permission permission = OVRPermissionsRequester.Permission.BodyTracking;
            if (!OVRPermissionsRequester.IsPermissionGranted(permission))
            {
                OVRPermissionsRequester.Request(new[] { permission });
                return; // OnPermissionGranted starts tracking
            }

            // OVRBody switches itself back off if tracking can't start (no
            // headset, feature unsupported); the avatar then just stays still.
            provider.enabled = true;
        }

        public void End()
        {
            wanted = false;
            if (provider != null) provider.enabled = false;
            poseValid = false;
        }

        private void OnPermissionGranted(string permissionId)
        {
            if (wanted && permissionId == OVRPermissionsRequester.BodyTrackingPermission) Begin();
        }

        public bool Refresh()
        {
            poseValid = false;
            if (provider == null || !provider.enabled) return false;

            pose = ((OVRSkeleton.IOVRSkeletonDataProvider)provider).GetSkeletonPoseData();
            if (!pose.IsDataValid || pose.BoneTranslations == null || pose.BoneTranslations.Length < (int)BoneId.End)
                return false;

            // The T-pose carries the player's own proportions, which the
            // runtime re-estimates now and then.
            if (restPositions == null || pose.SkeletonChangedCount != skeletonChangedCount)
            {
                if (!ReadRestPose()) return false;
                skeletonChangedCount = pose.SkeletonChangedCount;
            }

            poseValid = true;
            return true;
        }

        private bool ReadRestPose()
        {
            var tPose = provider.GetSkeletonTPose();
            if (!tPose.IsCreated || tPose.Length < (int)BoneId.End) return false;

            restPositions = new Vector3[tPose.Length];
            restRotations = new Quaternion[tPose.Length];
            for (int i = 0; i < tPose.Length; i++)
            {
                restPositions[i] = tPose[i].Position;
                restRotations[i] = tPose[i].Orientation;
            }
            RestVersion++;
            return true;
        }

        public bool TryGetPose(string humanoidBone, out Vector3 position, out Quaternion rotation)
        {
            if (poseValid && Bones.TryGetValue(humanoidBone, out var id))
            {
                position = pose.BoneTranslations[(int)id].FromFlippedZVector3f();
                rotation = pose.BoneRotations[(int)id].FromFlippedZQuatf();
                return true;
            }
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        public bool TryGetRestPose(string humanoidBone, out Vector3 position, out Quaternion rotation)
        {
            if (restPositions != null && Bones.TryGetValue(humanoidBone, out var id))
            {
                position = restPositions[(int)id];
                rotation = restRotations[(int)id];
                return true;
            }
            position = default;
            rotation = Quaternion.identity;
            return false;
        }
    }
}
