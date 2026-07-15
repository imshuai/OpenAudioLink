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
        private const string MediaCodecInteropEnabled =
            "OAL_MEDIACODEC_INTEROP";
        private const string MediaCodecInteropPath =
            "OAL_MEDIACODEC_ADTS_PATH";
        private const int MediaCodecInputFrameCount = 12;
        private const int MediaCodecAddedCandidateCount = 1;
        private const int MediaCodecExpectedOutputCandidateCount =
            MediaCodecInputFrameCount + MediaCodecAddedCandidateCount;

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
        public void AdtsSplitterRejectsInvalidHeaderTrailingByteAndWrongFrameCount()
        {
            byte[] invalidSync = TestFixtures.Read(ContinuousFixture);
            invalidSync[0] = 0;
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(invalidSync));

            byte[] invalidSecondSyncByte = TestFixtures.Read(ContinuousFixture);
            invalidSecondSyncByte[1] = (byte)(invalidSecondSyncByte[1] & 0x0f);
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(invalidSecondSyncByte));

            byte[] crcBearing = TestFixtures.Read(ContinuousFixture);
            crcBearing[1] = (byte)(crcBearing[1] & 0xfe);
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(crcBearing));

            byte[] wrongMpegVersion = TestFixtures.Read(ContinuousFixture);
            wrongMpegVersion[1] = (byte)(wrongMpegVersion[1] | 0x08);
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(wrongMpegVersion));

            byte[] wrongLayer = TestFixtures.Read(ContinuousFixture);
            wrongLayer[1] = (byte)(wrongLayer[1] | 0x02);
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(wrongLayer));

            byte[] wrongProfile = TestFixtures.Read(ContinuousFixture);
            wrongProfile[2] = (byte)(wrongProfile[2] & 0x3f);
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(wrongProfile));

            byte[] wrongRate = TestFixtures.Read(ContinuousFixture);
            wrongRate[2] = (byte)((wrongRate[2] & 0xc3) | (4 << 2));
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(wrongRate));

            byte[] wrongChannels = TestFixtures.Read(ContinuousFixture);
            wrongChannels[2] = (byte)(wrongChannels[2] & 0xfe);
            wrongChannels[3] = (byte)((wrongChannels[3] & 0x3f) | (1 << 6));
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(wrongChannels));

            byte[] multipleRawBlocks = TestFixtures.Read(ContinuousFixture);
            multipleRawBlocks[6] = (byte)(multipleRawBlocks[6] | 1);
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(multipleRawBlocks));

            byte[] invalidLength = TestFixtures.Read(ContinuousFixture);
            invalidLength[3] = (byte)(invalidLength[3] & 0xfc);
            invalidLength[4] = 0;
            invalidLength[5] = (byte)((invalidLength[5] & 0x1f) | (7 << 5));
            Array.Resize(ref invalidLength, 7);
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(invalidLength, 1));

            byte[] trailing = TestFixtures.Read(ContinuousFixture);
            Array.Resize(ref trailing, trailing.Length + 1);
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(trailing));
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(new byte[0]));
            Assert.ThrowsException<InvalidDataException>(() => SplitAdts(
                TestFixtures.Read(ContinuousFixture),
                5));
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
                    AssertWrongThread(() => decoder.Submit(new byte[] { 0x01 }));
                    AssertWrongThread(() => decoder.Drain());
                    AssertWrongThread(() => decoder.Dispose());
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

        [TestMethod]
        public void AndroidMediaCodecArtifactDecodesToCompleteStereoPcm()
        {
            string enabled = Environment.GetEnvironmentVariable(MediaCodecInteropEnabled);
            if (string.IsNullOrEmpty(enabled))
            {
                return;
            }
            Assert.AreEqual("1", enabled);

            string path = Environment.GetEnvironmentVariable(MediaCodecInteropPath);
            Assert.IsFalse(string.IsNullOrEmpty(path), "interop artifact path is missing");
            Assert.IsTrue(File.Exists(path), "interop artifact does not exist: " + path);

            IReadOnlyList<byte[]> frames = SplitAdts(
                File.ReadAllBytes(path),
                MediaCodecExpectedOutputCandidateCount);
            RunMta(() =>
            {
                foreach (byte[] frame in frames)
                {
                    Assert.AreEqual(
                        4096,
                        DecodeFrames(new[] { frame }).Length,
                        "MediaCodec output candidate is not exactly one AAC access unit");
                }
                byte[] pcm = DecodeFrames(frames);
                AssertPcm(
                    pcm,
                    checked(MediaCodecExpectedOutputCandidateCount * 4096));
                Console.WriteLine(
                    "MediaCodec interop decoded " + frames.Count + " access units to "
                    + pcm.Length + " PCM bytes.");
            });
        }

        private static byte[] DecodeFixture()
        {
            return DecodeFrames(SplitAdts(TestFixtures.Read(ContinuousFixture)));
        }

        private static byte[] DecodeFrames(IReadOnlyList<byte[]> frames)
        {
            List<byte> pcm = new List<byte>();
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

        private static void AssertPcm(byte[] pcm, int expectedLength = 24576)
        {
            Assert.AreEqual(expectedLength, pcm.Length);
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

        private static IReadOnlyList<byte[]> SplitAdts(
            byte[] data,
            int? expectedFrameCount = 6)
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
                if ((data[offset + 1] & 0x0e) != 0)
                {
                    throw new InvalidDataException("ADTS must use MPEG-4 layer 0");
                }
                if ((data[offset + 1] & 1) != 1)
                {
                    throw new InvalidDataException("CRC-bearing ADTS is unsupported");
                }
                int profile = (data[offset + 2] >> 6) & 3;
                int sampleRateIndex = (data[offset + 2] >> 2) & 15;
                int channelConfiguration =
                    ((data[offset + 2] & 1) << 2)
                    | ((data[offset + 3] >> 6) & 3);
                if (profile != 1 || sampleRateIndex != 3 || channelConfiguration != 2)
                {
                    throw new InvalidDataException(
                        "ADTS must be AAC-LC, 48 kHz, stereo");
                }
                if ((data[offset + 6] & 3) != 0)
                {
                    throw new InvalidDataException("ADTS frame must contain one raw block");
                }
                int length =
                    ((data[offset + 3] & 3) << 11)
                    | (data[offset + 4] << 3)
                    | ((data[offset + 5] >> 5) & 7);
                if (length <= 7 || length > data.Length - offset)
                {
                    throw new InvalidDataException("truncated ADTS frame");
                }
                byte[] raw = new byte[length - 7];
                Buffer.BlockCopy(data, offset + 7, raw, 0, raw.Length);
                frames.Add(raw);
                offset += length;
            }
            if (expectedFrameCount.HasValue && frames.Count != expectedFrameCount.Value)
            {
                throw new InvalidDataException(
                    "ADTS frame count must be " + expectedFrameCount.Value);
            }
            return frames;
        }

        private static void AssertWrongThread(Action action)
        {
            Exception error = CaptureOnMta(action);
            Assert.IsNotNull(error);
            Assert.AreEqual(typeof(InvalidOperationException), error.GetType());
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
