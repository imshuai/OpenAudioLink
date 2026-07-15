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
        private const string MediaCodecRuntimeInteropEnabled =
            "OAL_MEDIACODEC_RUNTIME_INTEROP";
        private const string MediaCodecRuntimeWirePath =
            "OAL_MEDIACODEC_RUNTIME_WIRE_PATH";

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
        public void AndroidEncodedTestStreamArtifactRunsThroughReceiverRuntime()
        {
            string enabled = Environment.GetEnvironmentVariable(
                MediaCodecRuntimeInteropEnabled);
            if (string.IsNullOrEmpty(enabled))
            {
                return;
            }
            Assert.AreEqual("1", enabled);
            string path = Environment.GetEnvironmentVariable(MediaCodecRuntimeWirePath);
            Assert.IsFalse(string.IsNullOrEmpty(path), "runtime wire path is missing");
            Assert.IsTrue(File.Exists(path), "runtime wire artifact does not exist: " + path);

            IReadOnlyList<byte[]> packets = ReadWirePackets(File.ReadAllBytes(path));
            AssertExactAndroidPackets(packets);

            using (ReceiverRuntime runtime = ReceiverRuntime.StartLoopback())
            using (TcpClient client = Connect(runtime.Port))
            {
                NetworkStream stream = client.GetStream();
                for (int i = 0; i < packets.Count; i++)
                {
                    byte type = PacketParser.ParseHeader(packets[i]).PacketType;
                    stream.Write(packets[i], 0, packets[i].Length);
                    if (type == ProtocolConstants.PacketTypeHello)
                    {
                        AssertPacket(stream, ProtocolConstants.PacketTypeWelcome,
                            HandshakePayloads.Welcome(
                                ProtocolConstants.ResultSuccess,
                                "Windows PC",
                                "1.0.0",
                                1UL));
                    }
                    else if (type == ProtocolConstants.PacketTypeStartStream)
                    {
                        AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady,
                            HandshakePayloads.StreamReady(
                                ProtocolConstants.StreamResultSuccess,
                                ProtocolConstants.CodecAacLc,
                                48000u,
                                2));
                    }
                    else if (type == ProtocolConstants.PacketTypePing)
                    {
                        AssertPacket(stream, ProtocolConstants.PacketTypePong,
                            PacketParser.Payload(packets[i]));
                    }
                }
                Assert.AreEqual(-1, stream.ReadByte(), "ReceiverRuntime did not close cleanly.");

                Assert.AreEqual(4, runtime.Renderer.RenderedCount);
                Assert.AreEqual(0, runtime.Queue.Count);
                ulong[] timestamps =
                {
                    123456003UL, 123477336UL, 123498670UL, 123520003UL,
                };
                long left = 0;
                long right = 0;
                for (int i = 0; i < 4; i++)
                {
                    FakePcmFrame frame = runtime.Renderer.RenderedFrames[i];
                    Assert.AreEqual((uint)(i + 1), frame.FrameNumber);
                    Assert.AreEqual(timestamps[i], frame.CaptureTimestamp);
                    Assert.AreEqual((ushort)21, frame.FrameDuration);
                    Assert.AreEqual(4096, frame.PcmBytes.Length);
                    left += ChannelEnergy(frame.PcmBytes, 0);
                    right += ChannelEnergy(frame.PcmBytes, 1);
                }
                Assert.IsTrue(left > 0, "left channel is silent");
                Assert.IsTrue(right > 0, "right channel is silent");
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

        private static IReadOnlyList<byte[]> ReadWirePackets(byte[] bytes)
        {
            List<byte[]> packets = new List<byte[]>();
            using (MemoryStream stream = new MemoryStream(bytes, false))
            {
                while (stream.Position < stream.Length)
                {
                    packets.Add(PacketReader.ReadPacket(stream));
                }
                Assert.AreEqual(stream.Length, stream.Position);
            }
            Assert.AreEqual(8, packets.Count);
            return packets;
        }

        private static void AssertExactAndroidPackets(IReadOnlyList<byte[]> packets)
        {
            byte[] types =
            {
                ProtocolConstants.PacketTypeHello,
                ProtocolConstants.PacketTypeStartStream,
                ProtocolConstants.PacketTypeAudio,
                ProtocolConstants.PacketTypeAudio,
                ProtocolConstants.PacketTypeAudio,
                ProtocolConstants.PacketTypeAudio,
                ProtocolConstants.PacketTypePing,
                ProtocolConstants.PacketTypeStopStream,
            };
            uint[] sequences = { 1u, 2u, 3u, 4u, 5u, 6u, 7u, 8u };
            ulong[] timestamps =
            {
                123456000UL,
                123456002UL,
                123456003UL,
                123477336UL,
                123498670UL,
                123520003UL,
                123520005UL,
                123520006UL,
            };

            Assert.AreEqual(8, packets.Count);
            for (int i = 0; i < packets.Count; i++)
            {
                PacketHeader header = PacketParser.ParseHeader(packets[i]);
                Assert.AreEqual(types[i], header.PacketType);
                Assert.AreEqual(sequences[i], header.SequenceNumber);
                Assert.AreEqual(timestamps[i], header.Timestamp);
            }

            CollectionAssert.AreEqual(
                HandshakePayloads.Hello(
                    "Android Phone",
                    "1.0.0",
                    ProtocolConstants.PlatformAndroid,
                    ProtocolConstants.CapabilityAacSupported),
                PacketParser.Payload(packets[0]));
            CollectionAssert.AreEqual(
                HandshakePayloads.StartStream(
                    ProtocolConstants.CodecAacLc,
                    48000u,
                    2,
                    192000u,
                    21),
                PacketParser.Payload(packets[1]));

            for (int i = 0; i < 4; i++)
            {
                byte[] payload = PacketParser.Payload(packets[i + 2]);
                AudioPayloadValidator.ValidateAacPayload(payload);
                Assert.AreEqual(ProtocolConstants.CodecAacLc, payload[0]);
                Assert.AreEqual((uint)(i + 1), ReadUInt32(payload, 1));
                Assert.AreEqual(timestamps[i + 2], ReadUInt64(payload, 5));
                Assert.AreEqual((ushort)21, ReadUInt16(payload, 13));
            }

            CollectionAssert.AreEqual(
                HandshakePayloads.Ping(6u, 123520004UL),
                PacketParser.Payload(packets[6]));
            Assert.AreEqual(0, PacketParser.Payload(packets[7]).Length);
        }

        private static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24)
                | ((uint)bytes[offset + 1] << 16)
                | ((uint)bytes[offset + 2] << 8)
                | bytes[offset + 3];
        }

        private static ulong ReadUInt64(byte[] bytes, int offset)
        {
            return ((ulong)ReadUInt32(bytes, offset) << 32)
                | ReadUInt32(bytes, offset + 4);
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
