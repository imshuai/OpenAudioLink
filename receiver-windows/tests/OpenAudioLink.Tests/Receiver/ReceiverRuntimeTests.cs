using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Tests;
using OpenAudioLink.Protocol;
using OpenAudioLink.Receiver;

namespace OpenAudioLink.Tests.Receiver
{
    [TestClass]
    public sealed class ReceiverRuntimeTests
    {
        private const int SocketTimeoutMilliseconds = 5000;

        [TestMethod]
        public void StartLoopbackExposesReceiverState()
        {
            using (ReceiverRuntime runtime = ReceiverRuntime.StartLoopback())
            {
                Assert.AreNotEqual(0, runtime.Port);
                Assert.AreEqual(0, runtime.Queue.Count);
                Assert.AreEqual(0, runtime.Renderer.RenderedCount);
            }
        }

        [TestMethod]
        public void StartRejectsNullAddress()
        {
            Assert.ThrowsException<ArgumentNullException>(() => ReceiverRuntime.Start(null, 0));
        }

        [TestMethod]
        public void StartRejectsNonPositiveQueueCapacity()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => ReceiverRuntime.Start(IPAddress.Loopback, 0, 0));
        }

        [TestMethod]
        public void ClientAudioFramesAreDecodedAndRendered()
        {
            using (ReceiverRuntime runtime = ReceiverRuntime.StartLoopback())
            using (TcpClient client = Connect(runtime.Port))
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

                byte[] ping = HandshakePayloads.Ping(5u, 123498671UL);
                Write(stream, ProtocolConstants.PacketTypePing, 6u, ping);
                AssertPacket(stream, ProtocolConstants.PacketTypePong, ping);

                Assert.AreEqual(3, runtime.Renderer.RenderedCount);
                Assert.AreEqual(0, runtime.Queue.Count);
                IReadOnlyList<FakePcmFrame> renderedFrames = runtime.Renderer.RenderedFrames;
                for (int i = 0; i < captureTimestamps.Length; i++)
                {
                    Assert.AreEqual((uint)(i + 1), renderedFrames[i].FrameNumber);
                    Assert.AreEqual(captureTimestamps[i], renderedFrames[i].CaptureTimestamp);
                    Assert.AreEqual((ushort)21, renderedFrames[i].FrameDuration);
                    CollectionAssert.AreEqual(encodedFrame, renderedFrames[i].PcmBytes);
                }

                Write(stream, ProtocolConstants.PacketTypeStopStream, 7u, new byte[0]);
            }
        }

        private static TcpClient Connect(int port)
        {
            TcpClient client = new TcpClient();
            client.ReceiveTimeout = SocketTimeoutMilliseconds;
            client.SendTimeout = SocketTimeoutMilliseconds;
            client.Connect(IPAddress.Loopback, port);
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
