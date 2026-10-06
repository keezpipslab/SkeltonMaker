using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace SkeletonMaker
{
    [ExecuteAlways]
    public class AvatarDanceSource : MonoBehaviour
    {
        [Tooltip("The Humanoid Animator to read bone poses from (e.g. the instantiated Dancing.fbx rig).")]
        [SerializeField] private Animator sourceAnimator;

        [Tooltip("The player's tracked body: Meta's body tracking, an Xsens suit, or the BodyInput that switches between them. There is no Tracking mode while this is unassigned.")]
        [SerializeField] private BodySource bodySource;

        [Tooltip("The stand-in skeleton root whose Bone_/Joint_ children get repositioned. Defaults to this GameObject.")]
        [SerializeField] private Transform standInRoot;

        [Tooltip("Still: the frozen A-pose (matching the main skeleton). Animation: the dance. Tracking: the player's own movement. Chosen at runtime with the three ModeButtons, or set it here directly for testing.")]
        [FormerlySerializedAs("isDancing")]
        [SerializeField] private AvatarMode mode = AvatarMode.Animation;

        [Tooltip("Tracking mode only. Off: the avatar stands at a distance, where the still one does. On: it is worn - every joint sits on the player's own. Flipped at runtime by Y on the left controller (or E).")]
        [SerializeField] private bool embody;

        private readonly AvatarDriver driver = new AvatarDriver();

        private AnimatorPoseSource animatorSource;
        private AvatarMode activeMode = AvatarMode.Still;

        // The driver's placement is fixed once at the start of a mode, so the avatar starts out
        // standing where the still one does and from then on moves exactly as its source does.
        private bool placed;

        private bool activeEmbodied;

        // The skeleton that is built on: the still pose is read off it, also in
        // the stages where it is switched off (Join).
        private SkeletonRig mainRig;

        private SkeletonRig MainRig()
        {
            if (mainRig == null) mainRig = BodyReceiver.MainRig();
            return mainRig;
        }
        private InputAction embodyAction;

        public AvatarMode Mode
        {
            get => mode;
            set => mode = value;
        }

        /// <summary>Whether there is a tracked body to follow at all.</summary>
        public bool CanTrack => bodySource != null;

        public bool Embody
        {
            get => embody;
            set => embody = value;
        }

        private void Reset() => standInRoot = transform;

        private void OnEnable()
        {
            if (standInRoot == null) standInRoot = transform;
            SkeletonRig.EnsureBodyAnchors(standInRoot); // a bone added to the skeleton since the scene was saved
            driver.Forget();
            animatorSource = null;
            activeMode = AvatarMode.Still;
            placed = false;

            // Built here rather than wired in the scene: Y is the one face button still free.
            if (embodyAction == null)
            {
                embodyAction = new InputAction("Embody", InputActionType.Button);
                embodyAction.AddBinding("<XRController>{LeftHand}/{SecondaryButton}");
                embodyAction.AddBinding("<Keyboard>/e");
            }
            embodyAction.Enable();
        }

        private void OnDisable()
        {
            embodyAction.Disable();
            ShowHeadPrimitives(true);
            activeEmbodied = false;
            if (activeMode == AvatarMode.Tracking && bodySource != null) bodySource.End();
            activeMode = AvatarMode.Still;
        }

        private void LateUpdate()
        {
            if (standInRoot == null) standInRoot = transform;
            if (Application.isPlaying && embodyAction.WasPressedThisFrame()) embody = !embody;
            if (mode != activeMode) EnterMode(mode);

            // Only the tracked body can be worn; taking it off again puts it back at its distance.
            bool embodied = embody && activeMode == AvatarMode.Tracking;
            if (embodied != activeEmbodied)
            {
                activeEmbodied = embodied;
                placed = false;
                ShowHeadPrimitives(true); // off again below once the avatar is actually on the player
            }

            var source = ActiveSource();
            if (source == null || !source.Refresh())
            {
                // Nothing to follow (yet): stand still, unless the source dropped out after the
                // avatar had already moved off - then hold its last pose rather than snap back.
                if (!placed) DriveFromRestPose();
                return;
            }

            if (!driver.EnsureCalibrated(source, standInRoot)) { DriveFromRestPose(); return; }
            if (embodied ? !PlaceOnPlayer() : !placed && !Place(source)) { DriveFromRestPose(); return; }
            ShowHeadPrimitives(!embodied);
            driver.Drive(source, standInRoot);
        }

        // Whatever is built on the head would sit around the player's eyes once worn. Switched off
        // as objects, which takes them out of the raymarch too; every frame, to catch new ones.
        private void ShowHeadPrimitives(bool show)
        {
            var head = standInRoot != null ? standInRoot.Find("Bone_Neck_Head") : null;
            if (head == null) return;
            foreach (Transform primitive in head)
                if (primitive.gameObject.activeSelf != show) primitive.gameObject.SetActive(show);
        }

        // Embodied: the tracked joints go exactly where they are in the scene. Redone every frame,
        // as the rig the player stands in can move.
        private bool PlaceOnPlayer()
        {
            if (!bodySource.TryGetTrackingToWorld(out var position, out var rotation)) return false;
            driver.Place(rotation, Vector3.zero, position);
            placed = true;
            return true;
        }

        // Runs however the mode was changed: a ModeButton, a stage, or straight in the Inspector.
        private void EnterMode(AvatarMode next)
        {
            if (bodySource != null && Application.isPlaying)
            {
                if (next == AvatarMode.Tracking) bodySource.Begin();
                else if (activeMode == AvatarMode.Tracking) bodySource.End();
            }
            activeMode = next;
            placed = false;
        }

        private IAvatarPoseSource ActiveSource()
        {
            switch (activeMode)
            {
                case AvatarMode.Animation:
                    if (sourceAnimator == null) return null;
                    if (animatorSource == null || animatorSource.Animator != sourceAnimator)
                        animatorSource = new AnimatorPoseSource(sourceAnimator);
                    return animatorSource;
                case AvatarMode.Tracking:
                    return bodySource;
                default:
                    return null;
            }
        }

        // Puts the source's hips over the still avatar's hips. The dance keeps its own heading and
        // height; the tracked body is also turned to face the way the still avatar does and stood
        // on the stand-in's floor, since the player is somewhere else, facing anywhere, in a
        // tracking space whose floor needn't be the scene's.
        private bool Place(IAvatarPoseSource source)
        {
            var mainRig = MainRig();
            var stillHips = mainRig != null ? mainRig.transform.Find("Bone_Hips_Spine") : null;
            if (stillHips == null || !source.TryGetPose("Hips", out var hips, out _)) return false;

            var up = standInRoot.up;
            var target = standInRoot.TransformPoint(stillHips.localPosition);
            var rotation = Quaternion.identity;
            float height = Vector3.Dot(hips - standInRoot.position, up);

            if (activeMode == AvatarMode.Tracking)
            {
                if (!AvatarDriver.TryGetRight(source, false, out var right)) return false;
                var forward = Vector3.ProjectOnPlane(Vector3.Cross(right, up), up);
                if (forward.sqrMagnitude > 1e-6f)
                    rotation =Quaternion.AngleAxis(Vector3.SignedAngle(forward, standInRoot.forward, up), up);

                // Whichever foot is lowest is taken to be on the ground.
                float lowest = float.MaxValue;
                foreach (var foot in new[] { "LeftFoot", "LeftToes", "RightFoot", "RightToes" })
                    if (source.TryGetPose(foot, out var p, out _)) lowest = Mathf.Min(lowest, Vector3.Dot(p - hips, up));
                float stillLowest = float.MaxValue;
                foreach (Transform child in mainRig.transform)
                {
                    var lr = child.GetComponent<LineRenderer>();
                    if (lr == null) continue;
                    stillLowest = Mathf.Min(stillLowest, (child.localPosition + child.localRotation * lr.GetPosition(1)).y);
                }
                if (lowest < float.MaxValue && stillLowest < float.MaxValue) height = stillLowest - lowest;
            }

            driver.Place(rotation, hips, target + up * (height - Vector3.Dot(target - standInRoot.position, up)));
            placed = true;
            return true;
        }

        private void DriveFromRestPose()
        {
            var mainRig = MainRig();
            if (mainRig == null) return;
            foreach (Transform child in standInRoot)
            {
                var source = mainRig.transform.Find(child.name);
                if (source == null) continue;
                child.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
                var lr = child.GetComponent<LineRenderer>();
                var sourceLr = source.GetComponent<LineRenderer>();
                if (lr != null && sourceLr != null)
                {
                    lr.SetPosition(0, sourceLr.GetPosition(0));
                    lr.SetPosition(1, sourceLr.GetPosition(1));
                }
            }
        }
    }
}
