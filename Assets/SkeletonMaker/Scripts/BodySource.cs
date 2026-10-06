using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// A live body the avatar's Tracking mode can follow and BodySender can
    /// send: Meta's body tracking (AvatarBodyTrackingSource), an Xsens suit
    /// (XsensBodySource), or the BodyInput that switches between the two.
    /// It only runs between Begin() and End(), and besides the joints it says
    /// where the space they are in sits in the scene.
    /// </summary>
    public abstract class BodySource : MonoBehaviour, IAvatarPoseSource
    {
        /// <summary>Starts the body. Safe to call again to retry.</summary>
        public abstract void Begin();

        public abstract void End();

        /// <summary>May be called more than once in a frame.</summary>
        public abstract bool Refresh();

        public abstract int RestVersion { get; }

        public abstract bool TryGetPose(string humanoidBone, out Vector3 position, out Quaternion rotation);

        public abstract bool TryGetRestPose(string humanoidBone, out Vector3 position, out Quaternion rotation);

        /// <summary>Where the space the joints are tracked in sits in the scene:
        /// world = rotation * tracked + position.</summary>
        public abstract bool TryGetTrackingToWorld(out Vector3 position, out Quaternion rotation);
    }
}
