using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Receiver;

namespace OpenAudioLink.Tests.Receiver
{
    [TestClass]
    public sealed class AudioFrameQueueTests
    {
        [TestMethod]
        public void ConstructorRejectsNonPositiveCapacity()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new AudioFrameQueue(0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new AudioFrameQueue(-1));
        }

        [TestMethod]
        public void EnqueueRejectsNull()
        {
            AudioFrameQueue queue = new AudioFrameQueue(1);

            Assert.ThrowsException<ArgumentNullException>(() => queue.Enqueue(null));
        }

        [TestMethod]
        public void EnqueueThenDequeueReturnsSameBytesAndUpdatesCount()
        {
            AudioFrameQueue queue = new AudioFrameQueue(2);
            byte[] payload = Payload(0x10);

            queue.Enqueue(payload);

            Assert.AreEqual(1, queue.Count);
            Assert.IsTrue(queue.TryDequeue(out byte[] dequeued));
            CollectionAssert.AreEqual(payload, dequeued);
            Assert.AreEqual(0, queue.Count);
        }

        [TestMethod]
        public void EnqueueClonesCallerPayload()
        {
            AudioFrameQueue queue = new AudioFrameQueue(1);
            byte[] payload = Payload(0x20);

            queue.Enqueue(payload);
            payload[0] = 0x7f;

            Assert.IsTrue(queue.TryDequeue(out byte[] dequeued));
            CollectionAssert.AreEqual(Payload(0x20), dequeued);
        }

        [TestMethod]
        public void TryDequeueReturnsFramesInFifoOrder()
        {
            AudioFrameQueue queue = new AudioFrameQueue(3);

            queue.Enqueue(Payload(0x01));
            queue.Enqueue(Payload(0x02));
            queue.Enqueue(Payload(0x03));

            Assert.IsTrue(queue.TryDequeue(out byte[] first));
            Assert.IsTrue(queue.TryDequeue(out byte[] second));
            Assert.IsTrue(queue.TryDequeue(out byte[] third));
            CollectionAssert.AreEqual(Payload(0x01), first);
            CollectionAssert.AreEqual(Payload(0x02), second);
            CollectionAssert.AreEqual(Payload(0x03), third);
        }

        [TestMethod]
        public void FullQueueDropsOldestFrameAndCountsDrop()
        {
            AudioFrameQueue queue = new AudioFrameQueue(2);

            queue.Enqueue(Payload(0x01));
            queue.Enqueue(Payload(0x02));
            queue.Enqueue(Payload(0x03));

            Assert.AreEqual(2, queue.Count);
            Assert.AreEqual(1UL, queue.DroppedFrames);
            Assert.IsTrue(queue.TryDequeue(out byte[] first));
            Assert.IsTrue(queue.TryDequeue(out byte[] second));
            CollectionAssert.AreEqual(Payload(0x02), first);
            CollectionAssert.AreEqual(Payload(0x03), second);
        }

        [TestMethod]
        public void EmptyDequeueReturnsFalseAndCountsUnderflow()
        {
            AudioFrameQueue queue = new AudioFrameQueue(1);

            bool result = queue.TryDequeue(out byte[] payload);

            Assert.IsFalse(result);
            Assert.IsNull(payload);
            Assert.AreEqual(1UL, queue.UnderflowCount);
        }

        private static byte[] Payload(byte first)
        {
            return new byte[] { first, (byte)(first + 1) };
        }
    }
}
