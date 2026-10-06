using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// The other performer for when there is nobody on the other end: a
    /// recording of yourself. It is made in the hidden Record stage (H) and
    /// kept on disk, and from then on it is who appears in the Join and
    /// Together Math stages whenever the OscLink's Remote Host is this PC itself. Until something
    /// has been recorded the fake peer dances there instead.
    ///
    /// In the Record stage the saved recording plays, so it can be checked.
    /// A on the right controller (or R) starts a new one after a countdown,
    /// and stops and saves it; one with hardly any body in it is thrown away
    /// and the saved one kept. Both stages put the avatar in Tracking mode -
    /// the body is only sent while it is tracked - and give it its old mode
    /// back afterwards.
    /// </summary>
    public class TestPartner : MonoBehaviour
    {
        [SerializeField] private OscLink link;
        [SerializeField] private BodyRecorder recorder;
        [SerializeField] private BodyPlayer player;
        [SerializeField] private BodyReceiver receiver;

        [Tooltip("Dances in the Join stage while nothing has been recorded yet. Kept switched off otherwise.")]
        [SerializeField] private GameObject fakePeer;

        [Tooltip("The Record stage's text.")]
        [SerializeField] private TextMesh label;

        [Tooltip("Where the recording stands, in stage meters from where you stood while recording it - without it, it would be standing right where you are.")]
        [SerializeField] private Vector3 recordingShift = new Vector3(1.2f, 0f, 0.6f);

        [Min(0f)]
        [SerializeField] private float countdown = 3f;

        private StageController stages;
        private AvatarDanceSource avatar;
        private InputAction recordAction;

        private bool withPartner;
        private bool meeting; // the recording or the fake peer has been brought on (Join, Together Math)
        private AvatarMode modeBefore;
        private float recordAt = -1f; // when the countdown runs out
        private string note = "";

        private void OnEnable()
        {
            if (recordAction == null)
            {
                recordAction = new InputAction("Record", InputActionType.Button);
                recordAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
                recordAction.AddBinding("<Keyboard>/r");
            }
            recordAction.Enable();
        }

        private void OnDisable() => recordAction.Disable();

        private void Start()
        {
            avatar = FindFirstObjectByType<AvatarDanceSource>(FindObjectsInactive.Include);
            if (receiver != null && player != null) receiver.ShiftFrom(player.PlayAsId, recordingShift);

            stages = StageController.Instance;
            if (stages == null) return;
            stages.Changed += OnStageChanged;
            OnStageChanged(stages.Current);
        }

        private void OnDestroy()
        {
            if (stages != null) stages.Changed -= OnStageChanged;
        }

        private void OnStageChanged(Stage stage)
        {
            // Whatever was going on in the stage that was left.
            recordAt = -1f;
            note = "";
            if (recorder != null && recorder.IsRecording) recorder.StopRecording();

            // From Join into Together Math the same partner simply carries on.
            bool together = stage == Stage.Join || stage == Stage.TogetherMath;
            bool meets = together && link != null && link.RemoteIsThisPc;
            bool carriesOn = meets && meeting;
            meeting = meets;
            if (!carriesOn)
            {
                if (player != null) player.StopPlaying();
                if (fakePeer != null) fakePeer.SetActive(false);
            }

            bool partner = together || stage == Stage.Record;
            if (partner != withPartner && avatar != null)
            {
                if (partner)
                {
                    modeBefore = avatar.Mode;
                    if (avatar.CanTrack) avatar.Mode = AvatarMode.Tracking;
                }
                else
                {
                    avatar.Mode = modeBefore;
                }
            }
            withPartner = partner;

            if (stage == Stage.Record)
            {
                if (player != null) player.StartPlaying();
            }
            else if (meets && !carriesOn)
            {
                // Nobody on the other end: the recording, or failing that the dance.
                bool playing = player != null && player.StartPlaying();
                if (!playing && fakePeer != null) fakePeer.SetActive(true);
            }
        }

        private void Update()
        {
            if (stages == null || stages.Current != Stage.Record || recorder == null) return;

            if (recordAction.WasPressedThisFrame())
            {
                if (recorder.IsRecording) Stop();
                else if (recordAt >= 0f) recordAt = -1f; // pressed again during the countdown: never mind
                else Begin();
            }

            if (recordAt >= 0f && Time.unscaledTime >= recordAt)
            {
                recordAt = -1f;
                recorder.StartRecording();
            }

            if (label != null) label.text = Text();
        }

        private void LateUpdate()
        {
            if (label == null || !label.gameObject.activeInHierarchy) return;

            // Readable from wherever the viewer is.
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 away = label.transform.position - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-6f) label.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        private void Begin()
        {
            if (player != null) player.StopPlaying(); // not in the way while recording
            note = "";
            recordAt = Time.unscaledTime + countdown;
        }

        private void Stop()
        {
            float seconds = recorder.Elapsed;
            note = recorder.StopRecording()
                ? $"Saved, {seconds:0} s."
                : "Not saved: there was no body in it.\nThe earlier recording is kept.";
            if (player != null) player.StartPlaying();
        }

        private string Text()
        {
            if (recorder.IsRecording)
            {
                string text = $"RECORDING  {recorder.Elapsed:0} s\n\nA (or R) stops and saves it.";
                if (recorder.Poses == 0 && recorder.Elapsed > 1f) text += "\n\nNo body yet - is tracking running?";
                return text;
            }

            if (recordAt >= 0f) return $"Recording in {Mathf.CeilToInt(recordAt - Time.unscaledTime)}";

            string saved = recorder.HasSaved
                ? "The saved recording is playing.\nA (or R) records a new one."
                : "Nothing recorded yet.\nA (or R) starts recording.";
            return $"Record yourself\n\n{saved}\nB (or H) goes back."
                   + (note.Length > 0 ? "\n\n" + note : "");
        }
    }
}
