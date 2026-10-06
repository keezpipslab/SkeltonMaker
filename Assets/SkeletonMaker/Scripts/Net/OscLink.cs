using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The one UDP socket this instance talks OSC through: everything is sent
    /// to Remote Host : Remote Port and everything arriving on Local Port is
    /// handed out, on the main thread, through Received. There is no
    /// connection - both sides just send, and whoever listens gets it.
    ///
    /// Two PCs: each one's Remote Host is the other's address, same port on
    /// both. One PC on its own: leave Remote Host on 127.0.0.1 with both ports
    /// equal, and everything sent comes straight back in.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class OscLink : MonoBehaviour
    {
        [Tooltip("Who this instance is on the stage. Must differ between the two PCs.")]
        [SerializeField] private int performerId = 1;

        [SerializeField] private int localPort = 9000;
        [SerializeField] private string remoteHost = "127.0.0.1";
        [SerializeField] private int remotePort = 9000;

        [Tooltip("Also show this instance's own body when it comes back in (Remote Host pointing at this PC): meeting yourself.")]
        [SerializeField] private bool acceptOwnId;

        [Header("Simulated network (applied to what comes in)")]
        [Min(0f)]
        [Tooltip("Seconds everything received is held back. A few seconds turns your own body into a partner that follows you.")]
        [SerializeField] private float delay;
        [Min(0f)]
        [Tooltip("Up to this many extra seconds, different for every datagram, so they can also arrive out of order.")]
        [SerializeField] private float jitter;
        [Range(0f, 1f)]
        [Tooltip("The share of datagrams thrown away.")]
        [SerializeField] private float loss;

        public int PerformerId => performerId;
        public bool AcceptOwnId => acceptOwnId;

        /// <summary>True while Remote Host is this PC itself: everyone on the stage is
        /// then a test (the fake peer, an echo, a recording), not a person in the room.</summary>
        public bool RemoteIsThisPc =>
            remoteHost == "localhost" || (IPAddress.TryParse(remoteHost, out IPAddress address) && IPAddress.IsLoopback(address));

        /// <summary>Every message that came in, on the main thread.</summary>
        public event Action<OscMessage> Received;

        /// <summary>The raw datagrams behind them, sent and received (what BodyRecorder keeps).</summary>
        public event Action<byte[]> DatagramSent;
        public event Action<byte[]> DatagramReceived;

        private UdpClient client;
        private Thread receiveThread;
        private volatile bool listening;
        private readonly ConcurrentQueue<byte[]> arrived = new ConcurrentQueue<byte[]>();
        private readonly List<(float due, byte[] datagram)> held = new List<(float, byte[])>();
        private readonly List<OscMessage> messages = new List<OscMessage>();

        private IPEndPoint remote;
        private string resolvedHost;
        private int resolvedPort;

        private void OnEnable()
        {
            try
            {
                client = new UdpClient(new IPEndPoint(IPAddress.Any, localPort));
            }
            catch (SocketException e)
            {
                Debug.LogError($"OscLink: couldn't open UDP port {localPort}: {e.Message}", this);
                client = null;
                return;
            }

            // Windows otherwise fails the next receive whenever something sent had nobody listening.
            try { client.Client.IOControl(-1744830452 /* SIO_UDP_CONNRESET */, new byte[] { 0 }, null); }
            catch (Exception e) when (e is SocketException || e is PlatformNotSupportedException || e is NotSupportedException) { }

            listening = true;
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "OscLink receive" };
            receiveThread.Start(client);
        }

        private void OnDisable()
        {
            listening = false;
            client?.Close();
            client = null;
            receiveThread?.Join(200);
            receiveThread = null;
            held.Clear();
            while (arrived.TryDequeue(out _)) { }
        }

        private void ReceiveLoop(object state)
        {
            var socket = (UdpClient)state;
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (listening)
            {
                try
                {
                    arrived.Enqueue(socket.Receive(ref from));
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

        public void Send(byte[] datagram)
        {
            if (client == null || !TryResolveRemote()) return;
            try
            {
                client.Send(datagram, datagram.Length, remote);
            }
            catch (SocketException)
            {
                return; // no route just now; the next one may get through
            }
            DatagramSent?.Invoke(datagram);
        }

        /// <summary>Hands a message out as if it had just come in (BodyPlayer).</summary>
        public void Deliver(OscMessage message) => Received?.Invoke(message);

        private bool TryResolveRemote()
        {
            if (remote != null && resolvedHost == remoteHost && resolvedPort == remotePort) return true;

            resolvedHost = remoteHost;
            resolvedPort = remotePort;
            remote = null;
            if (!IPAddress.TryParse(remoteHost, out IPAddress address))
            {
                try
                {
                    foreach (var candidate in Dns.GetHostAddresses(remoteHost))
                        if (candidate.AddressFamily == AddressFamily.InterNetwork) { address = candidate; break; }
                }
                catch (SocketException) { }
            }

            if (address == null)
            {
                Debug.LogWarning($"OscLink: can't find host '{remoteHost}'.", this);
                return false;
            }
            remote = new IPEndPoint(address, remotePort);
            return true;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            while (arrived.TryDequeue(out byte[] datagram))
            {
                if (loss > 0f && UnityEngine.Random.value < loss) continue;
                if (delay <= 0f && jitter <= 0f) Handle(datagram);
                else held.Add((now + delay + UnityEngine.Random.value * jitter, datagram));
            }

            for (int i = 0; i < held.Count;)
            {
                if (held[i].due > now) { i++; continue; }
                Handle(held[i].datagram);
                held.RemoveAt(i);
            }
        }

        private void Handle(byte[] datagram)
        {
            DatagramReceived?.Invoke(datagram);
            messages.Clear();
            OscCodec.Parse(datagram, messages);
            foreach (var message in messages) Received?.Invoke(message);
        }
    }
}
