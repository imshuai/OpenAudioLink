using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using OpenAudioLink.Protocol;

namespace OpenAudioLink.Receiver
{
    public sealed class TcpReceiver : IDisposable
    {
        private readonly TcpListener listener;
        private int active;
        private int disposed;
        private long nextSessionId;

        private TcpReceiver(TcpListener listener)
        {
            this.listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            ThreadPool.QueueUserWorkItem(_ => AcceptLoop());
        }

        public int Port { get; }

        public static TcpReceiver StartLoopback()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new TcpReceiver(listener);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                listener.Stop();
            }
        }

        private void AcceptLoop()
        {
            while (Volatile.Read(ref disposed) == 0)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => Handle(client));
                }
                catch (SocketException) when (Volatile.Read(ref disposed) != 0)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private void Handle(TcpClient client)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
                {
                    try
                    {
                        PacketReader.ReadPacket(stream);
                        Write(stream, ReceiverSession.BusyWelcome());
                    }
                    catch (IOException) { }
                    catch (PacketParseException) { }
                    return;
                }

                try
                {
                    ReceiverSession session = new ReceiverSession((ulong)Interlocked.Increment(ref nextSessionId));
                    while (session.State != ReceiverSessionState.Stopped)
                    {
                        byte[] response = session.Process(PacketReader.ReadPacket(stream));
                        if (response != null)
                        {
                            Write(stream, response);
                        }
                    }
                }
                catch (IOException) { }
                catch (PacketParseException) { }
                finally
                {
                    Interlocked.Exchange(ref active, 0);
                }
            }
        }

        private static void Write(Stream stream, byte[] packet)
        {
            stream.Write(packet, 0, packet.Length);
        }
    }
}
