using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;
using OpenAudioLink.Tests;

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

            CollectionAssert.AreEqual(TestFixtures.Read("testdata/protocol/valid-hello.bin"), packet);
        }

        [TestMethod]
        public void WriteWelcome_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeWelcome,
                2u,
                123456001UL,
                HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 0x0102030405060708UL));

            CollectionAssert.AreEqual(TestFixtures.Read("testdata/protocol/valid-welcome.bin"), packet);
        }

        [TestMethod]
        public void WriteStartStream_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStartStream,
                3u,
                123456002UL,
                HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 21));

            CollectionAssert.AreEqual(TestFixtures.Read("testdata/protocol/valid-start-stream.bin"), packet);
        }

        [TestMethod]
        public void WriteStreamReady_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStreamReady,
                4u,
                123456003UL,
                HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

            CollectionAssert.AreEqual(TestFixtures.Read("testdata/protocol/valid-stream-ready.bin"), packet);
        }

        [TestMethod]
        public void WritePingAndPong_MatchGoldenPackets()
        {
            byte[] payload = HandshakePayloads.Ping(5u, 123456005UL);

            CollectionAssert.AreEqual(
                TestFixtures.Read("testdata/protocol/valid-ping.bin"),
                PacketWriter.WritePacket(ProtocolConstants.PacketTypePing, 5u, 123456004UL, payload));
            CollectionAssert.AreEqual(
                TestFixtures.Read("testdata/protocol/valid-pong.bin"),
                PacketWriter.WritePacket(ProtocolConstants.PacketTypePong, 6u, 123456004UL, payload));
        }

        [TestMethod]
        public void WriteStopStream_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(ProtocolConstants.PacketTypeStopStream, 7u, 123456006UL, new byte[0]);

            CollectionAssert.AreEqual(TestFixtures.Read("testdata/protocol/valid-stop-stream.bin"), packet);
        }

        [TestMethod]
        public void WriteAudio_MatchesGoldenPacket()
        {
            byte[] encoded = TestFixtures.Read(
                "testdata/audio/aac-lc-48k-stereo-1024.raw");
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeAudio,
                2u,
                123456789UL,
                HandshakePayloads.Audio(
                    ProtocolConstants.CodecAacLc,
                    1u,
                    123456789UL,
                    21,
                    encoded));

            CollectionAssert.AreEqual(
                TestFixtures.Read("testdata/protocol/valid-audio-aac.bin"),
                packet);
        }

        [TestMethod]
        public void WriteError_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeError,
                8u,
                123456007UL,
                HandshakePayloads.Error(ProtocolConstants.ErrorUnsupportedCodec, ProtocolConstants.ErrorSeverityRecoverable, "Unsupported codec"));

            CollectionAssert.AreEqual(TestFixtures.Read("testdata/protocol/valid-error.bin"), packet);
        }
    }
}
