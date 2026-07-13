using System;

namespace OpenAudioLink.Receiver
{
    public sealed class FakePcmFrame
    {
        private readonly byte[] pcmBytes;

        public FakePcmFrame(uint frameNumber, ulong captureTimestamp, ushort frameDuration, byte[] pcmBytes)
        {
            if (pcmBytes == null)
            {
                throw new ArgumentNullException(nameof(pcmBytes));
            }

            FrameNumber = frameNumber;
            CaptureTimestamp = captureTimestamp;
            FrameDuration = frameDuration;
            this.pcmBytes = (byte[])pcmBytes.Clone();
        }

        public uint FrameNumber { get; }

        public ulong CaptureTimestamp { get; }

        public ushort FrameDuration { get; }

        public byte[] PcmBytes
        {
            get { return (byte[])pcmBytes.Clone(); }
        }
    }
}
