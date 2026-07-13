using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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
        public void ClientCompletesPhase1aHandshakeAndDeliversAudioToQueue()
        {
            int audioCalls = 0;
            AudioFrameQueue queue = new AudioFrameQueue(3);
            using (CountdownEvent audioReceived = new CountdownEvent(3))
            using (TcpReceiver receiver = TcpReceiver.StartLoopback(payload =>
            {
                queue.Enqueue(payload);
                Interlocked.Increment(ref audioCalls);
                audioReceived.Signal();
            }))
            using (TcpClient client = Connect(receiver))
            {
                NetworkStream stream = client.GetStream();

                Write(stream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
                AssertPacket(stream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 1));

                Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20));
                AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

                byte[][] encodedFrames =
                {
                    new byte[] { 0x11, 0x22, 0x33, 0x44 },
                    new byte[] { 0x21, 0x22, 0x23, 0x24 },
                    new byte[] { 0x31, 0x32, 0x33, 0x34 },
                };
                byte[][] audioPayloads =
                {
                    HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 1u, 123456003UL, 20, encodedFrames[0]),
                    HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 2u, 123456023UL, 20, encodedFrames[1]),
                    HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 3u, 123456043UL, 20, encodedFrames[2]),
                };

                for (int i = 0; i < audioPayloads.Length; i++)
                {
                    Write(stream, ProtocolConstants.PacketTypeAudio, 3u + (uint)i, audioPayloads[i]);
                }

                Assert.IsTrue(audioReceived.Wait(SocketTimeoutMilliseconds), "Timed out waiting for audio sink callback.");
                Assert.AreEqual(3, audioCalls);
                Assert.AreEqual(3, queue.Count);

                FakeAudioRenderer renderer = new FakeAudioRenderer();
                Assert.AreEqual(3, renderer.Drain(queue, new FakeAacDecoder()));
                Assert.AreEqual(0, queue.Count);
                Assert.AreEqual(3, renderer.RenderedCount);

                IReadOnlyList<FakePcmFrame> renderedFrames = renderer.RenderedFrames;
                for (int i = 0; i < audioPayloads.Length; i++)
                {
                    Assert.AreEqual((uint)(i + 1), renderedFrames[i].FrameNumber);
                    Assert.AreEqual(123456003UL + (ulong)(20 * i), renderedFrames[i].CaptureTimestamp);
                    Assert.AreEqual((ushort)20, renderedFrames[i].FrameDuration);
                    CollectionAssert.AreEqual(encodedFrames[i], renderedFrames[i].PcmBytes);
                }

                byte[] ping = HandshakePayloads.Ping(5u, 123456005UL);
                Write(stream, ProtocolConstants.PacketTypePing, 6u, ping);
                AssertPacket(stream, ProtocolConstants.PacketTypePong, ping);

                Write(stream, ProtocolConstants.PacketTypeStopStream, 7u, new byte[0]);
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
