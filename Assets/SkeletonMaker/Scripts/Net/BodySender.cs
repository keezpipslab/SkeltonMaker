using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Sends this performer to the other instance: the body, in the stage
    /// frame, every frame, and the skeleton built on the main rig whenever it
    /// changes. The body is the tracked one (Meta's body tracking or the Xsens
    /// suit) while it is tracked, where the performer really stands; otherwise
    /// whatever moves the avatar - the dance - where the avatar is. Nothing
    /// while the avatar stands still.
    /// </summary>
    [DefaultExecutionOrder(100)] // after AvatarDanceSource has refreshed the body
    public class BodySender : MonoBehaviour
    {
        [SerializeField] private OscLink link;

        [Tooltip("The player's tracked body. Nothing but the built skeleton is sent while this is unassigned or not tracking.")]
        [SerializeField] private BodySource bodySource;

        [Tooltip("The avatar, whose dance is sent while the body isn't tracked. Found in the scene if left empty.")]
        [SerializeField] private AvatarDanceSource avatar;

        [Tooltip("Send the avatar's dance while the body isn't tracked. Off: only a tracked body is sent.")]
        [SerializeField] private bool sendDance = true;

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
            IAvatarPoseSource body = null;
            if (bodySource != null && bodySource.Refresh() &&
                bodySource.TryGetTrackingToWorld(out toStagePosition, out toStageRotation))
            {
                body = bodySource;
            }
            else if (sendDance)
            {
                if (avatar == null) avatar = FindFirstObjectByType<AvatarDanceSource>(FindObjectsInactive.Include);
                if (avatar != null && avatar.TryGetLiveBody(out var danced, out toStagePosition, out toStageRotation))
                    body = danced;
            }
            if (body != null) StageFrame.WorldToStage(toStagePosition, toStageRotation, out toStagePosition, out toStageRotation);

            publisher.Publish(link, link.PerformerId, body,
                toStagePosition, toStageRotation, StageFrame.LocalShift, StageCalibrator.IsCalibrated);
        }
    }
}
