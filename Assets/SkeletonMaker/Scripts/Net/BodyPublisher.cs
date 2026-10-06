using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The addresses and joint order of the body messages (see the README's
    /// protocol table). Everything on the wire is in the stage frame: metres,
    /// Unity's left-handed axes, Y up, relative to the Stage Origin.
    /// </summary>
    public static class OscBody
    {
        public const string Pose = "pose";
        public const string Rest = "rest";
        public const string State = "state";
        public const string Element = "elem";

        /// <summary>The joints of a pose or rest message, in order, by
        /// HumanBodyBones name (anatomical left and right).</summary>
        public static readonly string[] Joints =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
            "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
            "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
        };

        public static string Address(int performerId, string leaf) => "/skm/" + performerId + "/" + leaf;

        public static bool TryParseAddress(string address, out int performerId, out string leaf)
        {
            performerId = 0;
            leaf = null;
            if (!address.StartsWith("/skm/")) return false;
            int slash = address.IndexOf('/', 5);
            if (slash < 0 || !int.TryParse(address.Substring(5, slash - 5), out performerId)) return false;
            leaf = address.Substring(slash + 1);
            return true;
        }
    }

    /// <summary>
    /// Sends one performer's body: the pose every frame, and once a second (or
    /// straight away when one of them changes) the rest pose, the state and
    /// every element of the skeleton built on it. Repeating instead of
    /// acknowledging means a peer that starts late, or misses a datagram, is
    /// up to date within a second.
    /// </summary>
    public sealed class BodyPublisher
    {
        private const float SlowInterval = 1f;

        private readonly OscWriter writer = new OscWriter();
        private int sequence;
        private float nextSlow;
        private int sentRestVersion = int.MinValue;
        private Vector3 sentShift;

        private SkeletonComposition composition;
        private int revision;
        private bool compositionChanged;

        /// <summary>What is built on this performer's skeleton right now. Cheap
        /// to call with an unchanged composition.</summary>
        public void SetComposition(SkeletonComposition current)
        {
            int currentRevision = Revision(current);
            if (composition != null && currentRevision == revision) return;
            composition = current;
            revision = currentRevision;
            compositionChanged = true;
        }

        /// <summary>source is null while there is no live pose; its joints are
        /// taken to the stage frame by p -> toStageRotation * p + toStagePosition.</summary>
        public void Publish(OscLink link, int performerId, IAvatarPoseSource source,
            Vector3 toStagePosition, Quaternion toStageRotation, Vector3 shift, bool calibrated)
        {
            if (source != null) SendPose(link, performerId, source, toStagePosition, toStageRotation);

            float now = Time.unscaledTime;
            bool restChanged = source != null && source.RestVersion != sentRestVersion;
            if (now < nextSlow && !restChanged && !compositionChanged && shift == sentShift) return;
            nextSlow = now + SlowInterval;
            compositionChanged = false;
            sentShift = shift;

            if (source != null)
            {
                SendRest(link, performerId, source);
                sentRestVersion = source.RestVersion;
            }

            int count = composition != null ? composition.elements.Count : 0;
            writer.Begin(OscBody.Address(performerId, OscBody.State))
                .Int(revision).Int(count).Float(composition != null ? composition.smoothing : 0f)
                .Float(shift.x).Float(shift.y).Float(shift.z)
                .Int(calibrated ? 1 : 0);
            link.Send(writer.ToArray());

            string elementAddress = OscBody.Address(performerId, OscBody.Element);
            for (int i = 0; i < count; i++)
            {
                var e = composition.elements[i];
                writer.Begin(elementAddress).Int(revision).Int(i).String(e.kind).String(e.anchor)
                    .Float(e.size.x).Float(e.size.y).Float(e.size.z)
                    .Float(e.localPosition.x).Float(e.localPosition.y).Float(e.localPosition.z)
                    .Float(e.localRotation.x).Float(e.localRotation.y).Float(e.localRotation.z).Float(e.localRotation.w)
                    .Int(e.painted ? 1 : 0)
                    .Float(e.color.r).Float(e.color.g).Float(e.color.b).Float(e.color.a);
                link.Send(writer.ToArray());
            }
        }

        private void SendPose(OscLink link, int performerId, IAvatarPoseSource source, Vector3 toStagePosition, Quaternion toStageRotation)
        {
            writer.Begin(OscBody.Address(performerId, OscBody.Pose)).Int(sequence++);
            foreach (string joint in OscBody.Joints)
            {
                if (source.TryGetPose(joint, out Vector3 position, out Quaternion rotation))
                {
                    position = toStageRotation * position + toStagePosition;
                    rotation = toStageRotation * rotation;
                }
                else
                {
                    position = new Vector3(float.NaN, float.NaN, float.NaN); // a joint this body doesn't have
                }
                Write(position, rotation);
            }
            link.Send(writer.ToArray());
        }

        // The rest pose stays in the source's own space: only its joints relative to each other matter.
        private void SendRest(OscLink link, int performerId, IAvatarPoseSource source)
        {
            writer.Begin(OscBody.Address(performerId, OscBody.Rest)).Int(source.RestVersion);
            foreach (string joint in OscBody.Joints)
            {
                if (!source.TryGetRestPose(joint, out Vector3 position, out Quaternion rotation))
                    position = new Vector3(float.NaN, float.NaN, float.NaN);
                Write(position, rotation);
            }
            link.Send(writer.ToArray());
        }

        private void Write(Vector3 position, Quaternion rotation) =>
            writer.Float(position.x).Float(position.y).Float(position.z)
                .Float(rotation.x).Float(rotation.y).Float(rotation.z).Float(rotation.w);

        // Any change to what is built gives a different number; that is all the receiver compares.
        private static int Revision(SkeletonComposition current)
        {
            if (current == null) return 0;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + current.smoothing.GetHashCode();
                foreach (var e in current.elements)
                {
                    hash = hash * 31 + Stable(e.kind);
                    hash = hash * 31 + Stable(e.anchor);
                    hash = hash * 31 + e.size.GetHashCode();
                    hash = hash * 31 + e.localPosition.GetHashCode();
                    hash = hash * 31 + e.localRotation.GetHashCode();
                    hash = hash * 31 + (e.painted ? e.color.GetHashCode() : 0);
                }
                return hash;
            }
        }

        private static int Stable(string text)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in text) hash = hash * 31 + c;
                return hash;
            }
        }
    }
}
