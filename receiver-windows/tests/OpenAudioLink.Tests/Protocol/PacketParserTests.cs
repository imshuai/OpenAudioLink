using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;
using OpenAudioLink.Tests;

namespace OpenAudioLink.Tests.Protocol
{
    [TestClass]
    public sealed class PacketParserTests
    {
        [TestMethod]
        public void ParseHeader_ValidHello_ReturnsHeader()
        {
            byte[] packet = TestFixtures.Read("testdata/protocol/valid-hello.bin");

            PacketHeader header = PacketParser.ParseHeader(packet);
            byte[] payload = PacketParser.Payload(packet);

            Assert.AreEqual(ProtocolConstants.MajorVersion, header.MajorVersion);
            Assert.AreEqual(ProtocolConstants.MinorVersion, header.MinorVersion);
            Assert.AreEqual(ProtocolConstants.PacketTypeHello, header.PacketType);
            Assert.AreEqual(1u, header.SequenceNumber);
            Assert.AreEqual(123456000UL, header.Timestamp);
            Assert.AreEqual((uint)payload.Length, header.PayloadLength);
        }

        [TestMethod]
        public void ParseHeader_InvalidMagic_Throws()
        {
            Assert.ThrowsException<PacketParseException>(() => PacketParser.ParseHeader(TestFixtures.Read("testdata/protocol/invalid-magic.bin")));
        }

        [TestMethod]
        public void ParseHeader_InvalidLength_Throws()
        {
            Assert.ThrowsException<PacketParseException>(() => PacketParser.ParseHeader(TestFixtures.Read("testdata/protocol/invalid-length.bin")));
        }

        [TestMethod]
        public void ValidateAacPayload_ValidAudioPayload_ExposesCanonicalRawFrame()
        {
            byte[] packet = TestFixtures.Read("testdata/protocol/valid-audio-aac.bin");
            byte[] payload = PacketParser.Payload(packet);
            byte[] encoded = TestFixtures.Read(
                "testdata/audio/aac-lc-48k-stereo-1024.raw");
            byte[] extracted = new byte[encoded.Length];
            Buffer.BlockCopy(
                payload,
                ProtocolConstants.AudioPayloadHeaderSize,
                extracted,
                0,
                extracted.Length);

            Assert.AreEqual(ProtocolConstants.PacketTypeAudio, PacketParser.ParseHeader(packet).PacketType);
            Assert.AreEqual(ProtocolConstants.AudioPayloadHeaderSize + encoded.Length, payload.Length);
            CollectionAssert.AreEqual(
                HandshakePayloads.Audio(
                    ProtocolConstants.CodecAacLc,
                    1u,
                    123456789UL,
                    21,
                    encoded),
                payload);
            CollectionAssert.AreEqual(encoded, extracted);
            AudioPayloadValidator.ValidateAacPayload(payload);
        }

        [TestMethod]
        public void ValidateAacPayload_TooShort_Throws()
        {
            Assert.ThrowsException<PacketParseException>(() => AudioPayloadValidator.ValidateAacPayload(new byte[ProtocolConstants.AudioPayloadHeaderSize - 1]));
        }

        [TestMethod]
        public void ValidateAacPayload_EmptyEncodedData_Throws()
        {
            byte[] payload = HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 1u, 2UL, 21, new byte[0]);

            Assert.ThrowsException<PacketParseException>(() => AudioPayloadValidator.ValidateAacPayload(payload));
        }

        [TestMethod]
        public void ValidateAacPayload_UnsupportedCodec_Throws()
        {
            byte[] payload = ValidAudioPayload();
            payload[0] = ProtocolConstants.CodecOpus;

            Assert.ThrowsException<PacketParseException>(() => AudioPayloadValidator.ValidateAacPayload(payload));
        }

        [TestMethod]
        public void ValidateAacPayload_EncodedSizeMismatch_Throws()
        {
            byte[] payload = ValidAudioPayload();
            payload[18] = 0x05;

            Assert.ThrowsException<PacketParseException>(() => AudioPayloadValidator.ValidateAacPayload(payload));
        }

        [TestMethod]
        public void ParseHeader_Phase1aFixtures_ReturnExpectedTypesAndPayloads()
        {
            AssertFixture("valid-hello.bin", ProtocolConstants.PacketTypeHello, "000d416e64726f69642050686f6e650005312e302e3001000100000001");
            AssertFixture("valid-welcome.bin", ProtocolConstants.PacketTypeWelcome, "00000a57696e646f77732050430005312e302e300102030405060708");
            AssertFixture("valid-start-stream.bin", ProtocolConstants.PacketTypeStartStream, "010000bb80020002ee000015");
            AssertFixture("valid-stream-ready.bin", ProtocolConstants.PacketTypeStreamReady, "00010000bb8002");
            AssertFixture("valid-ping.bin", ProtocolConstants.PacketTypePing, "0000000500000000075bca05");
            AssertFixture("valid-pong.bin", ProtocolConstants.PacketTypePong, "0000000500000000075bca05");
            AssertFixture("valid-stop-stream.bin", ProtocolConstants.PacketTypeStopStream, string.Empty);
            AssertFixture("valid-error.bin", ProtocolConstants.PacketTypeError, "03eb020011556e737570706f7274656420636f646563");
        }

        [TestMethod]
        public void Payload_InvalidDeclaredLength_Throws()
        {
            Assert.ThrowsException<PacketParseException>(() => PacketParser.Payload(TestFixtures.Read("testdata/protocol/invalid-length.bin")));
        }

        private static void AssertFixture(string name, byte packetType, string payloadHex)
        {
            byte[] packet = TestFixtures.Read("testdata/protocol/" + name);

            Assert.AreEqual(packetType, PacketParser.ParseHeader(packet).PacketType);
            CollectionAssert.AreEqual(FromHex(payloadHex), PacketParser.Payload(packet));
        }

        private static byte[] ValidAudioPayload()
        {
            return PacketParser.Payload(TestFixtures.Read("testdata/protocol/valid-audio-aac.bin"));
        }

        private static byte[] FromHex(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }
    }
}
