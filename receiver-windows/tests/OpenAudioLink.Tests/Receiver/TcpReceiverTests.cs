using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Tests;
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

                Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 21));
                AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

                byte[] encodedFrame = TestFixtures.Read("testdata/audio/aac-lc-48k-stereo-1024.raw");
                ulong[] captureTimestamps =
                {
                    123456003UL,
                    123477336UL,
                    123498670UL,
                };

                for (int i = 0; i < captureTimestamps.Length; i++)
                {
                    byte[] payload = HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, (uint)(i + 1), captureTimestamps[i], 21, encodedFrame);
                    Write(stream, ProtocolConstants.PacketTypeAudio, 3u + (uint)i, payload);
                }

                Assert.IsTrue(audioReceived.Wait(SocketTimeoutMilliseconds), "Timed out waiting for audio sink callback.");
                Assert.AreEqual(3, audioCalls);
                Assert.AreEqual(3, queue.Count);

                FakeAudioRenderer renderer = new FakeAudioRenderer();
                Assert.AreEqual(3, renderer.Drain(queue, new FakeAacDecoder()));
                Assert.AreEqual(0, queue.Count);
                Assert.AreEqual(3, renderer.RenderedCount);

                IReadOnlyList<FakePcmFrame> renderedFrames = renderer.RenderedFrames;
                for (int i = 0; i < captureTimestamps.Length; i++)
                {
                    Assert.AreEqual((uint)(i + 1), renderedFrames[i].FrameNumber);
                    Assert.AreEqual(captureTimestamps[i], renderedFrames[i].CaptureTimestamp);
                    Assert.AreEqual((ushort)21, renderedFrames[i].FrameDuration);
                    CollectionAssert.AreEqual(encodedFrame, renderedFrames[i].PcmBytes);
                }

                byte[] ping = HandshakePayloads.Ping(5u, 123498671UL);
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
        public void ClientCanReconnectAfterStopStream()
        {
            using (TcpReceiver receiver = TcpReceiver.StartLoopback())
            {
                CompleteAndStop(receiver, 1UL);
                CompleteAndStop(receiver, 2UL);
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

        private static void CompleteAndStop(TcpReceiver receiver, ulong sessionId)
        {
            using (TcpClient client = Connect(receiver))
            {
                NetworkStream stream = client.GetStream();

                Write(stream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
                AssertPacket(stream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", sessionId));

                Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 21));
                AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

                Write(stream, ProtocolConstants.PacketTypeStopStream, 3u, new byte[0]);
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
