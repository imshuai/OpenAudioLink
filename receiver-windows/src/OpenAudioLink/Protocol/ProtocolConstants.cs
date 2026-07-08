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
        public const byte PacketTypeWelcome = 0x02;
        public const byte PacketTypeStartStream = 0x03;
        public const byte PacketTypeStreamReady = 0x04;
        public const byte PacketTypeAudio = 0x05;
        public const byte PacketTypeStopStream = 0x06;
        public const byte PacketTypePing = 0x07;
        public const byte PacketTypePong = 0x08;
        public const byte PacketTypeError = 0x09;

        public const byte ResultSuccess = 0;
        public const byte ResultUnsupportedProtocol = 1;
        public const byte ResultReceiverBusy = 2;
        public const byte ResultInternalError = 4;

        public const byte StreamResultSuccess = 0;
        public const byte StreamResultUnsupportedCodec = 1;
        public const byte StreamResultUnsupportedFormat = 2;
        public const byte StreamResultReceiverNotReady = 3;
        public const byte StreamResultInternalError = 4;

        public const ushort ErrorInvalidPacket = 1001;
        public const ushort ErrorUnsupportedProtocol = 1002;
        public const ushort ErrorUnsupportedCodec = 1003;
        public const ushort ErrorInvalidPayload = 1004;
        public const ushort ErrorReceiverBusy = 1005;
        public const ushort ErrorTimeout = 1008;
        public const byte ErrorSeverityRecoverable = 2;
        public const byte ErrorSeverityFatal = 3;

        public const byte CodecAacLc = 1;
        public const byte PlatformAndroid = 1;
        public const byte PlatformWindows = 2;
        public const uint CapabilityAacSupported = 1;
        public const int DefaultPort = 37373;

        public static readonly byte[] Magic = { (byte)'O', (byte)'A', (byte)'L', (byte)'P' };
    }
}
