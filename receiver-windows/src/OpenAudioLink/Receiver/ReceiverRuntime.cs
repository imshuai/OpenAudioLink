using System;
using System.Net;

namespace OpenAudioLink.Receiver
{
    public sealed class ReceiverRuntime : IDisposable
    {
        private readonly TcpReceiver receiver;

        private ReceiverRuntime(AudioFrameQueue queue, FakeAudioRenderer renderer, TcpReceiver receiver)
        {
            Queue = queue;
            Renderer = renderer;
            this.receiver = receiver;
            Port = receiver.Port;
        }

        public int Port { get; }

        public AudioFrameQueue Queue { get; }

        public FakeAudioRenderer Renderer { get; }

        public static ReceiverRuntime StartLoopback(int queueCapacity = 8)
        {
            return Start(IPAddress.Loopback, 0, queueCapacity);
        }

        public static ReceiverRuntime Start(IPAddress address, int port, int queueCapacity = 8)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            AudioFrameQueue queue = new AudioFrameQueue(queueCapacity);
            FakeAacDecoder decoder = new FakeAacDecoder();
            FakeAudioRenderer renderer = new FakeAudioRenderer();
            TcpReceiver receiver = TcpReceiver.Start(address, port, payload =>
            {
                queue.Enqueue(payload);
                renderer.Drain(queue, decoder);
            });

            return new ReceiverRuntime(queue, renderer, receiver);
        }

        public void Dispose()
        {
            receiver.Dispose();
        }
    }
}
