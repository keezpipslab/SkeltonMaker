using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// A performer's body as it last arrived over OSC, in the stage frame.
    /// The newest pose simply replaces the one before; one that arrives out
    /// of order is dropped.
    /// </summary>
    public sealed class NetworkPoseSource : IAvatarPoseSource
    {
        private static readonly Dictionary<string, int> Index = BuildIndex();

        private static Dictionary<string, int> BuildIndex()
        {
            var index = new Dictionary<string, int>();
            for (int i = 0; i < OscBody.Joints.Length; i++) index[OscBody.Joints[i]] = i;
            return index;
        }

        private readonly Vector3[] positions = new Vector3[OscBody.Joints.Length];
        private readonly Quaternion[] rotations = new Quaternion[OscBody.Joints.Length];
        private readonly Vector3[] restPositions = new Vector3[OscBody.Joints.Length];
        private readonly Quaternion[] restRotations = new Quaternion[OscBody.Joints.Length];

        private bool hasPose;
        private bool hasRest;
        private int lastSequence;
        private float lastPoseTime;

        /// <summary>The body counts as gone once no pose has arrived for this long (seconds).</summary>
        public float StaleAfter = 1f;

        public int RestVersion { get; private set; }

        public bool Refresh() => hasPose && hasRest && Time.unscaledTime - lastPoseTime <= StaleAfter;

        public void ReadPose(OscMessage message)
        {
            int sequence = message.Int();

            // Older than what is already shown - unless it is so much older that the sender must have
            // started again (or a recording has looped round).
            if (hasPose && sequence <= lastSequence && lastSequence - sequence < 30) return;
            if (!Read(message, positions, rotations)) return;

            lastSequence = sequence;
            lastPoseTime = Time.unscaledTime;
            hasPose = true;
        }

        public void ReadRest(OscMessage message)
        {
            message.Int(); // the sender's own version; what counts here is whether the pose itself changed
            var newPositions = new Vector3[restPositions.Length];
            var newRotations = new Quaternion[restRotations.Length];
            if (!Read(message, newPositions, newRotations)) return;

            bool changed = !hasRest;
            for (int i = 0; i < newPositions.Length && !changed; i++)
                changed = !Same(newPositions[i], restPositions[i]) || newRotations[i] != restRotations[i];
            if (!changed) return;

            newPositions.CopyTo(restPositions, 0);
            newRotations.CopyTo(restRotations, 0);
            hasRest = true;
            RestVersion++;
        }

        private static bool Read(OscMessage message, Vector3[] intoPositions, Quaternion[] intoRotations)
        {
            for (int i = 0; i < intoPositions.Length; i++)
            {
                intoPositions[i] = new Vector3(message.Float(), message.Float(), message.Float());
                intoRotations[i] = new Quaternion(message.Float(), message.Float(), message.Float(), message.Float());
            }
            return !message.Bad;
        }

        // NaN marks a joint the sender doesn't have, and NaN never equals itself.
        private static bool Same(Vector3 a, Vector3 b) => a == b || float.IsNaN(a.x) && float.IsNaN(b.x);

        public bool TryGetPose(string humanoidBone, out Vector3 position, out Quaternion rotation) =>
            TryGet(humanoidBone, hasPose, positions, rotations, out position, out rotation);

        public bool TryGetRestPose(string humanoidBone, out Vector3 position, out Quaternion rotation) =>
            TryGet(humanoidBone, hasRest, restPositions, restRotations, out position, out rotation);

        private static bool TryGet(string humanoidBone, bool has, Vector3[] fromPositions, Quaternion[] fromRotations,
            out Vector3 position, out Quaternion rotation)
        {
            if (has && Index.TryGetValue(humanoidBone, out int i) && !float.IsNaN(fromPositions[i].x))
            {
                position = fromPositions[i];
                rotation = fromRotations[i];
                return true;
            }
            position = default;
            rotation = Quaternion.identity;
            return false;
        }
    }
}
