namespace OpenAudioLink.Protocol
{
    public static class PacketParser
    {
        public static PacketHeader ParseHeader(byte[] packet)
        {
            if (packet == null)
            {
                throw new PacketParseException("Packet is null.");
            }

            if (packet.Length < ProtocolConstants.HeaderSize)
            {
                throw new PacketParseException("Packet is shorter than header.");
            }

            if (packet[0] != ProtocolConstants.Magic[0] || packet[1] != ProtocolConstants.Magic[1] || packet[2] != ProtocolConstants.Magic[2] || packet[3] != ProtocolConstants.Magic[3])
            {
                throw new PacketParseException("Invalid magic.");
            }

            byte majorVersion = packet[4];
            if (majorVersion != ProtocolConstants.MajorVersion)
            {
                throw new PacketParseException("Unsupported major version.");
            }

            uint payloadLength = ReadUInt32(packet, 20);
            if (payloadLength > (uint)ProtocolConstants.MaxPacketSize)
            {
                throw new PacketParseException("Payload is too large.");
            }

            if ((ulong)packet.Length < (ulong)ProtocolConstants.HeaderSize + payloadLength)
            {
                throw new PacketParseException("Packet is shorter than payload length.");
            }

            return new PacketHeader(
                majorVersion,
                packet[5],
                packet[6],
                packet[7],
                ReadUInt32(packet, 8),
                ReadUInt64(packet, 12),
                payloadLength);
        }

        public static byte[] Payload(byte[] packet)
        {
            PacketHeader header = ParseHeader(packet);
            byte[] payload = new byte[(int)header.PayloadLength];
            System.Buffer.BlockCopy(packet, ProtocolConstants.HeaderSize, payload, 0, payload.Length);
            return payload;
        }

        internal static uint ReadUInt32(byte[] buffer, int offset)
        {
            return ((uint)buffer[offset] << 24) |
                   ((uint)buffer[offset + 1] << 16) |
                   ((uint)buffer[offset + 2] << 8) |
                   buffer[offset + 3];
        }

        private static ulong ReadUInt64(byte[] buffer, int offset)
        {
            return ((ulong)ReadUInt32(buffer, offset) << 32) | ReadUInt32(buffer, offset + 4);
        }
    }
}
