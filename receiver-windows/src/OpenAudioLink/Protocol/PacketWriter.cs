using System;

namespace OpenAudioLink.Protocol
{
    public static class PacketWriter
    {
        public static byte[] WritePacket(byte packetType, uint sequenceNumber, ulong timestamp, byte[] payload)
        {
            payload = payload ?? new byte[0];
            if (payload.Length > ProtocolConstants.MaxPacketSize)
            {
                throw new ArgumentOutOfRangeException(nameof(payload), "Payload is too large.");
            }

            byte[] packet = new byte[ProtocolConstants.HeaderSize + payload.Length];
            Buffer.BlockCopy(ProtocolConstants.Magic, 0, packet, 0, ProtocolConstants.Magic.Length);
            packet[4] = ProtocolConstants.MajorVersion;
            packet[5] = ProtocolConstants.MinorVersion;
            packet[6] = packetType;
            packet[7] = 0;
            WriteUInt32(packet, 8, sequenceNumber);
            WriteUInt64(packet, 12, timestamp);
            WriteUInt32(packet, 20, (uint)payload.Length);
            Buffer.BlockCopy(payload, 0, packet, ProtocolConstants.HeaderSize, payload.Length);
            return packet;
        }

        internal static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)value;
        }

        internal static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        internal static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            WriteUInt32(buffer, offset, (uint)(value >> 32));
            WriteUInt32(buffer, offset + 4, (uint)value);
        }
    }
}
