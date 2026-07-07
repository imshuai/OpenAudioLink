namespace OpenAudioLink.Protocol
{
    public static class AudioPayloadValidator
    {
        public static void ValidateAacPayload(byte[] payload)
        {
            if (payload == null)
            {
                throw new PacketParseException("Payload is null.");
            }

            if (payload.Length < ProtocolConstants.AudioPayloadHeaderSize)
            {
                throw new PacketParseException("Payload is shorter than AAC header.");
            }

            if (payload[0] != ProtocolConstants.CodecAacLc)
            {
                throw new PacketParseException("Unsupported codec.");
            }

            uint encodedSize = PacketParser.ReadUInt32(payload, 15);
            if ((ulong)payload.Length != (ulong)ProtocolConstants.AudioPayloadHeaderSize + encodedSize)
            {
                throw new PacketParseException("AAC payload length mismatch.");
            }
        }
    }
}
