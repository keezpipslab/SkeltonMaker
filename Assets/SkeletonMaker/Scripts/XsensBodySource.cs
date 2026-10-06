using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SkeletonMaker
{
    /// <summary>
    /// A Movella Xsens suit as the tracked body, in place of Meta's body
    /// tracking. Listens for MVN Analyze/Animate's network streamer (UDP,
    /// "MXTP02": position and quaternion of the 23 body segments, see Xsens'
    /// "MVN real-time network streaming protocol specification") - in MVN,
    /// Options > Network Streamer, add this PC's address and the port below
    /// and tick "Position + Orientation (Quaternion)". Nothing of Movella's
    /// has to be installed in the project.
    ///
    /// MVN's world is right-handed with Z up and X forward, in its own place
    /// and heading; the joints are handed out in Unity's axes (Y up, Z
    /// forward), still in MVN's place. Where that is in the scene comes from
    /// the headset, as the suit and the headset are on the same head: the
    /// suit's head is kept on the camera, turned the way the camera looks,
    /// with MVN's floor on the floor the XR rig stands on. Without a headset
    /// MVN's origin is simply this object.
    ///
    /// MVN doesn't send a T-pose along. Its segments all have no rotation in
    /// the T-pose by definition, so a standard T-pose with no rotations is
    /// the rest pose here; only its directions are ever used.
    /// </summary>
    public class XsensBodySource : BodySource
    {
        public enum UpAxis
        {
            Z, // MVN's default
            Y,
        }

        private const int SegmentCount = 23;
        private const int HeaderSize = 24;
        private const int SegmentSize = 32;

        // HumanBodyBones name -> MVN segment index (its ID - 1).
        private static readonly Dictionary<string, int> Segments = new Dictionary<string, int>
        {
            { "Hips", 0 },  // Pelvis
            { "Spine", 2 }, // L3
            { "Chest", 4 }, // T8
            { "Neck", 5 },
            { "Head", 6 },

            { "RightShoulder", 7 },
            { "RightUpperArm", 8 },
            { "RightLowerArm", 9 },
            { "RightHand", 10 },

            { "LeftShoulder", 11 },
            { "LeftUpperArm", 12 },
            { "LeftLowerArm", 13 },
            { "LeftHand", 14 },

            { "RightUpperLeg", 15 },
            { "RightLowerLeg", 16 },
            { "RightFoot", 17 },
            { "RightToes", 18 },

            { "LeftUpperLeg", 19 },
            { "LeftLowerLeg", 20 },
            { "LeftFoot", 21 },
            { "LeftToes", 22 },
        };

        // A T-pose facing +Z, anatomical left on -X: arms straight out, legs straight down.
        private static readonly Dictionary<string, Vector3> Rest = new Dictionary<string, Vector3>
        {
            { "Hips", new Vector3(0f, 0.95f, 0f) },
            { "Spine", new Vector3(0f, 1.08f, 0f) },
            { "Chest", new Vector3(0f, 1.25f, 0f) },
            { "Neck", new Vector3(0f, 1.45f, 0f) },
            { "Head", new Vector3(0f, 1.60f, 0f) },

            { "LeftShoulder", new Vector3(-0.04f, 1.42f, 0f) },
            { "LeftUpperArm", new Vector3(-0.18f, 1.42f, 0f) },
            { "LeftLowerArm", new Vector3(-0.46f, 1.42f, 0f) },
            { "LeftHand", new Vector3(-0.71f, 1.42f, 0f) },

            { "RightShoulder", new Vector3(0.04f, 1.42f, 0f) },
            { "RightUpperArm", new Vector3(0.18f, 1.42f, 0f) },
            { "RightLowerArm", new Vector3(0.46f, 1.42f, 0f) },
            { "RightHand", new Vector3(0.71f, 1.42f, 0f) },

            { "LeftUpperLeg", new Vector3(-0.10f, 0.90f, 0f) },
            { "LeftLowerLeg", new Vector3(-0.10f, 0.48f, 0f) },
            { "LeftFoot", new Vector3(-0.10f, 0.08f, 0f) },
            { "LeftToes", new Vector3(-0.10f, 0.02f, 0.13f) },

            { "RightUpperLeg", new Vector3(0.10f, 0.90f, 0f) },
            { "RightLowerLeg", new Vector3(0.10f, 0.48f, 0f) },
            { "RightFoot", new Vector3(0.10f, 0.08f, 0f) },
            { "RightToes", new Vector3(0.10f, 0.02f, 0.13f) },
        };

        [Tooltip("The UDP port MVN's network streamer sends to (its default).")]
        [SerializeField] private int port = 9763;

        [Tooltip("Which of MVN's characters to follow. -1: whichever is heard first.")]
        [SerializeField] private int characterId = -1;

        [Tooltip("The up axis chosen for the stream in MVN's network streamer options. Z unless it was changed.")]
        [SerializeField] private UpAxis upAxis = UpAxis.Z;

        [Min(0.05f)]
        [Tooltip("With nothing heard for this long (seconds), there is no body.")]
        [SerializeField] private float timeout = 0.5f;

        [Header("Where the suit is in the scene")]
        [Tooltip("Keep the suit's head on the headset. Off (or with no headset running), MVN's origin is this object.")]
        [SerializeField] private bool followHeadset = true;

        [Tooltip("From the suit's head segment (the top of the neck) to between the eyes, in the head's own axes: right, up, forward (meters).")]
        [SerializeField] private Vector3 eyeOffset = new Vector3(0f, 0.11f, 0.09f);

        [Min(0f)]
        [Tooltip("How slowly (seconds) the suit's heading is turned onto the headset's. It only has to follow the suit's drift.")]
        [SerializeField] private float headingSmoothing = 1f;

        [Tooltip("The floor the suit stands on: MVN's height 0 is at this object's height. The camera's rig if left empty.")]
        [SerializeField] private Transform floor;

        // Written by the receive thread, read under the lock.
        private readonly object gate = new object();
        private readonly float[] received = new float[SegmentCount * 7];
        private int receivedMask;
        private long receivedAt;
        private int receivedCharacter = -1;

        private readonly float[] raw = new float[SegmentCount * 7];
        private readonly Vector3[] positions = new Vector3[SegmentCount];
        private readonly Quaternion[] rotations = new Quaternion[SegmentCount];

        private UdpClient client;
        private Thread thread;
        private volatile bool listening;
        private bool valid;
        private int refreshedFrame = -1;
        private bool heard;

        private float heading;
        private bool headingSet;
        private int headingFrame = -1;

        public override int RestVersion => 1; // the one standard T-pose

        private void OnDisable() => End();

        public override void Begin()
        {
            if (client != null) return;
            try
            {
                client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            }
            catch (SocketException e)
            {
                Debug.LogError($"XsensBodySource: couldn't open UDP port {port}: {e.Message}", this);
                client = null;
                return;
            }

            lock (gate)
            {
                receivedMask = 0;
                receivedCharacter = -1;
            }
            heard = false;
            headingSet = false;
            listening = true;
            thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "Xsens receive" };
            thread.Start(client);
            Debug.Log($"XsensBodySource: listening for MVN's network streamer on UDP port {port}.", this);
        }

        public override void End()
        {
            listening = false;
            client?.Close();
            client = null;
            thread?.Join(200);
            thread = null;
            valid = false;
        }

        private void ReceiveLoop(object state)
        {
            var socket = (UdpClient)state;
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (listening)
            {
                try
                {
                    Read(socket.Receive(ref from));
                }
                catch (SocketException)
                {
                    if (!listening) return; // closed
                    Thread.Sleep(1);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        // One datagram: a 24-byte header ("MXTP02", ..., character ID at 16), then 32 bytes for
        // each segment - its ID, position x y z, quaternion w x y z - all big-endian.
        private void Read(byte[] d)
        {
            if (d.Length < HeaderSize + SegmentSize) return;
            if (d[0] != 'M' || d[1] != 'X' || d[2] != 'T' || d[3] != 'P' || d[4] != '0' || d[5] != '2') return;

            int character = d[16];
            lock (gate)
            {
                if (receivedCharacter < 0) receivedCharacter = characterId >= 0 ? characterId : character;
                if (character != receivedCharacter) return;

                for (int at = HeaderSize; at + SegmentSize <= d.Length; at += SegmentSize)
                {
                    int segment = Int(d, at) - 1; // props and fingers come after the body: not used
                    if (segment < 0 || segment >= SegmentCount) continue;
                    for (int i = 0; i < 7; i++)
                        received[segment * 7 + i] = BitConverter.Int32BitsToSingle(Int(d, at + 4 + i * 4));
                    receivedMask |= 1 << segment;
                }
                receivedAt = Stopwatch.GetTimestamp();
            }
        }

        private static int Int(byte[] d, int at) => (d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3];

        public override bool Refresh()
        {
            if (refreshedFrame == Time.frameCount) return valid;
            refreshedFrame = Time.frameCount;
            valid = false;
            if (client == null) return false;

            lock (gate)
            {
                if (receivedMask != (1 << SegmentCount) - 1) return false; // not a whole body yet
                double age = (Stopwatch.GetTimestamp() - receivedAt) / (double)Stopwatch.Frequency;
                if (age > timeout) return false;
                Array.Copy(received, raw, raw.Length);
            }

            for (int i = 0; i < SegmentCount; i++)
            {
                int at = i * 7;
                float x = raw[at], y = raw[at + 1], z = raw[at + 2];
                float qw = raw[at + 3], qx = raw[at + 4], qy = raw[at + 5], qz = raw[at + 6];
                if (upAxis == UpAxis.Z)
                {
                    // MVN: X forward, Y left, Z up. Unity: X right, Y up, Z forward.
                    positions[i] = new Vector3(-y, z, x);
                    rotations[i] = new Quaternion(qy, -qz, -qx, qw);
                }
                else
                {
                    // MVN's Y-up option: X forward, Y up, Z right.
                    positions[i] = new Vector3(z, y, x);
                    rotations[i] = new Quaternion(-qz, -qy, -qx, qw);
                }
            }

            // The specification says centimeters; the stream has been seen in meters too.
            // Nobody's head is 5 m, or 5 cm, above their pelvis.
            float scale = Vector3.Distance(positions[0], positions[6]) > 5f ? 0.01f : 1f;
            if (scale != 1f)
                for (int i = 0; i < SegmentCount; i++) positions[i] *= scale;

            if (!heard)
            {
                heard = true;
                Debug.Log($"XsensBodySource: receiving a body from MVN (positions in {(scale == 1f ? "meters" : "centimeters")}).", this);
            }
            valid = true;
            return true;
        }

        public override bool TryGetPose(string humanoidBone, out Vector3 position, out Quaternion rotation)
        {
            if (valid && Segments.TryGetValue(humanoidBone, out int segment))
            {
                position = positions[segment];
                rotation = rotations[segment];
                return true;
            }
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        public override bool TryGetRestPose(string humanoidBone, out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            return Rest.TryGetValue(humanoidBone, out position);
        }

        public override bool TryGetTrackingToWorld(out Vector3 position, out Quaternion rotation)
        {
            var camera = Camera.main;
            if (!followHeadset || camera == null || !UnityEngine.XR.XRSettings.isDeviceActive)
            {
                position = transform.position;
                rotation = transform.rotation;
                return true;
            }
            if (!valid)
            {
                position = default;
                rotation = Quaternion.identity;
                return false;
            }

            // Heading: turn MVN's world until the suit's head looks the way the headset does.
            // Looking straight up or down says nothing about heading, so those moments are skipped.
            Vector3 suitLook = Vector3.ProjectOnPlane(rotations[6] * Vector3.forward, Vector3.up);
            Vector3 headsetLook = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (headingFrame != Time.frameCount && suitLook.sqrMagnitude > 0.09f && headsetLook.sqrMagnitude > 0.09f)
            {
                headingFrame = Time.frameCount;
                float target = Vector3.SignedAngle(suitLook, headsetLook, Vector3.up);
                if (!headingSet || headingSmoothing <= 0f) heading = target;
                else heading = Mathf.LerpAngle(heading, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime / headingSmoothing));
                headingSet = true;
            }
            if (!headingSet)
            {
                position = default;
                rotation = Quaternion.identity;
                return false;
            }
            rotation = Quaternion.AngleAxis(heading, Vector3.up);

            // Place: the suit's eyes on the headset, MVN's floor on the rig's.
            Vector3 suitEyes = rotation * (positions[6] + rotations[6] * eyeOffset);
            Transform ground = floor != null ? floor : camera.transform.root;
            position = new Vector3(camera.transform.position.x - suitEyes.x, ground.position.y, camera.transform.position.z - suitEyes.z);
            return true;
        }
    }
}
