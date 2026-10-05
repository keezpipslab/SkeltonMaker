using System;
using System.Collections.Generic;
using System.Text;

namespace SkeletonMaker
{
    /// <summary>
    /// Builds one OSC 1.0 message: an address, then int32 / float32 / string
    /// arguments. Reused from message to message; ToArray() hands back the
    /// finished datagram.
    /// </summary>
    public sealed class OscWriter
    {
        private readonly StringBuilder tags = new StringBuilder();
        private string address;
        private byte[] args = new byte[1024];
        private int length;

        public OscWriter Begin(string messageAddress)
        {
            address = messageAddress;
            tags.Clear();
            tags.Append(',');
            length = 0;
            return this;
        }

        public OscWriter Int(int value)
        {
            tags.Append('i');
            Reserve(4);
            args[length++] = (byte)(value >> 24);
            args[length++] = (byte)(value >> 16);
            args[length++] = (byte)(value >> 8);
            args[length++] = (byte)value;
            return this;
        }

        public OscWriter Float(float value)
        {
            Int(BitConverter.SingleToInt32Bits(value));
            tags[tags.Length - 1] = 'f';
            return this;
        }

        public OscWriter String(string value)
        {
            tags.Append('s');
            Reserve(PaddedLength(value));
            length = WriteString(args, length, value);
            return this;
        }

        public byte[] ToArray()
        {
            string typeTags = tags.ToString();
            var datagram = new byte[PaddedLength(address) + PaddedLength(typeTags) + length];
            int at = WriteString(datagram, 0, address);
            at = WriteString(datagram, at, typeTags);
            Buffer.BlockCopy(args, 0, datagram, at, length);
            return datagram;
        }

        private void Reserve(int more)
        {
            if (length + more > args.Length) Array.Resize(ref args, Math.Max(args.Length * 2, length + more));
        }

        // Null-terminated, padded with nulls to a multiple of four bytes.
        private static int PaddedLength(string value) => (value.Length / 4 + 1) * 4;

        private static int WriteString(byte[] into, int at, string value)
        {
            int end = at + PaddedLength(value);
            for (int i = 0; i < value.Length; i++) into[at++] = (byte)value[i];
            while (at < end) into[at++] = 0;
            return at;
        }
    }

    /// <summary>
    /// One received OSC message. Arguments are read in order with Int(),
    /// Float() and String(); anything that doesn't fit (wrong type, too few
    /// arguments) reads as a default value and sets Bad, so a reader can pull
    /// every argument it expects and check Bad once at the end.
    /// </summary>
    public sealed class OscMessage
    {
        public string Address;

        private string tags;
        private byte[] data;
        private int cursor;
        private int end;
        private int next;

        public bool Bad { get; private set; }
        public int ArgumentCount => tags.Length;

        public int Int()
        {
            if (!Take('i', 4)) return 0;
            return ReadInt();
        }

        /// <summary>An int argument is accepted too, as other OSC senders often
        /// write whole numbers that way.</summary>
        public float Float()
        {
            if (next < tags.Length && tags[next] == 'i') return Int();
            if (!Take('f', 4)) return 0f;
            return BitConverter.Int32BitsToSingle(ReadInt());
        }

        public string String()
        {
            if (!Take('s', 0)) return "";
            if (!OscCodec.TryReadString(data, ref cursor, end, out string value)) { Bad = true; return ""; }
            return value;
        }

        private bool Take(char tag, int size)
        {
            if (Bad || next >= tags.Length || tags[next] != tag || cursor + size > end)
            {
                Bad = true;
                return false;
            }
            next++;
            return true;
        }

        private int ReadInt()
        {
            int value = data[cursor] << 24 | data[cursor + 1] << 16 | data[cursor + 2] << 8 | data[cursor + 3];
            cursor += 4;
            return value;
        }

        internal static bool TryParse(byte[] data, int start, int end, out OscMessage message)
        {
            message = null;
            int at = start;
            if (!OscCodec.TryReadString(data, ref at, end, out string address) || address.Length == 0 || address[0] != '/') return false;
            if (!OscCodec.TryReadString(data, ref at, end, out string typeTags) || typeTags.Length == 0 || typeTags[0] != ',') return false;
            message = new OscMessage { Address = address, tags = typeTags.Substring(1), data = data, cursor = at, end = end };
            return true;
        }
    }

    public static class OscCodec
    {
        /// <summary>Adds every message in a datagram to messages: the one it is,
        /// or all of those in it if it is a bundle. Anything unreadable is left out.</summary>
        public static void Parse(byte[] datagram, List<OscMessage> messages) => Parse(datagram, 0, datagram.Length, messages, 0);

        private static void Parse(byte[] data, int start, int end, List<OscMessage> messages, int depth)
        {
            if (end - start < 4 || depth > 4) return;
            if (data[start] != '#')
            {
                if (OscMessage.TryParse(data, start, end, out var message)) messages.Add(message);
                return;
            }

            // "#bundle\0", an 8-byte time tag, then size-prefixed elements.
            int at = start + 16;
            while (at + 4 <= end)
            {
                int size = data[at] << 24 | data[at + 1] << 16 | data[at + 2] << 8 | data[at + 3];
                at += 4;
                if (size <= 0 || at + size > end) return;
                Parse(data, at, at + size, messages, depth + 1);
                at += size;
            }
        }

        internal static bool TryReadString(byte[] data, ref int at, int end, out string value)
        {
            int terminator = at;
            while (terminator < end && data[terminator] != 0) terminator++;
            if (terminator >= end) { value = null; return false; }
            value = Encoding.ASCII.GetString(data, at, terminator - at);
            at += ((terminator - at) / 4 + 1) * 4;
            return at <= end;
        }
    }
}
