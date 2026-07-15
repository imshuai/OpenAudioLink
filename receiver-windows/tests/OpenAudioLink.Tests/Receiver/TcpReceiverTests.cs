using System;
using System.Collections.Generic;
using System.IO;
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
        public void StartLoopbackExposesStreamLifecycleCallbacks()
        {
            Assert.IsNotNull(typeof(TcpReceiver).GetMethod(
                "StartLoopback",
                new[] { typeof(Action<byte[]>), typeof(Action), typeof(Action) }));
        }

        [TestMethod]
        public void StreamLifecycleApiPreservesOriginalOverloads()
        {
            Assert.IsNotNull(typeof(TcpReceiver).GetMethod(
                "Start",
                new[] { typeof(IPAddress), typeof(int), typeof(Action<byte[]>) }));
            Assert.IsNotNull(typeof(TcpReceiver).GetMethod(
                "StartLoopback",
                new[] { typeof(Action<byte[]>) }));
        }

        [TestMethod]
        public void StreamCallbacksRunInOrderOnTheSessionThread()
        {
            List<string> events = new List<string>();
            int startThread = 0;
            int audioThread = 0;
            int endThread = 0;
            using (ManualResetEventSlim startEntered = new ManualResetEventSlim(false))
            using (ManualResetEventSlim releaseStart = new ManualResetEventSlim(false))
            using (ManualResetEventSlim endEntered = new ManualResetEventSlim(false))
            using (TcpReceiver receiver = TcpReceiver.StartLoopback(
                payload =>
                {
                    events.Add("audio");
                    audioThread = Thread.CurrentThread.ManagedThreadId;
                },
                () =>
                {
                    events.Add("start");
                    startThread = Thread.CurrentThread.ManagedThreadId;
                    startEntered.Set();
                    if (!releaseStart.Wait(SocketTimeoutMilliseconds * 2))
                    {
                        throw new PacketParseException("Timed out waiting to release stream start.");
                    }
                },
                () =>
                {
                    events.Add("end");
                    endThread = Thread.CurrentThread.ManagedThreadId;
                    endEntered.Set();
                }))
            using (TcpClient client = Connect(receiver))
            {
                NetworkStream stream = client.GetStream();
                CompleteHello(stream, 1UL);
                Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, StartStreamPayload());
                Assert.IsTrue(startEntered.Wait(SocketTimeoutMilliseconds));

                try
                {
                    stream.ReadTimeout = SocketTimeoutMilliseconds;
                    AssertReadTimesOut(stream);
                }
                finally
                {
                    stream.ReadTimeout = SocketTimeoutMilliseconds;
                    releaseStart.Set();
                }

                AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, StreamReadyPayload());
                Write(stream, ProtocolConstants.PacketTypeAudio, 3u, CanonicalAudioPayload());
                Write(stream, ProtocolConstants.PacketTypeStopStream, 4u, new byte[0]);
                Assert.IsTrue(endEntered.Wait(SocketTimeoutMilliseconds));
                CollectionAssert.AreEqual(new[] { "start", "audio", "end" }, events);
                Assert.AreEqual(startThread, audioThread);
                Assert.AreEqual(startThread, endThread);
            }
        }

        [TestMethod]
        public void StreamStartFailureClosesOnlyThatClientAndAllowsReconnect()
        {
            int starts = 0;
            int ends = 0;
            using (TcpReceiver receiver = TcpReceiver.StartLoopback(
                null,
                () =>
                {
                    if (Interlocked.Increment(ref starts) == 1)
                    {
                        throw new PacketParseException("test start failure");
                    }
                },
                () => Interlocked.Increment(ref ends)))
            {
                using (TcpClient first = Connect(receiver))
                {
                    NetworkStream stream = first.GetStream();
                    CompleteHello(stream, 1UL);
                    Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, StartStreamPayload());
                    AssertClientClosed(stream);
                }

                using (TcpClient second = Connect(receiver))
                {
                    NetworkStream stream = second.GetStream();
                    CompleteHandshake(stream, 2UL);
                    Write(stream, ProtocolConstants.PacketTypeStopStream, 3u, new byte[0]);
                    AssertClientClosed(stream);
                }

                Assert.AreEqual(2, starts);
                Assert.AreEqual(1, ends);
            }
        }

        [TestMethod]
        public void AbruptDisconnectInvokesStreamEndedOnOwnerThread()
        {
            int startThread = 0;
            int endThread = 0;
            using (ManualResetEventSlim endEntered = new ManualResetEventSlim(false))
            using (TcpReceiver receiver = TcpReceiver.StartLoopback(
                null,
                () => startThread = Thread.CurrentThread.ManagedThreadId,
                () =>
                {
                    endThread = Thread.CurrentThread.ManagedThreadId;
                    endEntered.Set();
                }))
            using (TcpClient client = Connect(receiver))
            {
                CompleteHandshake(client.GetStream(), 1UL);
                client.Close();
                Assert.IsTrue(endEntered.Wait(SocketTimeoutMilliseconds));
                Assert.AreEqual(startThread, endThread);
            }
        }

        [TestMethod]
        public void BusyClientDoesNotInvokeStreamLifecycle()
        {
            int starts = 0;
            int ends = 0;
            using (TcpReceiver receiver = TcpReceiver.StartLoopback(
                null,
                () => Interlocked.Increment(ref starts),
                () => Interlocked.Increment(ref ends)))
            using (TcpClient first = Connect(receiver))
            {
                CompleteHandshake(first.GetStream(), 1UL);
                using (TcpClient second = Connect(receiver))
                {
                    NetworkStream secondStream = second.GetStream();
                    Write(secondStream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone 2", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
                    AssertPacket(secondStream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultReceiverBusy, "Windows PC", "1.0.0", 0));
                }
                Assert.AreEqual(1, starts);

                Write(first.GetStream(), ProtocolConstants.PacketTypeStopStream, 3u, new byte[0]);
                AssertClientClosed(first.GetStream());
                Assert.AreEqual(1, ends);
            }
        }

        [TestMethod]
        public void DisposeBeforeStartClosesClientWithoutStartingStream()
        {
            int starts = 0;
            int ends = 0;
            using (TcpReceiver receiver = TcpReceiver.StartLoopback(
                null,
                () => Interlocked.Increment(ref starts),
                () => Interlocked.Increment(ref ends)))
            using (TcpClient client = Connect(receiver))
            {
                CompleteHello(client.GetStream(), 1UL);
                receiver.Dispose();
                AssertClientClosed(client.GetStream());
                Assert.AreEqual(0, starts);
                Assert.AreEqual(0, ends);
            }
        }

        [TestMethod]
        public void DisposeDuringStreamInvokesStreamEndedOnOwnerThread()
        {
            int startThread = 0;
            int endThread = 0;
            using (ManualResetEventSlim endEntered = new ManualResetEventSlim(false))
            using (TcpReceiver receiver = TcpReceiver.StartLoopback(
                null,
                () => startThread = Thread.CurrentThread.ManagedThreadId,
                () =>
                {
                    endThread = Thread.CurrentThread.ManagedThreadId;
                    endEntered.Set();
                }))
            using (TcpClient client = Connect(receiver))
            {
                CompleteHandshake(client.GetStream(), 1UL);
                receiver.Dispose();
                Assert.IsTrue(endEntered.Wait(SocketTimeoutMilliseconds));
                Assert.AreEqual(startThread, endThread);
            }
        }

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
                CompleteHandshake(stream, sessionId);

                Write(stream, ProtocolConstants.PacketTypeStopStream, 3u, new byte[0]);
                AssertClientClosed(stream);
            }
        }

        private static void CompleteHello(NetworkStream stream, ulong sessionId, string clientName = "Android Phone")
        {
            Write(stream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello(clientName, "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
            AssertPacket(stream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", sessionId));
        }

        private static void CompleteHandshake(NetworkStream stream, ulong sessionId)
        {
            CompleteHello(stream, sessionId);
            Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, StartStreamPayload());
            AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, StreamReadyPayload());
        }

        private static byte[] StartStreamPayload()
        {
            return HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 21);
        }

        private static byte[] StreamReadyPayload()
        {
            return HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2);
        }

        private static byte[] CanonicalAudioPayload()
        {
            return HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 1u, 123456789UL, 21, TestFixtures.Read("testdata/audio/aac-lc-48k-stereo-1024.raw"));
        }

        private static void AssertReadTimesOut(NetworkStream stream)
        {
            try
            {
                stream.ReadByte();
                Assert.Fail("Expected a timed-out read while stream start callback is blocked.");
            }
            catch (IOException exception)
            {
                SocketException socketException = exception.InnerException as SocketException;
                Assert.IsNotNull(socketException);
                Assert.AreEqual(SocketError.TimedOut, socketException.SocketErrorCode);
            }
        }

        private static void AssertClientClosed(NetworkStream stream)
        {
            try
            {
                Assert.AreEqual(-1, stream.ReadByte());
            }
            catch (IOException exception)
            {
                SocketException socketException = exception.InnerException as SocketException;
                if (socketException != null && socketException.SocketErrorCode == SocketError.TimedOut)
                {
                    Assert.Fail("Timed out waiting for the receiver to close the client.");
                }
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
