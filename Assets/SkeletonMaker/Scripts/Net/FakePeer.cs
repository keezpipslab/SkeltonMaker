using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// A second performer with nobody in it, for testing without a second
    /// headset: sends the dance animation as another performer id through the
    /// same OscLink, wearing whatever is built on the local skeleton. With the
    /// link pointing back at this PC it shows up here as a remote skeleton;
    /// pointing at another PC it is that PC's partner.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FakePeer : MonoBehaviour
    {
        [SerializeField] private OscLink link;

        [Tooltip("The Humanoid Animator to copy and send (the avatar's Dance Motion Source). The copy is this object's own, so it keeps dancing whichever stage is showing.")]
        [SerializeField] private Animator template;

        [SerializeField] private int performerId = 2;

        [Tooltip("Where on the stage the dance stands (meters from the Stage Origin), and which way it faces.")]
        [SerializeField] private Vector3 stagePosition = new Vector3(1.2f, 0f, 1.2f);
        [SerializeField] private float stageYaw = 180f;

        [Tooltip("Also send what is built on the local skeleton, so the fake performer has shapes to show.")]
        [SerializeField] private bool wearLocalSkeleton = true;

        private readonly BodyPublisher publisher = new BodyPublisher();
        private Animator animator;
        private AnimatorPoseSource source;
        private SkeletonRig rig;
        private float nextCapture;

        private void Start()
        {
            if (template == null)
            {
                Debug.LogWarning("FakePeer: no Animator to dance with.", this);
                enabled = false;
                return;
            }

            animator = Instantiate(template, transform);
            animator.name = "Fake Peer Dance";
            animator.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            animator.gameObject.SetActive(true);
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // nothing of it is ever on screen
            source = new AnimatorPoseSource(animator);
        }

        private void LateUpdate()
        {
            if (link == null || source == null) return;

            if (wearLocalSkeleton && Time.unscaledTime >= nextCapture)
            {
                nextCapture = Time.unscaledTime + 1f;
                if (rig == null) rig = BodyReceiver.MainRig();
                if (rig != null) publisher.SetComposition(SkeletonComposition.Capture(rig, null));
            }

            // The dance, relative to its own root, set down at the chosen spot on the stage.
            var root = animator.transform;
            Quaternion toStageRotation = Quaternion.Euler(0f, stageYaw, 0f) * Quaternion.Inverse(root.rotation);
            Vector3 toStagePosition = stagePosition - toStageRotation * root.position;

            // Its shift is the local one, so the two cancel and it stands exactly at Stage Position.
            publisher.Publish(link, performerId, source.Refresh() ? source : null,
                toStagePosition, toStageRotation, StageFrame.LocalShift, true);
        }
    }
}
