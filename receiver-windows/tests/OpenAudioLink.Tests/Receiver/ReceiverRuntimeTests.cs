using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

                Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20));
                AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

                byte[][] encodedFrames =
                {
                    new byte[] { 0x11, 0x22, 0x33, 0x44 },
                    new byte[] { 0x21, 0x22, 0x23, 0x24 },
                    new byte[] { 0x31, 0x32, 0x33, 0x34 },
                };

                for (int i = 0; i < encodedFrames.Length; i++)
                {
                    byte[] payload = HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, (uint)(i + 1), 123456003UL + (ulong)(20 * i), 20, encodedFrames[i]);
                    Write(stream, ProtocolConstants.PacketTypeAudio, 3u + (uint)i, payload);
                }

                Assert.AreEqual(3, runtime.Renderer.RenderedCount);
                Assert.AreEqual(0, runtime.Queue.Count);
                IReadOnlyList<FakePcmFrame> renderedFrames = runtime.Renderer.RenderedFrames;
                for (int i = 0; i < encodedFrames.Length; i++)
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
