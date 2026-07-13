using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;
using OpenAudioLink.UI;

namespace OpenAudioLink.Tests.UI
{
    [TestClass]
    public sealed class MainFormTests
    {
        private const int SocketTimeoutMilliseconds = 5000;
        private const int ConnectFailureTimeoutMilliseconds = 1000;

        [TestMethod]
        public void ConstructorStartsReceiverRuntimeAndShowsPort()
        {
            RunSta(() =>
            {
                using (MainForm form = new MainForm())
                {
                    Assert.AreNotEqual(0, form.ListeningPort);
                    StringAssert.Contains(VisibleText(form), "Listening on TCP port " + form.ListeningPort);

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
                if (!result.AsyncWaitHandle.WaitOne(ConnectFailureTimeoutMilliseconds))
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
