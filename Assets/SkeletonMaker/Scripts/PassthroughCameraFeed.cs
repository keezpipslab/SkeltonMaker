using System;
using Meta.XR;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// The headset's left passthrough camera as something to compute on: the
    /// picture, where the camera was in the scene when it was taken, and the
    /// lens numbers that turn a pixel into a direction. This is what a marker
    /// detector needs (see StageCalibrator.Calibrate).
    ///
    /// The camera only runs between Begin() and End(), so nothing is captured
    /// (and no permission is asked for) until something wants frames. It does
    /// not need the room to be visible - PassthroughView can be showing the
    /// skybox. Quest 3 / 3S only; in the editor it needs Link v85 or newer
    /// with "Passthrough Camera API permissions" switched on.
    ///
    /// V on the keyboard shows the live picture on a small panel in front of
    /// the table, to check that frames arrive at all.
    /// </summary>
    [RequireComponent(typeof(PassthroughCameraAccess))]
    public class PassthroughCameraFeed : MonoBehaviour
    {
        /// <summary>One camera picture and what is needed to measure with it.</summary>
        public struct Frame
        {
            /// <summary>Every pixel, row by row from the bottom row up (Unity's texture order, not OpenCV's).
            /// Only good until the next frame - copy what has to be kept.</summary>
            public NativeArray<Color32> pixels;
            public Vector2Int size;

            /// <summary>Focal length and principal point in pixels of this picture, measured from
            /// its bottom left corner. The picture is already undistorted.</summary>
            public Vector2 focalLength;
            public Vector2 principalPoint;

            /// <summary>The camera in the scene when the picture was taken: looking along +Z, +Y up.</summary>
            public Pose cameraPose;
            public DateTime timestamp;
        }

        [Tooltip("Says where the headset's tracking space sits in the scene (the XR Origin gets moved by StageCalibrator). Without it, camera poses are left in tracking space.")]
        [SerializeField] private AvatarBodyTrackingSource trackingSpace;

        [Tooltip("The panel V switches on, showing the live picture. Optional.")]
        [SerializeField] private Renderer preview;

        private PassthroughCameraAccess access;
        private bool previewing;
        private bool reported;

        /// <summary>True once pictures are coming in.</summary>
        public bool IsPlaying => access != null && access.enabled && access.IsPlaying;

        /// <summary>The latest picture on the GPU, or null while there is none.</summary>
        public Texture Texture => IsPlaying ? access.GetTexture() : null;

        private void Awake()
        {
            access = GetComponent<PassthroughCameraAccess>();
            access.enabled = false; // off until Begin()
            if (preview != null) preview.gameObject.SetActive(false);
        }

        private void OnDisable() => End();

        /// <summary>Starts the camera, asking for the permission first if need be.
        /// Pictures follow a moment later - check IsPlaying.</summary>
        public void Begin()
        {
            if (access.enabled) return;
            if (!PassthroughCameraAccess.IsSupported)
            {
                Debug.LogWarning("PassthroughCameraFeed: this headset has no passthrough camera access (Quest 3 / 3S only).", this);
                return;
            }

            const OVRPermissionsRequester.Permission permission = OVRPermissionsRequester.Permission.PassthroughCameraAccess;
            if (!OVRPermissionsRequester.IsPermissionGranted(permission))
                OVRPermissionsRequester.Request(new[] { permission });

            // The component waits for the permission by itself, and switches
            // itself back off if the camera can't be started.
            reported = false;
            access.enabled = true;
        }

        public void End()
        {
            if (access != null) access.enabled = false;
        }

        /// <summary>The latest picture, if a new one came in this frame. Reads the
        /// pixels back from the GPU, which is slow: ask only while measuring.</summary>
        public bool TryGetFrame(out Frame frame)
        {
            frame = default;
            if (!IsPlaying || !access.IsUpdatedThisFrame) return false;

            var pixels = access.GetColors();
            if (!pixels.IsCreated) return false;

            Vector2Int size = access.CurrentResolution;
            ImageIntrinsics(access.Intrinsics, size, out Vector2 focalLength, out Vector2 principalPoint);
            frame = new Frame
            {
                pixels = pixels,
                size = size,
                focalLength = focalLength,
                principalPoint = principalPoint,
                cameraPose = CameraPose(),
                timestamp = access.Timestamp,
            };
            return true;
        }

        /// <summary>Where the camera was in the scene when the latest picture was taken.</summary>
        public Pose CameraPose()
        {
            // Meta reports it in the headset's tracking space.
            Pose tracked = access.GetCameraPose();
            if (trackingSpace == null || !trackingSpace.TryGetTrackingToWorld(out Vector3 position, out Quaternion rotation))
                return tracked;
            return new Pose(position + rotation * tracked.position, rotation * tracked.rotation);
        }

        // The lens numbers are given for the whole sensor; the picture is the
        // middle of it, cropped to the picture's shape and scaled down.
        private static void ImageIntrinsics(PassthroughCameraAccess.CameraIntrinsics sensor, Vector2Int size,
            out Vector2 focalLength, out Vector2 principalPoint)
        {
            Vector2 sensorSize = sensor.SensorResolution;
            Vector2 fit = new Vector2(size.x / sensorSize.x, size.y / sensorSize.y);
            fit /= Mathf.Max(fit.x, fit.y);
            Vector2 cropSize = Vector2.Scale(sensorSize, fit);
            Vector2 cropCorner = (sensorSize - cropSize) * 0.5f;

            float scale = size.x / cropSize.x;
            focalLength = sensor.FocalLength * scale;
            principalPoint = (sensor.PrincipalPoint - cropCorner) * scale;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.vKey.wasPressedThisFrame)
            {
                previewing = !previewing;
                if (previewing) Begin();
                else End();
                if (preview != null) preview.gameObject.SetActive(previewing);
            }
            if (!previewing) return;

            if (!access.enabled)
            {
                // Couldn't start (the component has said why).
                previewing = false;
                if (preview != null) preview.gameObject.SetActive(false);
                return;
            }
            if (!IsPlaying) return;

            if (preview != null) preview.material.mainTexture = access.GetTexture();
            if (!reported)
            {
                reported = true;
                Vector2Int size = access.CurrentResolution;
                ImageIntrinsics(access.Intrinsics, size, out Vector2 focalLength, out Vector2 principalPoint);
                Pose pose = CameraPose();
                Debug.Log($"PassthroughCameraFeed: {size.x} x {size.y}, focal length {focalLength}, principal point {principalPoint}, " +
                          $"camera at {pose.position:F3} looking along {pose.forward:F2}.", this);
            }
        }
    }
}
