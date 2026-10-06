using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Keeps the body messages going through an OscLink in a file, each
    /// datagram with the time it passed, for BodyPlayer to bring back later
    /// as a partner. Started and stopped by TestPartner, in the hidden Record
    /// stage. A new recording only replaces the saved one once it is stopped
    /// and has a moving body in it, so a false start costs nothing.
    /// </summary>
    public class BodyRecorder : MonoBehaviour
    {
        public enum Direction
        {
            Sent,     // this instance's own body (works with nobody on the other end)
            Received, // the partner's
        }

        [SerializeField] private OscLink link;
        [SerializeField] private Direction record = Direction.Sent;

        [Tooltip("File name (without extension) under Application.persistentDataPath.")]
        [SerializeField] private string slot = "body-recording";

        [Min(0f)]
        [Tooltip("A recording with less than this (seconds) of body movement in it is thrown away and the saved one kept.")]
        [SerializeField] private float shortestKept = 2f;

        private static readonly byte[] PoseLeaf = System.Text.Encoding.ASCII.GetBytes("/" + OscBody.Pose);

        private BinaryWriter writer;
        private float startedAt;
        private float lastAt;
        private float firstPoseAt;
        private float lastPoseAt;
        private int count;
        private int poses;

        public bool IsRecording => writer != null;

        /// <summary>Seconds since the recording began.</summary>
        public float Elapsed => IsRecording ? Time.unscaledTime - startedAt : 0f;

        /// <summary>How many body poses the recording has in it so far: none means the body isn't being tracked.</summary>
        public int Poses => poses;

        public bool HasSaved => File.Exists(PathFor(slot));

        public static string PathFor(string slot) => Path.Combine(Application.persistentDataPath, slot + ".oscrec");

        private void OnDisable() => StopRecording();

        public void StartRecording()
        {
            if (IsRecording || link == null) return;
            string path = PathFor(slot) + ".tmp";
            try
            {
                writer = new BinaryWriter(File.Create(path));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"BodyRecorder: couldn't write {path}: {e.Message}", this);
                return;
            }

            startedAt = Time.unscaledTime;
            lastAt = 0f;
            count = 0;
            poses = 0;
            if (record == Direction.Sent) link.DatagramSent += OnDatagram;
            else link.DatagramReceived += OnDatagram;
            Debug.Log($"BodyRecorder: recording what is {record.ToString().ToLowerInvariant()}.", this);
        }

        /// <summary>Stops, and makes this the saved recording if there is enough
        /// of a body in it. False if it was thrown away (or nothing was being recorded).</summary>
        public bool StopRecording()
        {
            if (!IsRecording) return false;
            if (link != null)
            {
                link.DatagramSent -= OnDatagram;
                link.DatagramReceived -= OnDatagram;
            }
            writer.Dispose();
            writer = null;

            string path = PathFor(slot);
            string temp = path + ".tmp";
            float moving = poses > 1 ? lastPoseAt - firstPoseAt : 0f;
            try
            {
                if (moving < shortestKept)
                {
                    File.Delete(temp);
                    Debug.Log($"BodyRecorder: thrown away - only {moving:0.0} s of body in it ({poses} pose(s)). The saved recording is unchanged.", this);
                    return false;
                }
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"BodyRecorder: couldn't save {path}: {e.Message}", this);
                return false;
            }
            Debug.Log($"BodyRecorder: saved {count} datagram(s), {lastAt:0.0} s, to {path}", this);
            return true;
        }

        private void OnDatagram(byte[] datagram)
        {
            lastAt = Time.unscaledTime - startedAt;
            if (IsPose(datagram))
            {
                if (poses++ == 0) firstPoseAt = lastAt;
                lastPoseAt = lastAt;
            }
            writer.Write(lastAt);
            writer.Write(datagram.Length);
            writer.Write(datagram);
            count++;
        }

        // A message's address is the zero-ended text it starts with.
        private static bool IsPose(byte[] datagram)
        {
            int end = Array.IndexOf(datagram, (byte)0);
            if (end < PoseLeaf.Length) return false;
            for (int i = 0; i < PoseLeaf.Length; i++)
                if (datagram[end - PoseLeaf.Length + i] != PoseLeaf[i]) return false;
            return true;
        }

        /// <summary>Everything in a recording, in the order it was recorded.
        /// Null if there is no readable file for the slot.</summary>
        public static List<(float time, byte[] datagram)> Load(string slot)
        {
            string path = PathFor(slot);
            if (!File.Exists(path)) return null;

            var recording = new List<(float, byte[])>();
            try
            {
                using var reader = new BinaryReader(File.OpenRead(path));
                long length = reader.BaseStream.Length;
                while (reader.BaseStream.Position + 8 <= length)
                {
                    float time = reader.ReadSingle();
                    int size = reader.ReadInt32();
                    if (size <= 0 || reader.BaseStream.Position + size > length) break; // cut off mid-write
                    recording.Add((time, reader.ReadBytes(size)));
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"BodyRecorder: couldn't read {path}: {e.Message}");
                return null;
            }
            return recording;
        }
    }
}
