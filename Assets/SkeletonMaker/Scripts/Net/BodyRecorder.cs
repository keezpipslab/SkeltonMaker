using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Keeps the body messages going through an OscLink in a file, each
    /// datagram with the time it passed, for BodyPlayer to bring back later
    /// as a partner. R starts and stops for now.
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

        private BinaryWriter writer;
        private float startedAt;
        private float lastAt;
        private int count;

        public bool IsRecording => writer != null;

        public static string PathFor(string slot) => Path.Combine(Application.persistentDataPath, slot + ".oscrec");

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null || !keyboard.rKey.wasPressedThisFrame) return;
            if (IsRecording) StopRecording();
            else StartRecording();
        }

        private void OnDisable() => StopRecording();

        public void StartRecording()
        {
            if (IsRecording || link == null) return;
            string path = PathFor(slot);
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
            if (record == Direction.Sent) link.DatagramSent += OnDatagram;
            else link.DatagramReceived += OnDatagram;
            Debug.Log($"BodyRecorder: recording what is {record.ToString().ToLowerInvariant()} to {path}", this);
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            if (link != null)
            {
                link.DatagramSent -= OnDatagram;
                link.DatagramReceived -= OnDatagram;
            }
            writer.Dispose();
            writer = null;
            Debug.Log($"BodyRecorder: stopped after {count} datagram(s), {lastAt:0.0} s.", this);
        }

        private void OnDatagram(byte[] datagram)
        {
            lastAt = Time.unscaledTime - startedAt;
            writer.Write(lastAt);
            writer.Write(datagram.Length);
            writer.Write(datagram);
            count++;
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
