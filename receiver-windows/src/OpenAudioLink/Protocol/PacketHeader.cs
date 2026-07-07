namespace OpenAudioLink.Protocol
{
    public sealed class PacketHeader
    {
        internal PacketHeader(byte majorVersion, byte minorVersion, byte packetType, byte flags, uint sequenceNumber, ulong timestamp, uint payloadLength)
        {
            MajorVersion = majorVersion;
            MinorVersion = minorVersion;
            PacketType = packetType;
            Flags = flags;
            SequenceNumber = sequenceNumber;
            Timestamp = timestamp;
            PayloadLength = payloadLength;
        }

        public byte MajorVersion { get; }
        public byte MinorVersion { get; }
        public byte PacketType { get; }
        public byte Flags { get; }
        public uint SequenceNumber { get; }
        public ulong Timestamp { get; }
        public uint PayloadLength { get; }
    }
}
