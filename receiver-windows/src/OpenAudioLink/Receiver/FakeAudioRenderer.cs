using System;
using System.Collections.Generic;

namespace OpenAudioLink.Receiver
{
    public sealed class FakeAudioRenderer
    {
        private readonly List<FakePcmFrame> renderedFrames = new List<FakePcmFrame>();

        public int RenderedCount
        {
            get { return renderedFrames.Count; }
        }

        public IReadOnlyList<FakePcmFrame> RenderedFrames
        {
            get { return renderedFrames.ToArray(); }
        }

        public void Render(FakePcmFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            renderedFrames.Add(new FakePcmFrame(frame.FrameNumber, frame.CaptureTimestamp, frame.FrameDuration, frame.PcmBytes));
        }

        public int Drain(AudioFrameQueue queue, FakeAacDecoder decoder)
        {
            if (queue == null)
            {
                throw new ArgumentNullException(nameof(queue));
            }

            if (decoder == null)
            {
                throw new ArgumentNullException(nameof(decoder));
            }

            int drained = 0;
            while (queue.TryDequeue(out byte[] payload))
            {
                Render(decoder.Decode(payload));
                drained++;
            }

            return drained;
        }
    }
}
