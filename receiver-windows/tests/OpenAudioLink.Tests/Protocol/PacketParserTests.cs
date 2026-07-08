using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;

namespace OpenAudioLink.Tests.Protocol
{
    [TestClass]
    public sealed class PacketParserTests
    {
        [TestMethod]
        public void ParseHeader_ValidHello_ReturnsHeader()
        {
            byte[] packet = ReadFixture("valid-hello.bin");

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
            Assert.ThrowsException<PacketParseException>(() => PacketParser.ParseHeader(ReadFixture("invalid-magic.bin")));
        }

        [TestMethod]
        public void ParseHeader_InvalidLength_Throws()
        {
            Assert.ThrowsException<PacketParseException>(() => PacketParser.ParseHeader(ReadFixture("invalid-length.bin")));
        }

        [TestMethod]
        public void ValidateAacPayload_ValidAudioPayload_DoesNotThrow()
        {
            byte[] packet = ReadFixture("valid-audio-aac.bin");

            PacketHeader header = PacketParser.ParseHeader(packet);
            byte[] payload = PacketParser.Payload(packet);

            Assert.AreEqual(ProtocolConstants.PacketTypeAudio, header.PacketType);
            Assert.AreEqual(ProtocolConstants.CodecAacLc, payload[0]);
            Assert.AreEqual(23, payload.Length);
            AudioPayloadValidator.ValidateAacPayload(payload);
        }

        [TestMethod]
        public void ParseHeader_Phase1aFixtures_ReturnExpectedTypes()
        {
            Assert.AreEqual(ProtocolConstants.PacketTypeWelcome, PacketParser.ParseHeader(ReadFixture("valid-welcome.bin")).PacketType);
            Assert.AreEqual(ProtocolConstants.PacketTypeStartStream, PacketParser.ParseHeader(ReadFixture("valid-start-stream.bin")).PacketType);
            Assert.AreEqual(ProtocolConstants.PacketTypeStreamReady, PacketParser.ParseHeader(ReadFixture("valid-stream-ready.bin")).PacketType);
            Assert.AreEqual(ProtocolConstants.PacketTypePing, PacketParser.ParseHeader(ReadFixture("valid-ping.bin")).PacketType);
            Assert.AreEqual(ProtocolConstants.PacketTypePong, PacketParser.ParseHeader(ReadFixture("valid-pong.bin")).PacketType);
            Assert.AreEqual(ProtocolConstants.PacketTypeStopStream, PacketParser.ParseHeader(ReadFixture("valid-stop-stream.bin")).PacketType);
            Assert.AreEqual(ProtocolConstants.PacketTypeError, PacketParser.ParseHeader(ReadFixture("valid-error.bin")).PacketType);
        }

        private static byte[] ReadFixture(string name)
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                string path = Path.Combine(directory.FullName, "testdata", "protocol", name);
                if (File.Exists(path))
                {
                    return File.ReadAllBytes(path);
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Fixture not found.", name);
        }
    }
}
