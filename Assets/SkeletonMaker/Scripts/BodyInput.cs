using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    public enum BodyInputKind
    {
        Meta,
        Xsens,
    }

    /// <summary>
    /// Which body the avatar's Tracking mode follows: Meta's body tracking or
    /// an Xsens suit. It is a BodySource itself and passes everything on to
    /// the chosen one, so AvatarDanceSource and BodySender point here and
    /// never know which it is. X on the keyboard (or Use in the Inspector)
    /// switches, also while tracking: the one that's left is stopped and the
    /// other started.
    /// </summary>
    public class BodyInput : BodySource
    {
        [SerializeField] private BodySource meta;
        [SerializeField] private BodySource xsens;

        [Tooltip("Which body Tracking mode follows. Switched at runtime with X on the keyboard.")]
        [SerializeField] private BodyInputKind use = BodyInputKind.Meta;

        private BodySource current;
        private bool begun;

        public BodyInputKind Use
        {
            get => use;
            set => use = value;
        }

        // The chosen one, or the other if that one isn't there.
        private BodySource Chosen
        {
            get
            {
                var first = use == BodyInputKind.Xsens ? xsens : meta;
                return first != null ? first : use == BodyInputKind.Xsens ? meta : xsens;
            }
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.xKey.wasPressedThisFrame) return;
            use = use == BodyInputKind.Meta ? BodyInputKind.Xsens : BodyInputKind.Meta;
            Debug.Log($"BodyInput: the body now comes from {use}.", this);
        }

        private void OnDisable() => End();

        public override void Begin()
        {
            begun = true;
            current = Chosen;
            if (current != null) current.Begin();
        }

        public override void End()
        {
            begun = false;
            if (current != null) current.End();
            current = null;
        }

        public override bool Refresh()
        {
            var chosen = Chosen;
            if (begun && chosen != current)
            {
                if (current != null) current.End();
                current = chosen;
                if (current != null) current.Begin();
            }
            return current != null && current.Refresh();
        }

        // Never the same for the two bodies, so whoever is posed from this recalibrates on a switch.
        public override int RestVersion => current == null ? -1 : current.RestVersion * 2 + (current == xsens ? 1 : 0);

        public override bool TryGetPose(string humanoidBone, out Vector3 position, out Quaternion rotation)
        {
            if (current != null) return current.TryGetPose(humanoidBone, out position, out rotation);
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        public override bool TryGetRestPose(string humanoidBone, out Vector3 position, out Quaternion rotation)
        {
            if (current != null) return current.TryGetRestPose(humanoidBone, out position, out rotation);
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        public override bool TryGetTrackingToWorld(out Vector3 position, out Quaternion rotation)
        {
            if (current != null) return current.TryGetTrackingToWorld(out position, out rotation);
            position = default;
            rotation = Quaternion.identity;
            return false;
        }
    }
}
