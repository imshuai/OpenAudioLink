using System;
using System.Collections.Generic;

namespace OpenAudioLink.Receiver
{
    public sealed class AudioFrameQueue
    {
        private readonly object gate = new object();
        private readonly Queue<byte[]> frames = new Queue<byte[]>();
        private ulong droppedFrames;
        private ulong underflowCount;

        public AudioFrameQueue(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
        }

        public int Capacity { get; }

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return frames.Count;
                }
            }
        }

        public ulong DroppedFrames
        {
            get
            {
                lock (gate)
                {
                    return droppedFrames;
                }
            }
        }

        public ulong UnderflowCount
        {
            get
            {
                lock (gate)
                {
                    return underflowCount;
                }
            }
        }

        public void Enqueue(byte[] payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            byte[] acceptedPayload = (byte[])payload.Clone();

            lock (gate)
            {
                if (frames.Count == Capacity)
                {
                    frames.Dequeue();
                    droppedFrames++;
                }

                frames.Enqueue(acceptedPayload);
            }
        }

        public bool TryDequeue(out byte[] payload)
        {
            lock (gate)
            {
                if (frames.Count == 0)
                {
                    underflowCount++;
                    payload = null;
                    return false;
                }

                payload = frames.Dequeue();
                return true;
            }
        }
    }
}
