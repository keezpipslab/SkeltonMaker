using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Sends this performer to the other instance: the tracked body, in the
    /// stage frame, every frame it is being tracked (the avatar's Tracking
    /// mode), and the skeleton built on the main rig whenever it changes.
    /// </summary>
    [DefaultExecutionOrder(100)] // after AvatarDanceSource has refreshed the body
    public class BodySender : MonoBehaviour
    {
        [SerializeField] private OscLink link;

        [Tooltip("The player's tracked body. Nothing but the built skeleton is sent while this is unassigned or not tracking.")]
        [SerializeField] private AvatarBodyTrackingSource bodySource;

        [Tooltip("Whose smoothing goes along with the skeleton. Optional.")]
        [SerializeField] private RaymarchQuad quad;

        private readonly BodyPublisher publisher = new BodyPublisher();
        private SkeletonRig rig;
        private float nextCapture;

        private void LateUpdate()
        {
            if (link == null) return;

            // There is no "something was placed" event to hang this on, so just look once a second.
            if (Time.unscaledTime >= nextCapture)
            {
                nextCapture = Time.unscaledTime + 1f;
                if (rig == null) rig = BodyReceiver.MainRig();
                if (rig != null) publisher.SetComposition(SkeletonComposition.Capture(rig, quad));
            }

            Vector3 toStagePosition = default;
            Quaternion toStageRotation = Quaternion.identity;
            bool live = bodySource != null && bodySource.Refresh() &&
                        bodySource.TryGetTrackingToWorld(out toStagePosition, out toStageRotation);
            if (live) StageFrame.WorldToStage(toStagePosition, toStageRotation, out toStagePosition, out toStageRotation);

            publisher.Publish(link, link.PerformerId, live ? bodySource : null,
                toStagePosition, toStageRotation, StageFrame.LocalShift, StageCalibrator.IsCalibrated);
        }
    }
}
