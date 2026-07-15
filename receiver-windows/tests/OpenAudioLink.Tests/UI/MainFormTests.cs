using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;
using OpenAudioLink.Receiver;
using OpenAudioLink.Tests;
using OpenAudioLink;

namespace OpenAudioLink.Tests.UI
{
    [TestClass]
    public sealed class MainFormTests
    {
        private const int SocketTimeoutMilliseconds = 5000;

        [TestMethod]
        public void ConstructorStartsReceiverRuntimeAndShowsPort()
        {
            RunSta(() =>
            {
                using (MainForm form = new MainForm())
                {
                    Assert.AreEqual(ProtocolConstants.DefaultPort, form.ListeningPort);
                    StringAssert.Contains(VisibleText(form), "Listening on TCP port " + ProtocolConstants.DefaultPort);
                    StringAssert.Contains(VisibleText(form), "Rendered frames: 0");

                    using (TcpClient client = Connect(form.ListeningPort))
                    {
                        NetworkStream stream = client.GetStream();
                        Write(stream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
                        AssertPacket(stream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 1));
                        Write(stream, ProtocolConstants.PacketTypeStopStream, 2u, new byte[0]);
                    }
                }
            });
        }

        [TestMethod]
        public void DecodedStreamUpdatesRenderedFrameStatus()
        {
            RunSta(() =>
            {
                using (MainForm form = new MainForm())
                using (TcpClient client = Connect(form.ListeningPort))
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

                    Write(stream, ProtocolConstants.PacketTypeStopStream, 7u, new byte[0]);
                    AssertEndOfStream(stream);

                    WaitForVisibleText(form, "Rendered frames: 3");
                    ReceiverRuntime runtime = (ReceiverRuntime)typeof(MainForm)
                        .GetField("runtime", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(form);
                    IReadOnlyList<FakePcmFrame> renderedFrames = runtime.Renderer.RenderedFrames;
                    Assert.AreEqual(3, runtime.Renderer.RenderedCount);
                    Assert.AreEqual(3, renderedFrames.Count);
                    for (int i = 0; i < captureTimestamps.Length; i++)
                    {
                        Assert.AreEqual((uint)(i + 1), renderedFrames[i].FrameNumber);
                        Assert.AreEqual(captureTimestamps[i], renderedFrames[i].CaptureTimestamp);
                        Assert.AreEqual((ushort)21, renderedFrames[i].FrameDuration);
                        Assert.AreEqual(4096, renderedFrames[i].PcmBytes.Length);
                        AssertStereoEnergy(renderedFrames[i].PcmBytes);
                    }
                }
            });
        }

        [TestMethod]
        public void DisposeStopsReceiverRuntime()
        {
            int port = 0;

            RunSta(() =>
            {
                MainForm form = new MainForm();
                port = form.ListeningPort;
                form.Dispose();
            });

            AssertConnectFails(port);
        }

        private static void RunSta(Action action)
        {
            Exception error = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (error != null)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }

        private static string VisibleText(Control parent)
        {
            List<string> parts = new List<string>();
            foreach (Control control in parent.Controls)
            {
                if (!string.IsNullOrEmpty(control.Text))
                {
                    parts.Add(control.Text);
                }
            }

            return string.Join("\n", parts.ToArray());
        }

        private static void WaitForVisibleText(Control parent, string expected)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < SocketTimeoutMilliseconds)
            {
                if (VisibleText(parent).Contains(expected))
                {
                    return;
                }

                Application.DoEvents();
                Thread.Yield();
            }

            StringAssert.Contains(VisibleText(parent), expected);
        }

        private static void AssertStereoEnergy(byte[] pcmBytes)
        {
            Assert.AreEqual(0, pcmBytes.Length % 4);
            long leftEnergy = 0;
            long rightEnergy = 0;
            for (int i = 0; i < pcmBytes.Length; i += 4)
            {
                leftEnergy += Math.Abs((int)BitConverter.ToInt16(pcmBytes, i));
                rightEnergy += Math.Abs((int)BitConverter.ToInt16(pcmBytes, i + 2));
            }

            Assert.IsTrue(leftEnergy > 0, "Expected non-zero left-channel PCM energy.");
            Assert.IsTrue(rightEnergy > 0, "Expected non-zero right-channel PCM energy.");
        }

        private static void AssertEndOfStream(NetworkStream stream)
        {
            stream.ReadTimeout = 100;
            byte[] buffer = new byte[256];
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < SocketTimeoutMilliseconds)
            {
                try
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        return;
                    }

                    Assert.Fail("Received an unexpected packet while waiting for stream EOF.");
                }
                catch (System.IO.IOException error)
                {
                    if (!IsSocketTimeout(error))
                    {
                        return;
                    }

                    Thread.Yield();
                }
            }

            Assert.Fail("Timed out waiting for stream EOF.");
        }

        private static bool IsSocketTimeout(Exception error)
        {
            for (Exception current = error; current != null; current = current.InnerException)
            {
                SocketException socketError = current as SocketException;
                if (socketError != null && socketError.SocketErrorCode == SocketError.TimedOut)
                {
                    return true;
                }
            }

            return false;
        }

        private static TcpClient Connect(int port)
        {
            TcpClient client = new TcpClient();
            client.ReceiveTimeout = SocketTimeoutMilliseconds;
            client.SendTimeout = SocketTimeoutMilliseconds;
            client.Connect(IPAddress.Loopback, port);
            return client;
        }

        private static void AssertConnectFails(int port)
        {
            using (TcpClient client = new TcpClient())
            {
                IAsyncResult result = client.BeginConnect(IPAddress.Loopback, port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(SocketTimeoutMilliseconds))
                {
                    client.Close();
                    Assert.Fail("Timed out waiting for connection failure after MainForm.Dispose.");
                }

                try
                {
                    client.EndConnect(result);
                    Assert.Fail("Expected connection to fail after MainForm.Dispose.");
                }
                catch (SocketException)
                {
                }
            }
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
