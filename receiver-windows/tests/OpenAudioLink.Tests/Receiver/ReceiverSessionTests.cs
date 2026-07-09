using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;
using OpenAudioLink.Receiver;

namespace OpenAudioLink.Tests.Receiver
{
    [TestClass]
    public sealed class ReceiverSessionTests
    {
        private const ulong SessionId = 0x0102030405060708UL;

        [TestMethod]
        public void ProcessHello_ReturnsWelcome()
        {
            ReceiverSession session = new ReceiverSession(SessionId);
            byte[] hello = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeHello,
                1u,
                123456000UL,
                HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));

            byte[] response = session.Process(hello);
            PacketHeader header = PacketParser.ParseHeader(response);

            Assert.AreEqual(ProtocolConstants.PacketTypeWelcome, header.PacketType);
            CollectionAssert.AreEqual(
                HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", SessionId),
                PacketParser.Payload(response));
            Assert.AreEqual(ReceiverSessionState.WaitingForStartStream, session.State);
        }

        [TestMethod]
        public void ProcessStartStream_ReturnsStreamReady()
        {
            ReceiverSession session = ReadySession();
            byte[] start = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStartStream,
                3u,
                123456002UL,
                HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20));

            byte[] response = session.Process(start);
            PacketHeader header = PacketParser.ParseHeader(response);

            Assert.AreEqual(ProtocolConstants.PacketTypeStreamReady, header.PacketType);
            CollectionAssert.AreEqual(
                HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2),
                PacketParser.Payload(response));
            Assert.AreEqual(ReceiverSessionState.Streaming, session.State);
        }

        [TestMethod]
        public void ProcessUnsupportedCodec_ReturnsStreamReadyFailure()
        {
            ReceiverSession session = ReadySession();
            byte[] start = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStartStream,
                3u,
                123456002UL,
                HandshakePayloads.StartStream(2, 48000u, 2, 192000u, 20));

            byte[] response = session.Process(start);
            PacketHeader header = PacketParser.ParseHeader(response);
            byte[] payload = PacketParser.Payload(response);

            Assert.AreEqual(ProtocolConstants.PacketTypeStreamReady, header.PacketType);
            Assert.AreEqual(ProtocolConstants.StreamResultUnsupportedCodec, payload[0]);
            Assert.AreEqual(ReceiverSessionState.Stopped, session.State);
        }

        [TestMethod]
        public void ProcessPing_ReturnsPongWithSamePayload()
        {
            ReceiverSession session = StreamingSession();
            byte[] pingPayload = HandshakePayloads.Ping(5u, 123456005UL);
            byte[] ping = PacketWriter.WritePacket(ProtocolConstants.PacketTypePing, 5u, 123456004UL, pingPayload);

            byte[] response = session.Process(ping);
            PacketHeader header = PacketParser.ParseHeader(response);

            Assert.AreEqual(ProtocolConstants.PacketTypePong, header.PacketType);
            Assert.AreEqual(123456004UL, header.Timestamp);
            CollectionAssert.AreEqual(pingPayload, PacketParser.Payload(response));
        }

        [TestMethod]
        public void ProcessStopStream_StopsSession()
        {
            ReceiverSession session = StreamingSession();
            byte[] stop = PacketWriter.WritePacket(ProtocolConstants.PacketTypeStopStream, 7u, 123456006UL, new byte[0]);

            byte[] response = session.Process(stop);

            Assert.IsNull(response);
            Assert.AreEqual(ReceiverSessionState.Stopped, session.State);
        }

        [TestMethod]
        public void ProcessAudioWhileStreaming_RecordsFrameAndReturnsNull()
        {
            ReceiverSession session = StreamingSession();
            byte[] payload = ValidAudioPayload();

            byte[] response = session.Process(PacketWriter.WritePacket(ProtocolConstants.PacketTypeAudio, 5u, 123456789UL, payload));

            Assert.IsNull(response);
            Assert.AreEqual(ReceiverSessionState.Streaming, session.State);
            Assert.AreEqual(1, session.AudioFramesReceived);
            CollectionAssert.AreEqual(payload, session.LastAudioPayload);
        }

        [TestMethod]
        public void ProcessAudioBeforeStartStream_Throws()
        {
            ReceiverSession session = ReadySession();

            Assert.ThrowsException<PacketParseException>(() => session.Process(PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeAudio,
                5u,
                123456789UL,
                ValidAudioPayload())));
        }

        [TestMethod]
        public void ProcessInvalidAudioWhileStreaming_Throws()
        {
            ReceiverSession session = StreamingSession();

            Assert.ThrowsException<PacketParseException>(() => session.Process(PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeAudio,
                5u,
                123456789UL,
                new byte[] { ProtocolConstants.CodecAacLc })));
            Assert.AreEqual(0, session.AudioFramesReceived);
        }

        [TestMethod]
        public void ProcessPingBeforeStartStream_Throws()
        {
            ReceiverSession session = ReadySession();
            byte[] ping = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypePing,
                5u,
                123456004UL,
                HandshakePayloads.Ping(5u, 123456005UL));

            Assert.ThrowsException<PacketParseException>(() => session.Process(ping));
        }

        [TestMethod]
        public void BusyWelcome_ReturnsReceiverBusy()
        {
            byte[] response = ReceiverSession.BusyWelcome();

            Assert.AreEqual(ProtocolConstants.PacketTypeWelcome, PacketParser.ParseHeader(response).PacketType);
            CollectionAssert.AreEqual(
                HandshakePayloads.Welcome(ProtocolConstants.ResultReceiverBusy, "Windows PC", "1.0.0", 0),
                PacketParser.Payload(response));
        }

        private static ReceiverSession ReadySession()
        {
            ReceiverSession session = new ReceiverSession(SessionId);
            session.Process(PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeHello,
                1u,
                123456000UL,
                HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported)));
            return session;
        }

        private static byte[] ValidAudioPayload()
        {
            return HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 1u, 123456789UL, 20, new byte[] { 0x11, 0x22, 0x33, 0x44 });
        }

        private static ReceiverSession StreamingSession()
        {
            ReceiverSession session = ReadySession();
            session.Process(PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStartStream,
                3u,
                123456002UL,
                HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20)));
            return session;
        }
    }
}
