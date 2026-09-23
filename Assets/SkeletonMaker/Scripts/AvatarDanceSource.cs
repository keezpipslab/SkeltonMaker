using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// Drives the "Avatar Stand-In (Preview)" line skeleton every frame - either
    /// from a Humanoid Animator (dancing, the same pattern the rayMarchVR
    /// project's RaymarchAvatarSource uses to feed a raymarched skeleton from
    /// mocap), or from the main SkeletonRig's own frozen A-pose (its bones never
    /// move, so it's always the authoritative "standing still" reference).
    /// Toggle between the two with either controller's trigger (Activate).
    /// Reads each stand-in bone/joint child's name (the same "Bone_{from}_{to}"
    /// / "Joint_{name}" convention SkeletonRig.Build() creates) to look up the
    /// matching HumanBodyBones transform, so it needs no separate joint list of
    /// its own - it just mirrors whatever SkeletonRig already named things.
    /// </summary>
    [ExecuteAlways]
    public class AvatarDanceSource : MonoBehaviour
    {
        [Tooltip("The Humanoid Animator to read bone poses from (e.g. the instantiated Dancing.fbx rig).")]
        [SerializeField] private Animator sourceAnimator;

        [Tooltip("The stand-in skeleton root whose Bone_/Joint_ children get repositioned. Defaults to this GameObject.")]
        [SerializeField] private Transform standInRoot;

        [Tooltip("Dancing when true; a frozen A-pose (matching the main skeleton) when false. Toggled at runtime by either controller trigger below, or flip it here directly for testing.")]
        [SerializeField] private bool isDancing = true;

        [Tooltip("Either controller's trigger (XRI's \"Activate\" action) toggles dancing on/off - unused elsewhere in this project.")]
        [SerializeField] private InputActionReference leftToggleAction;
        [SerializeField] private InputActionReference rightToggleAction;

        // A Humanoid rig's own bones aren't rotated the same way our own
        // skeleton's bone/joint anchors are (always identity, i.e. "no rotation
        // relative to setup") - a source rig's rest/bind orientation per bone is
        // whatever its original rigger/importer set up (e.g. Mixamo's own
        // convention), which is essentially arbitrary relative to ours. Driving
        // an anchor's rotation straight from the animator bakes that mismatch
        // into every placed duplicate's position *and* rotation (Instantiate
        // rotates a child's local offset by its parent's rotation), which only
        // shows up once dancing since the frozen-A-pose path never touches this
        // at all. Fixed by tracking each bone's rotation *relative to wherever it
        // was the moment dancing last started* - that reference becomes our
        // "zero," matching the identity-rotation anchors the A-pose path uses,
        // and the visible motion afterward is exactly the limb's own relative
        // rotation from that instant, not skewed by the source rig's own setup.
        private readonly Dictionary<string, Quaternion> calibrationOffsets = new Dictionary<string, Quaternion>();
        private bool needsCalibration = true;

        private void Reset() => standInRoot = transform;

        private void OnEnable()
        {
            needsCalibration = true;
            if (leftToggleAction != null) { leftToggleAction.action.Enable(); leftToggleAction.action.performed += OnToggle; }
            if (rightToggleAction != null) { rightToggleAction.action.Enable(); rightToggleAction.action.performed += OnToggle; }
        }

        private void OnDisable()
        {
            if (leftToggleAction != null) leftToggleAction.action.performed -= OnToggle;
            if (rightToggleAction != null) rightToggleAction.action.performed -= OnToggle;
        }

        private void OnToggle(InputAction.CallbackContext ctx)
        {
            isDancing = !isDancing;
            if (isDancing) needsCalibration = true; // re-align to the A-pose look at the instant dancing (re)starts
        }

        private void LateUpdate()
        {
            if (standInRoot == null) standInRoot = transform;

            if (isDancing)
            {
                if (sourceAnimator == null || !sourceAnimator.isHuman) return;
                if (needsCalibration) { Calibrate(); needsCalibration = false; }
                DriveFromAnimator();
            }
            else
            {
                DriveFromRestPose();
            }
        }

        // Captures, once per dancing session, the inverse of each relevant
        // bone's current world rotation - so CorrectedRotation() below can later
        // compute "how far this bone has turned since calibration," starting
        // from identity at the calibration instant itself (matching the A-pose
        // anchors exactly at that moment) rather than the source rig's own
        // arbitrary rest orientation.
        private void Calibrate()
        {
            calibrationOffsets.Clear();
            foreach (Transform child in standInRoot)
            {
                foreach (var jointName in NamesFor(child))
                {
                    if (calibrationOffsets.ContainsKey(jointName)) continue;
                    var bone = BoneTransform(jointName);
                    if (bone != null) calibrationOffsets[jointName] = Quaternion.Inverse(bone.rotation);
                }
            }
        }

        private void DriveFromAnimator()
        {
            foreach (Transform child in standInRoot)
            {
                if (child.name.StartsWith("Joint_"))
                {
                    string jointName = child.name.Substring("Joint_".Length);
                    var bone = BoneTransform(jointName);
                    if (bone == null) continue;

                    // Rotation matters here even though a bare joint has no line
                    // of its own to orient: anything placed on this joint is a
                    // child of it, so its world rotation rides along with
                    // whatever rotation we give the joint (AvatarDuplicateManager
                    // only ever captured a *local* offset, so without this the
                    // limb could swing through a whole dance move while a
                    // decoration stayed pointed the same fixed way in world space).
                    child.SetPositionAndRotation(bone.position, CorrectedRotation(jointName, bone));
                }
                else if (child.name.StartsWith("Bone_"))
                {
                    // "Bone_{from}_{to}" - joint names themselves never contain
                    // underscores, so splitting the remainder on '_' always
                    // yields exactly the two names.
                    var parts = child.name.Substring("Bone_".Length).Split('_');
                    if (parts.Length != 2) continue;

                    var from = BoneTransform(parts[0]);
                    var to = BoneTransform(parts[1]);
                    if (from == null || to == null) continue;

                    // Same reasoning as above - the "from" bone's (corrected)
                    // rotation, not just its position, so anything placed
                    // mid-limb tracks that limb's current swing instead of
                    // staying world-locked.
                    child.SetPositionAndRotation(from.position, CorrectedRotation(parts[0], from));

                    var lr = child.GetComponent<LineRenderer>();
                    if (lr != null)
                    {
                        lr.SetPosition(0, Vector3.zero);
                        lr.SetPosition(1, child.InverseTransformPoint(to.position));
                    }
                }
            }
        }

        // The main SkeletonRig's own Bone_/Joint_ children never move (its A-pose
        // is frozen), so it's always the correct "standing still" reference to
        // copy - no separate cached snapshot needed, and it can never go stale.
        private void DriveFromRestPose()
        {
            var mainRig = SkeletonRig.Instance;
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

        private static IEnumerable<string> NamesFor(Transform child)
        {
            if (child.name.StartsWith("Joint_"))
            {
                yield return child.name.Substring("Joint_".Length);
            }
            else if (child.name.StartsWith("Bone_"))
            {
                var parts = child.name.Substring("Bone_".Length).Split('_');
                if (parts.Length == 2) { yield return parts[0]; yield return parts[1]; }
            }
        }

        private Quaternion CorrectedRotation(string jointName, Transform bone) =>
            calibrationOffsets.TryGetValue(jointName, out var offset) ? bone.rotation * offset : bone.rotation;

        private Transform BoneTransform(string jointName) =>
            System.Enum.TryParse(jointName, out HumanBodyBones bone) ? sourceAnimator.GetBoneTransform(bone) : null;
    }
}
