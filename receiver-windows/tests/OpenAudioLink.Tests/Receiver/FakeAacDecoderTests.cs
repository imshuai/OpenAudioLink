using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;
using OpenAudioLink.Receiver;

namespace OpenAudioLink.Tests.Receiver
{
    [TestClass]
    public sealed class FakeAacDecoderTests
    {
        [TestMethod]
        public void DecodeMapsAudioPayloadToFakePcmFrame()
        {
            byte[] encoded = new byte[] { 0x11, 0x22, 0x33 };
            byte[] payload = HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 7u, 123456789UL, 20, encoded);

            FakePcmFrame frame = new FakeAacDecoder().Decode(payload);

            Assert.AreEqual(7u, frame.FrameNumber);
            Assert.AreEqual(123456789UL, frame.CaptureTimestamp);
            Assert.AreEqual((ushort)20, frame.FrameDuration);
            CollectionAssert.AreEqual(encoded, frame.PcmBytes);
        }

        [TestMethod]
        public void DecodeRejectsUnsupportedCodec()
        {
            byte[] payload = HandshakePayloads.Audio(ProtocolConstants.CodecOpus, 1u, 2UL, 20, new byte[] { 0x01 });

            Assert.ThrowsException<PacketParseException>(() => new FakeAacDecoder().Decode(payload));
        }

        [TestMethod]
        public void DecodeRejectsLengthMismatch()
        {
            byte[] payload = HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 1u, 2UL, 20, new byte[] { 0x01, 0x02 });
            byte[] truncated = new byte[payload.Length - 1];
            System.Buffer.BlockCopy(payload, 0, truncated, 0, truncated.Length);

            Assert.ThrowsException<PacketParseException>(() => new FakeAacDecoder().Decode(truncated));
        }

        [TestMethod]
        public void FakePcmFrameClonesPcmBytes()
        {
            byte[] pcmBytes = new byte[] { 0x31, 0x32 };
            FakePcmFrame frame = new FakePcmFrame(1u, 2UL, 20, pcmBytes);

            pcmBytes[0] = 0x7f;
            CollectionAssert.AreEqual(new byte[] { 0x31, 0x32 }, frame.PcmBytes);

            byte[] returned = frame.PcmBytes;
            returned[0] = 0x7e;

            CollectionAssert.AreEqual(new byte[] { 0x31, 0x32 }, frame.PcmBytes);
        }
    }
}
