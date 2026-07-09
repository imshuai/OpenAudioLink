using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;

namespace OpenAudioLink.Tests.Protocol
{
    [TestClass]
    public sealed class PacketWriterTests
    {
        [TestMethod]
        public void WriteHello_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeHello,
                1u,
                123456000UL,
                HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));

            CollectionAssert.AreEqual(ReadFixture("valid-hello.bin"), packet);
        }

        [TestMethod]
        public void WriteWelcome_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeWelcome,
                2u,
                123456001UL,
                HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 0x0102030405060708UL));

            CollectionAssert.AreEqual(ReadFixture("valid-welcome.bin"), packet);
        }

        [TestMethod]
        public void WriteStartStream_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStartStream,
                3u,
                123456002UL,
                HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20));

            CollectionAssert.AreEqual(ReadFixture("valid-start-stream.bin"), packet);
        }

        [TestMethod]
        public void WriteStreamReady_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStreamReady,
                4u,
                123456003UL,
                HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

            CollectionAssert.AreEqual(ReadFixture("valid-stream-ready.bin"), packet);
        }

        [TestMethod]
        public void WritePingAndPong_MatchGoldenPackets()
        {
            byte[] payload = HandshakePayloads.Ping(5u, 123456005UL);

            CollectionAssert.AreEqual(
                ReadFixture("valid-ping.bin"),
                PacketWriter.WritePacket(ProtocolConstants.PacketTypePing, 5u, 123456004UL, payload));
            CollectionAssert.AreEqual(
                ReadFixture("valid-pong.bin"),
                PacketWriter.WritePacket(ProtocolConstants.PacketTypePong, 6u, 123456004UL, payload));
        }

        [TestMethod]
        public void WriteStopStream_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(ProtocolConstants.PacketTypeStopStream, 7u, 123456006UL, new byte[0]);

            CollectionAssert.AreEqual(ReadFixture("valid-stop-stream.bin"), packet);
        }

        [TestMethod]
        public void WriteAudio_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeAudio,
                2u,
                123456789UL,
                HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 1u, 123456789UL, 20, new byte[] { 0x11, 0x22, 0x33, 0x44 }));

            CollectionAssert.AreEqual(ReadFixture("valid-audio-aac.bin"), packet);
        }

        [TestMethod]
        public void WriteError_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeError,
                8u,
                123456007UL,
                HandshakePayloads.Error(ProtocolConstants.ErrorUnsupportedCodec, ProtocolConstants.ErrorSeverityRecoverable, "Unsupported codec"));

            CollectionAssert.AreEqual(ReadFixture("valid-error.bin"), packet);
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
