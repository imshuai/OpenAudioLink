using System;
using System.Collections.Generic;
using System.IO;
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
            byte[] encodedFrame = TestFixtures.Read("testdata/audio/aac-lc-48k-stereo-1024.raw");
            ulong[] captureTimestamps =
            {
                123456003UL,
                123477336UL,
                123498670UL,
                223456003UL,
                223477336UL,
                223498670UL,
            };

            using (ReceiverRuntime runtime = ReceiverRuntime.StartLoopback())
            {
                for (int session = 0; session < 2; session++)
                {
                    using (TcpClient client = Connect(runtime.Port))
                    {
                        NetworkStream stream = client.GetStream();
                        WriteHelloAndStart(stream, (ulong)(session + 1));

                        for (int frame = 0; frame < 3; frame++)
                        {
                            int index = session * 3 + frame;
                            byte[] payload = HandshakePayloads.Audio(
                                ProtocolConstants.CodecAacLc,
                                (uint)(index + 1),
                                captureTimestamps[index],
                                21,
                                encodedFrame);
                            Write(stream, ProtocolConstants.PacketTypeAudio, 3u + (uint)frame, payload);
                        }

                        byte[] ping = HandshakePayloads.Ping(5u, captureTimestamps[session * 3 + 2] + 1);
                        Write(stream, ProtocolConstants.PacketTypePing, 6u, ping);
                        AssertPacket(stream, ProtocolConstants.PacketTypePong, ping);
                        Write(stream, ProtocolConstants.PacketTypeStopStream, 7u, new byte[0]);
                        AssertStreamClosed(client);
                    }
                }

                Assert.AreEqual(6, runtime.Renderer.RenderedCount);
                Assert.AreEqual(0, runtime.Queue.Count);
                IReadOnlyList<FakePcmFrame> renderedFrames = runtime.Renderer.RenderedFrames;
                Assert.AreEqual(6, renderedFrames.Count);
                for (int i = 0; i < renderedFrames.Count; i++)
                {
                    Assert.AreEqual((uint)(i + 1), renderedFrames[i].FrameNumber);
                    Assert.AreEqual(captureTimestamps[i], renderedFrames[i].CaptureTimestamp);
                    Assert.AreEqual((ushort)21, renderedFrames[i].FrameDuration);
                    Assert.AreEqual(4096, renderedFrames[i].PcmBytes.Length);
                    Assert.IsTrue(ChannelEnergy(renderedFrames[i].PcmBytes, 0) > 0);
                    Assert.IsTrue(ChannelEnergy(renderedFrames[i].PcmBytes, 1) > 0);
                }
            }
        }

        [TestMethod]
        public void CorruptAacClosesCurrentStreamAndAllowsHealthyReconnect()
        {
            byte[] encodedFrame = TestFixtures.Read("testdata/audio/aac-lc-48k-stereo-1024.raw");
            byte[] truncatedFrame = { encodedFrame[0] };

            using (ReceiverRuntime runtime = ReceiverRuntime.StartLoopback())
            {
                using (TcpClient corruptClient = Connect(runtime.Port))
                {
                    NetworkStream stream = corruptClient.GetStream();
                    WriteHelloAndStart(stream, 1UL);
                    Write(stream, ProtocolConstants.PacketTypeAudio, 3u, HandshakePayloads.Audio(
                        ProtocolConstants.CodecAacLc, 1u, 323456003UL, 21, truncatedFrame));

                    try
                    {
                        Write(stream, ProtocolConstants.PacketTypeStopStream, 4u, new byte[0]);
                    }
                    catch (SocketException exception)
                    {
                        AssertNotTimedOut(exception);
                    }
                    catch (IOException exception)
                    {
                        AssertNotTimedOut(exception);
                    }

                    AssertStreamClosed(corruptClient);
                }

                Assert.AreEqual(0, runtime.Renderer.RenderedCount);
                Assert.AreEqual(0, runtime.Queue.Count);

                using (TcpClient healthyClient = Connect(runtime.Port))
                {
                    NetworkStream stream = healthyClient.GetStream();
                    WriteHelloAndStart(stream, 2UL);
                    Write(stream, ProtocolConstants.PacketTypeAudio, 3u, HandshakePayloads.Audio(
                        ProtocolConstants.CodecAacLc, 1u, 423456003UL, 21, encodedFrame));
                    byte[] ping = HandshakePayloads.Ping(4u, 423456004UL);
                    Write(stream, ProtocolConstants.PacketTypePing, 5u, ping);
                    AssertPacket(stream, ProtocolConstants.PacketTypePong, ping);
                    Write(stream, ProtocolConstants.PacketTypeStopStream, 6u, new byte[0]);
                    AssertStreamClosed(healthyClient);
                }

                Assert.AreEqual(1, runtime.Renderer.RenderedCount);
                Assert.AreEqual(0, runtime.Queue.Count);
                FakePcmFrame renderedFrame = runtime.Renderer.RenderedFrames[0];
                Assert.AreEqual((uint)1, renderedFrame.FrameNumber);
                Assert.AreEqual(423456003UL, renderedFrame.CaptureTimestamp);
                Assert.AreEqual((ushort)21, renderedFrame.FrameDuration);
                Assert.AreEqual(4096, renderedFrame.PcmBytes.Length);
                Assert.IsTrue(ChannelEnergy(renderedFrame.PcmBytes, 0) > 0);
                Assert.IsTrue(ChannelEnergy(renderedFrame.PcmBytes, 1) > 0);
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

        private static void WriteHelloAndStart(NetworkStream stream, ulong sessionId)
        {
            Write(stream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello(
                "Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
            AssertPacket(stream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(
                ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", sessionId));
            Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, HandshakePayloads.StartStream(
                ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 21));
            AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, HandshakePayloads.StreamReady(
                ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));
        }

        private static void AssertStreamClosed(TcpClient client)
        {
            try
            {
                int value = client.GetStream().ReadByte();
                Assert.AreEqual(-1, value, "ReceiverRuntime did not reach EOF.");
            }
            catch (SocketException exception)
            {
                AssertNotTimedOut(exception);
            }
            catch (IOException exception)
            {
                AssertNotTimedOut(exception);
            }
        }

        private static void AssertNotTimedOut(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                SocketException socketException = current as SocketException;
                if (socketException != null && socketException.SocketErrorCode == SocketError.TimedOut)
                {
                    Assert.Fail("ReceiverRuntime socket operation timed out.");
                }
            }
        }

        private static long ChannelEnergy(byte[] pcmBytes, int channel)
        {
            long energy = 0;
            for (int i = channel * 2; i + 1 < pcmBytes.Length; i += 4)
            {
                short sample = (short)(pcmBytes[i] | (pcmBytes[i + 1] << 8));
                energy += Math.Abs((int)sample);
            }

            return energy;
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
