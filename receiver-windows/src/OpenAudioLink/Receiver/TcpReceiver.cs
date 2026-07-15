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
        private readonly Action<byte[]> audioSink;
        private readonly Action streamStarted;
        private readonly Action streamEnded;
        private readonly object lifecycleGate = new object();
        private const int SocketTimeoutMilliseconds = 5000;
        private int active;
        private int disposed;
        private long nextSessionId;
        private TcpClient currentClient;

        private TcpReceiver(
            TcpListener listener,
            Action<byte[]> audioSink,
            Action streamStarted,
            Action streamEnded)
        {
            this.listener = listener;
            this.audioSink = audioSink ?? (_ => { });
            this.streamStarted = streamStarted ?? (() => { });
            this.streamEnded = streamEnded ?? (() => { });
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            ThreadPool.QueueUserWorkItem(_ => AcceptLoop());
        }

        public int Port { get; }

        public static TcpReceiver Start(
            IPAddress address,
            int port,
            Action<byte[]> audioSink = null,
            Action streamStarted = null,
            Action streamEnded = null)
        {
            TcpListener listener = new TcpListener(address, port);
            listener.Start();
            return new TcpReceiver(listener, audioSink, streamStarted, streamEnded);
        }

        public static TcpReceiver StartLoopback(
            Action<byte[]> audioSink = null,
            Action streamStarted = null,
            Action streamEnded = null)
        {
            return Start(IPAddress.Loopback, 0, audioSink, streamStarted, streamEnded);
        }

        public void Dispose()
        {
            TcpClient client;
            lock (lifecycleGate)
            {
                if (disposed != 0)
                {
                    return;
                }

                disposed = 1;
                client = currentClient;
                currentClient = null;
            }

            listener.Stop();
            client?.Close();
        }

        private void AcceptLoop()
        {
            while (Volatile.Read(ref disposed) == 0)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    client.ReceiveTimeout = SocketTimeoutMilliseconds;
                    client.SendTimeout = SocketTimeoutMilliseconds;
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
            bool ownsActiveSlot = false;
            bool streamActive = false;
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
                    {
                        PacketReader.ReadPacket(stream);
                        byte[] busy = ReceiverSession.BusyWelcome();
                        stream.Write(busy, 0, busy.Length);
                        return;
                    }
                    ownsActiveSlot = true;

                    lock (lifecycleGate)
                    {
                        if (Volatile.Read(ref disposed) != 0)
                        {
                            return;
                        }

                        currentClient = client;
                    }

                    ReceiverSession session = new ReceiverSession((ulong)Interlocked.Increment(ref nextSessionId), audioSink);
                    while (session.State != ReceiverSessionState.Stopped)
                    {
                        ReceiverSessionState previousState = session.State;
                        byte[] response = session.Process(PacketReader.ReadPacket(stream));
                        if (previousState == ReceiverSessionState.WaitingForStartStream && session.State == ReceiverSessionState.Streaming)
                        {
                            lock (lifecycleGate)
                            {
                                if (Volatile.Read(ref disposed) != 0)
                                {
                                    return;
                                }

                                streamStarted();
                                streamActive = true;
                            }
                        }

                        if (response != null)
                        {
                            stream.Write(response, 0, response.Length);
                        }
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (PacketParseException) { }
                finally
                {
                    try
                    {
                        if (streamActive)
                        {
                            try { streamEnded(); }
                            catch (PacketParseException) { }
                        }
                    }
                    finally
                    {
                        if (ownsActiveSlot)
                        {
                            lock (lifecycleGate)
                            {
                                if (ReferenceEquals(currentClient, client))
                                {
                                    currentClient = null;
                                }
                            }

                            Interlocked.Exchange(ref active, 0);
                        }
                    }
                }
            }
        }
    }
}
