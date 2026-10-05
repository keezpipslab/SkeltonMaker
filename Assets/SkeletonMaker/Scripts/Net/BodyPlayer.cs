using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Plays a BodyRecorder file back into the OscLink as if it were arriving
    /// from the other instance, over and over: a partner to rehearse against
    /// with nobody there. Started and stopped by TestPartner.
    /// </summary>
    public class BodyPlayer : MonoBehaviour
    {
        [SerializeField] private OscLink link;

        [Tooltip("File name (without extension) under Application.persistentDataPath - the BodyRecorder's slot.")]
        [SerializeField] private string slot = "body-recording";

        [Tooltip("Who the recorded performer comes back as - a recording of yourself has your own id, which is otherwise ignored. If more than one performer was recorded, the next ones count up from here. 0 keeps the ids as recorded.")]
        [SerializeField] private int playAsId = 10;

        [SerializeField] private bool loop = true;

        private List<(float time, byte[] datagram)> recording;
        private readonly List<OscMessage> messages = new List<OscMessage>();
        private readonly Dictionary<int, int> playedIds = new Dictionary<int, int>();
        private int next;
        private float startedAt;

        public bool IsPlaying => recording != null;

        /// <summary>The first performer id a recording comes back as.</summary>
        public int PlayAsId => playAsId;

        private void Update()
        {
            if (!IsPlaying) return;
            float elapsed = Time.unscaledTime - startedAt;
            while (next < recording.Count && recording[next].time <= elapsed) Play(recording[next++].datagram);
            if (next < recording.Count) return;

            if (loop)
            {
                next = 0;
                startedAt = Time.unscaledTime;
            }
            else
            {
                StopPlaying();
            }
        }

        private void OnDisable() => recording = null;

        /// <summary>Plays the saved recording from its start (again, if it is
        /// already playing). False if there is none.</summary>
        public bool StartPlaying()
        {
            if (link == null) return false;
            var loaded = BodyRecorder.Load(slot);
            if (loaded == null || loaded.Count == 0)
            {
                Debug.Log($"BodyPlayer: nothing recorded yet at {BodyRecorder.PathFor(slot)}", this);
                return false;
            }

            recording = loaded;
            playedIds.Clear();
            next = 0;
            startedAt = Time.unscaledTime;
            Debug.Log($"BodyPlayer: playing {recording.Count} datagram(s), {recording[recording.Count - 1].time:0.0} s.", this);
            return true;
        }

        public void StopPlaying()
        {
            if (!IsPlaying) return;
            recording = null;
            Debug.Log("BodyPlayer: stopped.", this);
        }

        private void Play(byte[] datagram)
        {
            messages.Clear();
            OscCodec.Parse(datagram, messages);
            foreach (var message in messages)
            {
                if (playAsId > 0 && OscBody.TryParseAddress(message.Address, out int recordedId, out string leaf))
                {
                    if (!playedIds.TryGetValue(recordedId, out int id)) playedIds[recordedId] = id = playAsId + playedIds.Count;
                    message.Address = OscBody.Address(id, leaf);
                }
                link.Deliver(message);
            }
        }
    }
}
