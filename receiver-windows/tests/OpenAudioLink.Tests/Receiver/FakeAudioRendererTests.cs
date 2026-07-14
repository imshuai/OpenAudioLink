using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;
using OpenAudioLink.Receiver;

namespace OpenAudioLink.Tests.Receiver
{
    [TestClass]
    public sealed class FakeAudioRendererTests
    {
        [TestMethod]
        public void DrainDecodesQueuedFramesToRenderedHistoryInFifoOrder()
        {
            AudioFrameQueue queue = new AudioFrameQueue(3);
            FakeAudioRenderer renderer = new FakeAudioRenderer();
            FakeAacDecoder decoder = new FakeAacDecoder();

            queue.Enqueue(AudioPayload(1u, 100UL, Payload(0x01)));
            queue.Enqueue(AudioPayload(2u, 120UL, Payload(0x02)));
            queue.Enqueue(AudioPayload(3u, 140UL, Payload(0x03)));

            int drained = renderer.Drain(queue, decoder);

            Assert.AreEqual(3, drained);
            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual(3, renderer.RenderedCount);
            IReadOnlyList<FakePcmFrame> rendered = renderer.RenderedFrames;
            AssertFrame(rendered[0], 1u, 100UL, Payload(0x01));
            AssertFrame(rendered[1], 2u, 120UL, Payload(0x02));
            AssertFrame(rendered[2], 3u, 140UL, Payload(0x03));
        }

        [TestMethod]
        public void DrainAppendsAcrossCalls()
        {
            AudioFrameQueue queue = new AudioFrameQueue(2);
            FakeAudioRenderer renderer = new FakeAudioRenderer();
            FakeAacDecoder decoder = new FakeAacDecoder();

            queue.Enqueue(AudioPayload(1u, 100UL, Payload(0x10)));
            Assert.AreEqual(1, renderer.Drain(queue, decoder));

            queue.Enqueue(AudioPayload(2u, 120UL, Payload(0x20)));
            Assert.AreEqual(1, renderer.Drain(queue, decoder));

            Assert.AreEqual(2, renderer.RenderedCount);
            IReadOnlyList<FakePcmFrame> rendered = renderer.RenderedFrames;
            AssertFrame(rendered[0], 1u, 100UL, Payload(0x10));
            AssertFrame(rendered[1], 2u, 120UL, Payload(0x20));
        }

        [TestMethod]
        public void DrainEmptyQueueReturnsZeroAndCountsUnderflow()
        {
            AudioFrameQueue queue = new AudioFrameQueue(1);
            FakeAudioRenderer renderer = new FakeAudioRenderer();

            int drained = renderer.Drain(queue, new FakeAacDecoder());

            Assert.AreEqual(0, drained);
            Assert.AreEqual(0, renderer.RenderedCount);
            Assert.AreEqual(1UL, queue.UnderflowCount);
        }

        [TestMethod]
        public void DrainRejectsNullQueueOrDecoder()
        {
            AudioFrameQueue queue = new AudioFrameQueue(1);
            FakeAudioRenderer renderer = new FakeAudioRenderer();
            FakeAacDecoder decoder = new FakeAacDecoder();

            Assert.ThrowsException<ArgumentNullException>(() => renderer.Drain(null, decoder));
            Assert.ThrowsException<ArgumentNullException>(() => renderer.Drain(queue, null));
        }

        [TestMethod]
        public void RenderRejectsNullFrame()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new FakeAudioRenderer().Render(null));
        }

        [TestMethod]
        public void RenderedHistoryIsIsolatedFromCallerMutations()
        {
            FakeAudioRenderer renderer = new FakeAudioRenderer();
            byte[] pcmBytes = Payload(0x30);
            FakePcmFrame frame = new FakePcmFrame(1u, 100UL, 21, pcmBytes);

            renderer.Render(frame);
            pcmBytes[0] = 0x7f;

            IReadOnlyList<FakePcmFrame> rendered = renderer.RenderedFrames;
            AssertFrame(rendered[0], 1u, 100UL, Payload(0x30));

            byte[] returned = rendered[0].PcmBytes;
            returned[0] = 0x7e;

            AssertFrame(renderer.RenderedFrames[0], 1u, 100UL, Payload(0x30));
        }

        private static byte[] AudioPayload(uint frameNumber, ulong captureTimestamp, byte[] encoded)
        {
            return HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, frameNumber, captureTimestamp, 21, encoded);
        }

        private static byte[] Payload(byte first)
        {
            return new byte[] { first, (byte)(first + 1) };
        }

        private static void AssertFrame(FakePcmFrame frame, uint frameNumber, ulong captureTimestamp, byte[] pcmBytes)
        {
            Assert.AreEqual(frameNumber, frame.FrameNumber);
            Assert.AreEqual(captureTimestamp, frame.CaptureTimestamp);
            Assert.AreEqual((ushort)21, frame.FrameDuration);
            CollectionAssert.AreEqual(pcmBytes, frame.PcmBytes);
        }
    }
}
