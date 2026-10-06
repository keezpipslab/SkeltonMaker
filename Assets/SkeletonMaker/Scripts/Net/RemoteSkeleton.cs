using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Another performer on the stage, as received over OSC: a line skeleton
    /// with the same Bone_/Joint_ anchors as the stand-in, wearing a ghost of
    /// every primitive they built, standing where they stand on the shared
    /// stage (plus the safe offset - see StageFrame). Made and removed by
    /// BodyReceiver.
    /// </summary>
    public class RemoteSkeleton : MonoBehaviour
    {
        // A safe offset smaller than this (meters) is no safe offset.
        private const float OnTopOfPerformer = 0.5f;

        private readonly NetworkPoseSource source = new NetworkPoseSource();
        private readonly AvatarDriver driver = new AvatarDriver();
        private readonly List<GameObject> ghosts = new List<GameObject>();

        private Transform body;
        private Vector3 shift;

        // The skeleton being received, until every element of it is in.
        private int builtRevision;
        private int builtCount;
        private int pendingRevision;
        private SkeletonComposition.Element[] pending;
        private int pendingCount;

        /// <summary>The root whose children are the anchors (what RaymarchQuad draws).</summary>
        public Transform Body => body;

        public float LastHeard { get; private set; }

        /// <summary>Whether this performer says their stage is calibrated.</summary>
        public bool Calibrated { get; private set; }

        /// <summary>Added on top of the usual offset (BodyReceiver's echo shift, for meeting yourself).</summary>
        public Vector3 ExtraShift { get; set; }

        /// <summary>Whether this could be a person standing in the same room (it came from
        /// another PC), as opposed to a fake peer, an echo or a recording on this one.</summary>
        public bool InTheRoom { get; set; }

        public void Build(float lineWidth, Material lineMaterial, Color lineColor)
        {
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            SkeletonRig.BuildBodyAnchors(body, lineWidth, lineMaterial, lineColor);
            body.gameObject.SetActive(false); // until there is a pose to stand in
            LastHeard = Time.unscaledTime;
        }

        public void Handle(string leaf, OscMessage message)
        {
            LastHeard = Time.unscaledTime;
            switch (leaf)
            {
                case OscBody.Pose: source.ReadPose(message); break;
                case OscBody.Rest: source.ReadRest(message); break;
                case OscBody.State: ReadState(message); break;
                case OscBody.Element: ReadElement(message); break;
            }
        }

        private void ReadState(OscMessage message)
        {
            int revision = message.Int();
            int count = message.Int();
            message.Float(); // their smoothing: one quad has one smoothing, and it stays the local one
            var theirShift = new Vector3(message.Float(), message.Float(), message.Float());
            bool calibrated = message.Int() != 0;
            if (message.Bad || count < 0 || count > RaymarchQuad.MaxShapes) return;

            shift = theirShift;
            Calibrated = calibrated;

            if (revision == builtRevision && builtCount == count) return;
            if (count == 0)
            {
                pending = null;
                Rebuild(revision, System.Array.Empty<SkeletonComposition.Element>());
            }
            else if (pending == null || revision != pendingRevision || pending.Length != count)
            {
                pendingRevision = revision;
                pending = new SkeletonComposition.Element[count];
                pendingCount = 0;
            }
        }

        private void ReadElement(OscMessage message)
        {
            int revision = message.Int();
            int index = message.Int();
            var element = new SkeletonComposition.Element
            {
                kind = message.String(),
                anchor = message.String(),
                size = new Vector3(message.Float(), message.Float(), message.Float()),
                localPosition = new Vector3(message.Float(), message.Float(), message.Float()),
                localRotation = new Quaternion(message.Float(), message.Float(), message.Float(), message.Float()),
                painted = message.Int() != 0,
                color = new Color(message.Float(), message.Float(), message.Float(), message.Float()),
            };
            if (message.Bad || pending == null || revision != pendingRevision) return;
            if (index < 0 || index >= pending.Length || pending[index] != null) return;

            pending[index] = element;
            if (++pendingCount < pending.Length) return;

            var complete = pending;
            pending = null;
            Rebuild(revision, complete);
        }

        private void Rebuild(int revision, SkeletonComposition.Element[] elements)
        {
            foreach (var ghost in ghosts)
                if (ghost != null) Destroy(ghost);
            ghosts.Clear();
            builtRevision = revision;
            builtCount = elements.Length;

            var manager = AvatarDuplicateManager.Instance;
            if (manager == null)
            {
                if (elements.Length > 0) Debug.LogWarning("RemoteSkeleton: no AvatarDuplicateManager in the scene, so the remote skeleton stays bare.", this);
                return;
            }

            foreach (var element in elements)
            {
                Transform anchor = body.Find(element.anchor);
                var ghost = anchor != null ? manager.BuildGhost(element, anchor) : null;
                if (ghost != null) ghosts.Add(ghost);
                else Debug.LogWarning($"RemoteSkeleton: couldn't build '{element.kind}' on '{element.anchor}', skipped.", this);
            }
        }

        private void OnDestroy() => PassthroughView.Require(this, false);

        private void LateUpdate()
        {
            if (body == null) return;
            if (!source.Refresh() || !driver.EnsureCalibrated(source, body))
            {
                // Gone quiet: better no body than one frozen mid-step.
                if (body.gameObject.activeSelf) body.gameObject.SetActive(false);
                PassthroughView.Require(this, false);
                return;
            }

            // A stage point p stands at origin + heading * (p + offset).
            StageFrame.StageToWorld(out Vector3 origin, out Quaternion heading);
            Vector3 offset = shift - StageFrame.LocalShift + ExtraShift;
            driver.Place(heading, -offset, origin);

            // Drawn (nearly) where the performer really stands: in one room that is
            // a person to walk into, so the room has to be visible.
            PassthroughView.Require(this, InTheRoom && offset.sqrMagnitude < OnTopOfPerformer * OnTopOfPerformer);
            if (!body.gameObject.activeSelf) body.gameObject.SetActive(true);
            driver.Drive(source, body);
        }
    }
}
