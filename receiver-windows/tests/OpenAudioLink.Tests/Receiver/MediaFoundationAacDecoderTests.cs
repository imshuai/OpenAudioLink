using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Receiver;
using OpenAudioLink.Tests;

namespace OpenAudioLink.Tests.Receiver
{
    [TestClass]
    public sealed class MediaFoundationAacDecoderTests
    {
        private const string ContinuousFixture =
            "testdata/audio/aac-lc-48k-stereo-6frames.adts";

        [TestMethod]
        public void CiUsesExpectedProcessArchitecture()
        {
            string expected = Environment.GetEnvironmentVariable("OAL_TEST_ARCHITECTURE");
            if (string.IsNullOrEmpty(expected))
            {
                return;
            }
            if (expected == "x86")
            {
                Assert.AreEqual(4, IntPtr.Size);
                return;
            }
            if (expected == "x64")
            {
                Assert.AreEqual(8, IntPtr.Size);
                return;
            }
            Assert.Fail("Unknown OAL_TEST_ARCHITECTURE: " + expected);
        }

        [TestMethod]
        public void CanonicalAdtsSplitterReturnsSixRawAccessUnits()
        {
            IReadOnlyList<byte[]> frames = SplitAdts(TestFixtures.Read(ContinuousFixture));
            Assert.AreEqual(6, frames.Count);
            CollectionAssert.AreEqual(
                TestFixtures.Read("testdata/audio/aac-lc-48k-stereo-1024.raw"),
                frames[0]);
        }

        [TestMethod]
        public void AdtsSplitterRejectsTruncatedFinalFrame()
        {
            byte[] data = TestFixtures.Read(ContinuousFixture);
            Array.Resize(ref data, data.Length - 1);
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(data));
        }

        [TestMethod]
        public void SubmitRejectsNullAndEmptyInput()
        {
            RunMta(() =>
            {
                using (MediaFoundationAacDecoder decoder = new MediaFoundationAacDecoder())
                {
                    Assert.ThrowsException<ArgumentNullException>(() => decoder.Submit(null));
                    Assert.ThrowsException<ArgumentException>(() => decoder.Submit(new byte[0]));
                }
            });
        }

        [TestMethod]
        public void CallsFromAnotherThreadAreRejected()
        {
            RunMta(() =>
            {
                using (MediaFoundationAacDecoder decoder = new MediaFoundationAacDecoder())
                {
                    Exception error = CaptureOnMta(
                        () => decoder.Submit(new byte[] { 0x01 }));
                    Assert.IsNotNull(error);
                    Assert.AreEqual(typeof(InvalidOperationException), error.GetType());
                }
            });
        }

        [TestMethod]
        public void DrainIsIdempotentAndClosesInput()
        {
            RunMta(() =>
            {
                using (MediaFoundationAacDecoder decoder = new MediaFoundationAacDecoder())
                {
                    Assert.AreEqual(0, decoder.Drain().Count);
                    Assert.AreEqual(0, decoder.Drain().Count);
                    Assert.ThrowsException<InvalidOperationException>(
                        () => decoder.Submit(new byte[] { 0x01 }));
                }
            });
        }

        [TestMethod]
        public void DisposeIsIdempotentAndRejectsFurtherCalls()
        {
            RunMta(() =>
            {
                MediaFoundationAacDecoder decoder = new MediaFoundationAacDecoder();
                decoder.Dispose();
                Assert.ThrowsException<ObjectDisposedException>(() => decoder.Drain());
                decoder.Dispose();
            });
        }

        [TestMethod]
        public void CanonicalFixtureDecodesTwiceToCompleteStereoPcm()
        {
            RunMta(() =>
            {
                AssertPcm(DecodeFixture());
                AssertPcm(DecodeFixture());
            });
        }

        private static byte[] DecodeFixture()
        {
            List<byte> pcm = new List<byte>();
            IReadOnlyList<byte[]> frames = SplitAdts(TestFixtures.Read(ContinuousFixture));
            using (MediaFoundationAacDecoder decoder = new MediaFoundationAacDecoder())
            {
                foreach (byte[] frame in frames)
                {
                    AddChunks(pcm, decoder.Submit(frame));
                }
                AddChunks(pcm, decoder.Drain());
            }
            return pcm.ToArray();
        }

        private static void AddChunks(List<byte> destination, IReadOnlyList<byte[]> chunks)
        {
            foreach (byte[] chunk in chunks)
            {
                destination.AddRange(chunk);
            }
        }

        private static void AssertPcm(byte[] pcm)
        {
            Assert.AreEqual(24576, pcm.Length);
            long leftEnergy = 0;
            long rightEnergy = 0;
            for (int offset = 0; offset < pcm.Length; offset += 4)
            {
                short left = (short)(pcm[offset] | (pcm[offset + 1] << 8));
                short right = (short)(pcm[offset + 2] | (pcm[offset + 3] << 8));
                leftEnergy += Math.Abs((long)left);
                rightEnergy += Math.Abs((long)right);
            }
            Assert.IsTrue(leftEnergy > 0, "left channel is silent");
            Assert.IsTrue(rightEnergy > 0, "right channel is silent");
        }

        private static IReadOnlyList<byte[]> SplitAdts(byte[] data)
        {
            List<byte[]> frames = new List<byte[]>();
            int offset = 0;
            while (offset < data.Length)
            {
                if (data.Length - offset < 7)
                {
                    throw new InvalidDataException("truncated ADTS header");
                }
                if (data[offset] != 0xff || (data[offset + 1] & 0xf0) != 0xf0)
                {
                    throw new InvalidDataException("invalid ADTS sync");
                }
                if ((data[offset + 1] & 1) != 1)
                {
                    throw new InvalidDataException("CRC-bearing ADTS is unsupported");
                }
                int length =
                    ((data[offset + 3] & 3) << 11)
                    | (data[offset + 4] << 3)
                    | ((data[offset + 5] >> 5) & 7);
                if (length <= 7 || offset + length > data.Length)
                {
                    throw new InvalidDataException("truncated ADTS frame");
                }
                byte[] raw = new byte[length - 7];
                Buffer.BlockCopy(data, offset + 7, raw, 0, raw.Length);
                frames.Add(raw);
                offset += length;
            }
            if (frames.Count != 6)
            {
                throw new InvalidDataException("continuous fixture must contain six frames");
            }
            return frames;
        }

        private static Exception CaptureOnMta(Action action)
        {
            Exception error = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("MTA test thread did not stop");
            }
            return error;
        }

        private static void RunMta(Action action)
        {
            Exception error = CaptureOnMta(action);
            if (error != null)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }
    }
}
