using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;
using OpenAudioLink.Receiver;

namespace OpenAudioLink.Tests.Receiver
{
    [TestClass]
    public sealed class TcpReceiverTests
    {
        private const int SocketTimeoutMilliseconds = 5000;

        [TestMethod]
        public void ClientCompletesPhase1aHandshake()
        {
            using (TcpReceiver receiver = TcpReceiver.StartLoopback())
            using (TcpClient client = Connect(receiver))
            {
                NetworkStream stream = client.GetStream();

                Write(stream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
                AssertPacket(stream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 1));

                Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20));
                AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

                Write(stream, ProtocolConstants.PacketTypeAudio, 3u, HandshakePayloads.Audio(
                    ProtocolConstants.CodecAacLc,
                    1u,
                    123456789UL,
                    20,
                    new byte[] { 0x11, 0x22, 0x33, 0x44 }));

                byte[] ping = HandshakePayloads.Ping(4u, 123UL);
                Write(stream, ProtocolConstants.PacketTypePing, 4u, ping);
                AssertPacket(stream, ProtocolConstants.PacketTypePong, ping);

                Write(stream, ProtocolConstants.PacketTypeStopStream, 5u, new byte[0]);
            }
        }

        [TestMethod]
        public void SecondClientReceivesBusyWelcomeWhileFirstActive()
        {
            using (TcpReceiver receiver = TcpReceiver.StartLoopback())
            using (TcpClient first = Connect(receiver))
            using (TcpClient second = Connect(receiver))
            {
                NetworkStream firstStream = first.GetStream();
                Write(firstStream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
                AssertPacket(firstStream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 1));

                NetworkStream secondStream = second.GetStream();
                Write(secondStream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone 2", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
                AssertPacket(secondStream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultReceiverBusy, "Windows PC", "1.0.0", 0));
            }
        }

        [TestMethod]
        public void MalformedPacketClosesConnection()
        {
            using (TcpReceiver receiver = TcpReceiver.Start(IPAddress.Loopback, 0))
            using (TcpClient client = Connect(receiver))
            {
                NetworkStream stream = client.GetStream();
                byte[] malformed = PacketWriter.WritePacket(ProtocolConstants.PacketTypeHello, 1u, 0, new byte[0]);
                malformed[0] = 0;

                stream.Write(malformed, 0, malformed.Length);

                Assert.AreEqual(0, stream.Read(new byte[1], 0, 1));
            }
        }

        private static TcpClient Connect(TcpReceiver receiver)
        {
            TcpClient client = new TcpClient();
            client.ReceiveTimeout = SocketTimeoutMilliseconds;
            client.SendTimeout = SocketTimeoutMilliseconds;
            client.Connect(IPAddress.Loopback, receiver.Port);
            return client;
        }

        private static void Write(NetworkStream stream, byte type, uint sequence, byte[] payload)
        {
            byte[] packet = PacketWriter.WritePacket(type, sequence, 0, payload);
            stream.Write(packet, 0, packet.Length);
        }

        private static void AssertPacket(NetworkStream stream, byte type, byte[] payload)
        {
            byte[] packet = PacketReader.ReadPacket(stream);
            Assert.AreEqual(type, PacketParser.ParseHeader(packet).PacketType);
            CollectionAssert.AreEqual(payload, PacketParser.Payload(packet));
        }
    }
}
