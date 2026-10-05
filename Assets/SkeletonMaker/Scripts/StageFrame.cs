using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The spot on the floor both performers agree on: this object's position
    /// and heading are the origin of the stage frame, the one space every
    /// body is sent in. (Not a StageController stage - those are the app's
    /// phases.) StageCalibrator moves the XR Origin so that the real floor
    /// mark ends up exactly here; nothing in the scene has to move for it.
    /// Without one in the scene, the stage frame is simply world space.
    /// </summary>
    public class StageFrame : MonoBehaviour
    {
        public static StageFrame Instance { get; private set; }

        [Tooltip("Where this performer is shown to the others, relative to where they really stand on the stage. Each side draws the other moved by (their shift - its own), so (-1,0,0) here and (1,0,0) there keeps the two bodies 2 m further apart than they really are. Zero on both: every skeleton is on its real performer.")]
        [SerializeField] private Vector3 stageShift;

        [Min(0.1f)]
        [Tooltip("How far apart the two floor marks really are (meters). Only used to report how good a calibration was.")]
        [SerializeField] private float markDistance = 1.5f;

        public float MarkDistance => markDistance;

        /// <summary>This performer's own shift (zero without a StageFrame).</summary>
        public static Vector3 LocalShift => Instance != null ? Instance.stageShift : Vector3.zero;

        private void OnEnable() => Instance = this;

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>The stage frame's pose in the scene: world = rotation * stage + position.</summary>
        public static void StageToWorld(out Vector3 position, out Quaternion rotation)
        {
            if (Instance == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }
            Instance.transform.GetPositionAndRotation(out position, out rotation);
        }

        /// <summary>A world-space pose, as seen from the stage frame.</summary>
        public static void WorldToStage(Vector3 worldPosition, Quaternion worldRotation, out Vector3 position, out Quaternion rotation)
        {
            StageToWorld(out Vector3 origin, out Quaternion heading);
            var toStage = Quaternion.Inverse(heading);
            position = toStage * (worldPosition - origin);
            rotation = toStage * worldRotation;
        }
    }
}
