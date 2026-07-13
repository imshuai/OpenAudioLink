using System;
using OpenAudioLink.Protocol;

namespace OpenAudioLink.Receiver
{
    public sealed class FakeAacDecoder
    {
        public FakePcmFrame Decode(byte[] audioPayload)
        {
            AudioPayloadValidator.ValidateAacPayload(audioPayload);

            uint frameNumber = PacketParser.ReadUInt32(audioPayload, 1);
            ulong captureTimestamp = ((ulong)PacketParser.ReadUInt32(audioPayload, 5) << 32) | PacketParser.ReadUInt32(audioPayload, 9);
            ushort frameDuration = ReadUInt16(audioPayload, 13);
            uint encodedSize = PacketParser.ReadUInt32(audioPayload, 15);
            byte[] fakePcmBytes = new byte[encodedSize];
            Buffer.BlockCopy(audioPayload, ProtocolConstants.AudioPayloadHeaderSize, fakePcmBytes, 0, fakePcmBytes.Length);

            return new FakePcmFrame(frameNumber, captureTimestamp, frameDuration, fakePcmBytes);
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        }
    }
}
