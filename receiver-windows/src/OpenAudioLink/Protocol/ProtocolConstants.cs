namespace OpenAudioLink.Protocol
{
    public static class ProtocolConstants
    {
        public const int HeaderSize = 24;
        public const int AudioPayloadHeaderSize = 19;
        public const int MaxPacketSize = 65536;
        public const byte MajorVersion = 1;
        public const byte MinorVersion = 0;
        public const byte PacketTypeHello = 0x01;
        public const byte PacketTypeAudio = 0x05;
        public const byte CodecAacLc = 1;
        public static readonly byte[] Magic = { (byte)'O', (byte)'A', (byte)'L', (byte)'P' };
    }
}
