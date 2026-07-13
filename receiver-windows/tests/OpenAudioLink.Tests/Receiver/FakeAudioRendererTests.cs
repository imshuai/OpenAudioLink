using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Receiver;

namespace OpenAudioLink.Tests.Receiver
{
    [TestClass]
    public sealed class FakeAudioRendererTests
    {
        [TestMethod]
        public void DrainMovesQueuedFramesToRenderedHistoryInFifoOrder()
        {
            AudioFrameQueue queue = new AudioFrameQueue(3);
            FakeAudioRenderer renderer = new FakeAudioRenderer();

            queue.Enqueue(Payload(0x01));
            queue.Enqueue(Payload(0x02));
            queue.Enqueue(Payload(0x03));

            int drained = renderer.Drain(queue);

            Assert.AreEqual(3, drained);
            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual(3, renderer.RenderedCount);
            IReadOnlyList<byte[]> rendered = renderer.RenderedFrames;
            CollectionAssert.AreEqual(Payload(0x01), rendered[0]);
            CollectionAssert.AreEqual(Payload(0x02), rendered[1]);
            CollectionAssert.AreEqual(Payload(0x03), rendered[2]);
        }

        [TestMethod]
        public void DrainAppendsAcrossCalls()
        {
            AudioFrameQueue queue = new AudioFrameQueue(2);
            FakeAudioRenderer renderer = new FakeAudioRenderer();

            queue.Enqueue(Payload(0x10));
            Assert.AreEqual(1, renderer.Drain(queue));

            queue.Enqueue(Payload(0x20));
            Assert.AreEqual(1, renderer.Drain(queue));

            Assert.AreEqual(2, renderer.RenderedCount);
            IReadOnlyList<byte[]> rendered = renderer.RenderedFrames;
            CollectionAssert.AreEqual(Payload(0x10), rendered[0]);
            CollectionAssert.AreEqual(Payload(0x20), rendered[1]);
        }

        [TestMethod]
        public void DrainEmptyQueueReturnsZeroAndCountsUnderflow()
        {
            AudioFrameQueue queue = new AudioFrameQueue(1);
            FakeAudioRenderer renderer = new FakeAudioRenderer();

            int drained = renderer.Drain(queue);

            Assert.AreEqual(0, drained);
            Assert.AreEqual(0, renderer.RenderedCount);
            Assert.AreEqual(1UL, queue.UnderflowCount);
        }

        [TestMethod]
        public void DrainRejectsNullQueue()
        {
            FakeAudioRenderer renderer = new FakeAudioRenderer();

            Assert.ThrowsException<ArgumentNullException>(() => renderer.Drain(null));
        }

        [TestMethod]
        public void RenderedHistoryIsIsolatedFromCallerMutations()
        {
            AudioFrameQueue queue = new AudioFrameQueue(1);
            FakeAudioRenderer renderer = new FakeAudioRenderer();
            byte[] payload = Payload(0x30);

            queue.Enqueue(payload);
            payload[0] = 0x7f;
            renderer.Drain(queue);

            IReadOnlyList<byte[]> rendered = renderer.RenderedFrames;
            CollectionAssert.AreEqual(Payload(0x30), rendered[0]);

            rendered[0][0] = 0x7e;

            CollectionAssert.AreEqual(Payload(0x30), renderer.RenderedFrames[0]);
        }

        private static byte[] Payload(byte first)
        {
            return new byte[] { first, (byte)(first + 1) };
        }
    }
}
