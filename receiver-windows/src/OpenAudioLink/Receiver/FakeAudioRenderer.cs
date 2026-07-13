using System;
using System.Collections.Generic;

namespace OpenAudioLink.Receiver
{
    public sealed class FakeAudioRenderer
    {
        private readonly List<byte[]> renderedFrames = new List<byte[]>();

        public int RenderedCount
        {
            get { return renderedFrames.Count; }
        }

        public IReadOnlyList<byte[]> RenderedFrames
        {
            get
            {
                byte[][] snapshot = new byte[renderedFrames.Count][];
                for (int i = 0; i < renderedFrames.Count; i++)
                {
                    snapshot[i] = (byte[])renderedFrames[i].Clone();
                }

                return snapshot;
            }
        }

        public int Drain(AudioFrameQueue queue)
        {
            if (queue == null)
            {
                throw new ArgumentNullException(nameof(queue));
            }

            int drained = 0;
            while (queue.TryDequeue(out byte[] payload))
            {
                renderedFrames.Add((byte[])payload.Clone());
                drained++;
            }

            return drained;
        }
    }
}
