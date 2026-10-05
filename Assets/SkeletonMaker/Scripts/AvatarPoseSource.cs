using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>What drives the stand-in avatar: nothing (the frozen A-pose), the
    /// dance Animator, or the player's own tracked body.</summary>
    public enum AvatarMode
    {
        Still,
        Animation,
        Tracking,
    }

    /// <summary>
    /// A body AvatarDanceSource can pose the stand-in from. Joints are addressed
    /// by HumanBodyBones name (anatomical left/right); poses may be in any one
    /// consistent space - the driver only ever uses them relative to each other
    /// and to the rest pose, and places the result on the stand-in itself.
    /// </summary>
    public interface IAvatarPoseSource
    {
        /// <summary>Called once per frame before any pose is read. False while
        /// there is no valid live pose (e.g. body tracking not running yet).</summary>
        bool Refresh();

        /// <summary>Changes whenever the rest pose does, so the driver knows to
        /// recalibrate against it.</summary>
        int RestVersion { get; }

        bool TryGetPose(string humanoidBone, out Vector3 position, out Quaternion rotation);

        /// <summary>The same joint in the source's T-pose.</summary>
        bool TryGetRestPose(string humanoidBone, out Vector3 position, out Quaternion rotation);
    }
}
