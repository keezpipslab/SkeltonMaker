using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
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

        // anchorRotation = humanoidBone.rotation * restInverse[joint]; identity-in-A-pose by construction.
        private readonly Dictionary<string, Quaternion> restInverse = new Dictionary<string, Quaternion>();
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

        private void OnToggle(InputAction.CallbackContext ctx) => isDancing = !isDancing;

        private void LateUpdate()
        {
            if (standInRoot == null) standInRoot = transform;
            if (isDancing)
            {
                if (sourceAnimator == null || !sourceAnimator.isHuman) return;
                if (needsCalibration && Calibrate()) needsCalibration = false;
                DriveFromAnimator();
            }
            else
            {
                DriveFromRestPose();
            }
        }

        // The main skeleton's "Left" joints sit on +X, which is the humanoid's *Right* side when both
        // face +Z, so every name is looked up on the opposite side to keep primitives on the same limb.
        private static string HumanoidName(string jointName)
        {
            if (jointName.StartsWith("Left")) return "Right" + jointName.Substring(4);
            if (jointName.StartsWith("Right")) return "Left" + jointName.Substring(5);
            return jointName;
        }

        // Rest orientation = the humanoid's zero-muscle T-pose, converted to the A-pose the main
        // skeleton uses (identity anchors) by swinging each limb onto its A-pose direction.
        private bool Calibrate()
        {
            var mainRig = SkeletonRig.Instance != null ? SkeletonRig.Instance : FindFirstObjectByType<SkeletonRig>();
            if (mainRig == null || sourceAnimator.avatar == null) return false;

            var segments = new List<(string from, string to, Vector3 dir)>();
            foreach (Transform child in mainRig.transform)
            {
                if (!child.name.StartsWith("Bone_")) continue;
                var parts = child.name.Substring("Bone_".Length).Split('_');
                var lr = child.GetComponent<LineRenderer>();
                if (parts.Length != 2 || lr == null) continue;
                var localDir = child.localRotation * (lr.GetPosition(1) - lr.GetPosition(0));
                segments.Add((parts[0], parts[1], standInRoot.TransformDirection(localDir).normalized));
            }

            var restPos = new Dictionary<string, Vector3>();
            var restRot = new Dictionary<string, Quaternion>();
            var handler = new HumanPoseHandler(sourceAnimator.avatar, sourceAnimator.transform);
            var current = new HumanPose();
            handler.GetHumanPose(ref current);
            var rest = new HumanPose
            {
                bodyPosition = current.bodyPosition,
                bodyRotation = Quaternion.identity,
                muscles = new float[current.muscles.Length],
            };
            handler.SetHumanPose(ref rest);
            foreach (var name in System.Enum.GetNames(typeof(HumanBodyBones)))
            {
                var t = BoneByHumanoidName(name);
                if (t == null) continue;
                restPos[name] = t.position;
                restRot[name] = t.rotation;
            }
            handler.SetHumanPose(ref current);
            handler.Dispose();

            restInverse.Clear();
            foreach (Transform child in standInRoot)
            {
                foreach (var jointName in NamesFor(child))
                {
                    if (restInverse.ContainsKey(jointName)) continue;
                    string humanName = HumanoidName(jointName);
                    if (!restRot.TryGetValue(humanName, out var rotAtRest)) continue;

                    string a = null, b = null;
                    Vector3 aDir = default;
                    foreach (var s in segments)
                        if (s.from == jointName) { a = s.from; b = s.to; aDir = s.dir; break; }
                    if (a == null)
                        foreach (var s in segments)
                            if (s.to == jointName) { a = s.from; b = s.to; aDir = s.dir; break; }
                    if (a == null) continue;
                    if (!restPos.TryGetValue(HumanoidName(a), out var pa) || !restPos.TryGetValue(HumanoidName(b), out var pb)) continue;

                    var tDir = (pb - pa).normalized;
                    var swing = Quaternion.FromToRotation(tDir, aDir);
                    restInverse[jointName] = Quaternion.Inverse(swing * rotAtRest) * standInRoot.rotation;
                }
            }
            return true;
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
                    child.SetPositionAndRotation(bone.position, AnchorRotation(jointName, bone));
                }
                else if (child.name.StartsWith("Bone_"))
                {
                    var parts = child.name.Substring("Bone_".Length).Split('_');
                    if (parts.Length != 2) continue;
                    var from = BoneTransform(parts[0]);
                    var to = BoneTransform(parts[1]);
                    if (from == null || to == null) continue;
                    child.SetPositionAndRotation(from.position, AnchorRotation(parts[0], from));
                    var lr = child.GetComponent<LineRenderer>();
                    if (lr != null)
                    {
                        lr.SetPosition(0, Vector3.zero);
                        lr.SetPosition(1, child.InverseTransformPoint(to.position));
                    }
                }
            }
        }

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

        private Quaternion AnchorRotation(string jointName, Transform bone) =>
            restInverse.TryGetValue(jointName, out var inv) ? bone.rotation * inv : bone.rotation;

        private Transform BoneTransform(string jointName) => BoneByHumanoidName(HumanoidName(jointName));

        private Transform BoneByHumanoidName(string humanoidName) =>
            System.Enum.TryParse(humanoidName, out HumanBodyBones bone) && bone != HumanBodyBones.LastBone
                ? sourceAnimator.GetBoneTransform(bone)
                : null;
    }
}
