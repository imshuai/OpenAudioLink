using System;
using System.Text;

namespace OpenAudioLink.Protocol
{
    public static class HandshakePayloads
    {
        public static byte[] Hello(string senderName, string senderVersion, byte platform, uint capabilities)
        {
            return Join(
                WriteString(senderName),
                WriteString(senderVersion),
                new[] { ProtocolConstants.MajorVersion, ProtocolConstants.MinorVersion, platform },
                WriteUInt32(capabilities));
        }

        public static byte[] Welcome(byte result, string receiverName, string receiverVersion, ulong sessionId)
        {
            return Join(new[] { result }, WriteString(receiverName), WriteString(receiverVersion), WriteUInt64(sessionId));
        }

        public static byte[] StartStream(byte codec, uint sampleRate, byte channels, uint bitrate, ushort frameDuration)
        {
            return Join(new[] { codec }, WriteUInt32(sampleRate), new[] { channels }, WriteUInt32(bitrate), WriteUInt16(frameDuration));
        }

        public static byte[] StreamReady(byte result, byte codec, uint sampleRate, byte channels)
        {
            return Join(new[] { result, codec }, WriteUInt32(sampleRate), new[] { channels });
        }

        public static byte[] Audio(byte codec, uint frameNumber, ulong captureTimestamp, ushort frameDuration, byte[] encodedData)
        {
            encodedData = encodedData ?? new byte[0];
            return Join(new[] { codec }, WriteUInt32(frameNumber), WriteUInt64(captureTimestamp), WriteUInt16(frameDuration), WriteUInt32((uint)encodedData.Length), encodedData);
        }

        public static byte[] Ping(uint sequence, ulong timestamp)
        {
            return Join(WriteUInt32(sequence), WriteUInt64(timestamp));
        }

        public static byte[] Error(ushort code, byte severity, string description)
        {
            return Join(WriteUInt16(code), new[] { severity }, WriteString(description));
        }

        public static byte ReadStartStreamCodec(byte[] payload)
        {
            if (payload == null || payload.Length < 12)
            {
                throw new PacketParseException("START_STREAM payload is too short.");
            }

            return payload[0];
        }

        private static byte[] WriteString(string value)
        {
            byte[] text = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (text.Length > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "String is too long.");
            }

            return Join(WriteUInt16((ushort)text.Length), text);
        }

        private static byte[] WriteUInt16(ushort value)
        {
            byte[] buffer = new byte[2];
            PacketWriter.WriteUInt16(buffer, 0, value);
            return buffer;
        }

        private static byte[] WriteUInt32(uint value)
        {
            byte[] buffer = new byte[4];
            PacketWriter.WriteUInt32(buffer, 0, value);
            return buffer;
        }

        private static byte[] WriteUInt64(ulong value)
        {
            byte[] buffer = new byte[8];
            PacketWriter.WriteUInt64(buffer, 0, value);
            return buffer;
        }

        private static byte[] Join(params byte[][] parts)
        {
            int length = 0;
            foreach (byte[] part in parts)
            {
                length += part.Length;
            }

            byte[] output = new byte[length];
            int offset = 0;
            foreach (byte[] part in parts)
            {
                Buffer.BlockCopy(part, 0, output, offset, part.Length);
                offset += part.Length;
            }

            return output;
        }
    }
}
