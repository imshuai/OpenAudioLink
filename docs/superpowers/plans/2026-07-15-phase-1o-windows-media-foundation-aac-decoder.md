# Phase 1-O Windows Media Foundation AAC Decoder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove that Windows Media Foundation decodes the canonical Version 1 raw AAC-LC stream into exact-format PCM in x86 and x64 CI processes, without connecting it to the fake receiver runtime.

**Architecture:** Extend the existing generated AAC fixture with six consecutive ADTS frames, while keeping production input raw and preserving the existing one-frame bytes. Add one standalone `MediaFoundationAacDecoder` backed directly by `CLSID_CMSAACDecMFT` and a narrow handwritten COM/P/Invoke boundary; exercise it from one MSTest file on a dedicated MTA thread. Keep `FakeAacDecoder`, network/session/runtime/renderer code, Android, protocol bytes and playback untouched.

**Tech Stack:** Python 3 standard library and generation-only FFmpeg, C#/.NET Framework 4.8, Windows Media Foundation and COM, MSTest, GitHub Actions `windows-2022`, Markdown.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-15-phase-1o-windows-media-foundation-aac-decoder-design.md`.

In scope:

- Generated ADTS source frames `2..7`, exact length `1214`, SHA-256 `587a29c59fa6fb508090f75404643e8b567fc4c621b0df6033ed7c46841e38ac`.
- Standalone `MediaFoundationAacDecoder` with direct `IMFTransform`, fixed AAC/PCM media types, buffering, drain, faults and same-thread disposal.
- Decode of six raw access units to exactly `24576` PCM bytes with non-zero energy in both channels.
- Verified x86 and x64 Windows testhost lanes and focused documentation corrections.

Out of scope:

- Changes to fake/runtime/network/renderer/UI code, Android, protocol or golden packets.
- `IAudioDecoder`, factories, DI, production worker threads, playback, queueing, jitter/reconnect policy or malformed-frame recovery.
- Third-party codec/wrapper dependencies, continuous raw data, PCM goldens, minimum-OS certification or performance gates.

---

## Files And Responsibilities

Create:

- `testdata/audio/aac-lc-48k-stereo-6frames.adts` — canonical ADTS frames `2..7`.
- `receiver-windows/src/OpenAudioLink/Receiver/MediaFoundationInterop.cs` — narrow native ABI.
- `receiver-windows/src/OpenAudioLink/Receiver/MediaFoundationAacDecoder.cs` — standalone decoder and lifetime.
- `receiver-windows/tests/OpenAudioLink.Tests/Receiver/MediaFoundationAacDecoderTests.cs` — MTA/state/native decode tests and test-only ADTS stripping.

Modify:

- `tools/audio/test_generate_aac_fixture.py`, `tools/audio/test_validate_aac_fixture.py`.
- `tools/audio/generate_aac_fixture.py`, `tools/audio/validate_aac_fixture.py`.
- `testdata/audio/fixture-manifest.json`, `testdata/audio/README.md`.
- `.github/workflows/windows.yml`.
- `docs/05-Windows.md`, `docs/06-Audio.md`, `docs/10-Testing.md`.

Do not modify either `.csproj`: SDK-style projects include new `.cs` files automatically and no package/reference is needed.

---

### Task 1: Specify the six-frame fixture extension

**Files:**
- Modify: `tools/audio/test_generate_aac_fixture.py`
- Modify: `tools/audio/test_validate_aac_fixture.py`

- [ ] **Step 1: Add RED source-range tests**

Change the generator-test import and add:

```python
from generate_aac_fixture import select_continuous_frames, split_adts_frames

def test_select_continuous_frames_returns_source_indices_2_through_7(self) -> None:
    frames = [bytes([index]) for index in range(8)]
    self.assertEqual(
        [bytes([index]) for index in range(2, 8)],
        select_continuous_frames(frames),
    )

def test_select_continuous_frames_rejects_fewer_than_eight_source_frames(self) -> None:
    with self.assertRaisesRegex(ValueError, "2..7"):
        select_continuous_frames([b"x"] * 7)
```

- [ ] **Step 2: Extend in-memory fixture and manifest helpers**

Import `CONTINUOUS_ADTS_NAME`, `split_adts_frames`, and `validate_continuous_adts`. Add:

```python
CONTINUOUS = "aac-lc-48k-stereo-6frames.adts"

def continuous_adts() -> bytes:
    return b"".join([adts()] + [adts(bytes([index])) for index in range(1, 6)])

def files() -> dict[str, bytes]:
    return {
        ADTS: adts(),
        RAW_NAME: RAW,
        ASC: asc(),
        CONTINUOUS: continuous_adts(),
    }
```

Add `"selectedFrameCount": 6` beside `selectedFrameIndex` in `manifest()`.

- [ ] **Step 3: Add exact continuous-file mutations**

Add:

```python
def test_continuous_adts_valid_values_pass(self) -> None:
    data = files()
    frames = split_adts_frames(data[CONTINUOUS])
    self.assertEqual(6, len(frames))
    self.assertEqual(data[ADTS], frames[0])
    validate_continuous_adts(data[CONTINUOUS], data[ADTS])

def test_continuous_adts_mutations_are_independently_rejected(self) -> None:
    data = files()
    frames = split_adts_frames(data[CONTINUOUS])
    malformed = list(frames)
    malformed[2] = b"\x00" + malformed[2][1:]
    cases = [
        ("count", b"".join(frames[:5]), "frame count"),
        ("middle", b"".join(malformed), "ADTS sync"),
        ("truncated", data[CONTINUOUS][:-1], "truncated ADTS frame"),
        ("trailing", data[CONTINUOUS] + b"\x00", "truncated ADTS header"),
    ]
    for name, value, message in cases:
        with self.subTest(name=name):
            self.rejected(message, lambda v=value: validate_continuous_adts(v, data[ADTS]))
```

Also add independent manifest mutations for wrong `selectedFrameCount`, stale continuous-file length/hash, and boolean/float frame counts. Keep `type(value) is int` expectations.

Use these exact mutation cases in the existing lists:

```python
(
    "count",
    "selected frame count",
    changed(lambda value: value["generator"].update(selectedFrameCount=5)),
),
(
    "continuous length",
    "length mismatch",
    changed(lambda value: value["files"][CONTINUOUS].update(length=1)),
),
(
    "continuous hash",
    "SHA-256 mismatch",
    changed(lambda value: value["files"][CONTINUOUS].update(sha256="0" * 64)),
),
```

```python
(
    "selected count bool",
    lambda value: value["generator"].update(selectedFrameCount=True),
    "selected frame count",
),
(
    "selected count float",
    lambda value: value["generator"].update(selectedFrameCount=6.0),
    "selected frame count",
),
```

- [ ] **Step 4: Verify RED and commit**

```bash
python3 -m unittest discover -s tools/audio -p 'test_*.py'
```

Expected: imports fail because the new symbols do not exist.

```bash
git add tools/audio/test_generate_aac_fixture.py tools/audio/test_validate_aac_fixture.py
git commit -m "test: specify continuous aac fixture"
```

---

### Task 2: Generate and validate the continuous fixture

**Files:**
- Modify: `tools/audio/validate_aac_fixture.py`
- Modify: `tools/audio/generate_aac_fixture.py`
- Create: `testdata/audio/aac-lc-48k-stereo-6frames.adts`
- Modify: `testdata/audio/fixture-manifest.json`
- Modify: `testdata/audio/README.md`

- [ ] **Step 1: Share one boundary splitter**

In `validate_aac_fixture.py`, add the new name to `BINARY_NAMES`, then add:

```python
CONTINUOUS_ADTS_NAME = "aac-lc-48k-stereo-6frames.adts"

def split_adts_frames(data: bytes) -> list[bytes]:
    frames: list[bytes] = []
    offset = 0
    while offset < len(data):
        require(len(data) - offset >= 7, "truncated ADTS header")
        require(
            data[offset] == 0xFF and data[offset + 1] & 0xF0 == 0xF0,
            "invalid ADTS sync",
        )
        require(data[offset + 1] & 1 == 1, "CRC-bearing ADTS frame")
        length = (
            ((data[offset + 3] & 3) << 11)
            | (data[offset + 4] << 3)
            | ((data[offset + 5] >> 5) & 7)
        )
        require(length >= 7, "invalid ADTS frame length")
        require(offset + length <= len(data), "truncated ADTS frame")
        frames.append(data[offset : offset + length])
        offset += length
    return frames

def validate_continuous_adts(data: bytes, first_frame: bytes) -> None:
    frames = split_adts_frames(data)
    require(len(frames) == 6, "continuous ADTS frame count must be 6")
    require(frames[0] == first_frame, "first continuous ADTS frame mismatch")
    for frame in frames:
        validate_adts(frame, frame[7:])
```

Require integer `selectedFrameCount == 6`, read the fourth binary, and call `validate_continuous_adts` from `validate_fixture`.

- [ ] **Step 2: Generate exactly indices 2 through 7**

Import the shared splitter/name in `generate_aac_fixture.py`, delete its local splitter, and add:

```python
SELECTED_FRAME_COUNT = 6

def select_continuous_frames(frames: list[bytes]) -> list[bytes]:
    end = SELECTED_FRAME_INDEX + SELECTED_FRAME_COUNT
    if len(frames) < end:
        raise ValueError("FFmpeg did not produce source frames 2..7")
    return frames[SELECTED_FRAME_INDEX:end]
```

In `main()` use:

```python
selected = select_continuous_frames(split_adts_frames(encoded))
adts = selected[0]
raw = adts[7:]
continuous = b"".join(selected)
files = {
    ADTS_NAME: adts,
    RAW_NAME: raw,
    ASC_NAME: ASC,
    CONTINUOUS_ADTS_NAME: continuous,
}
```

Add `selectedFrameCount` to the manifest and change README generation to `Selected zero-based ADTS frame range: 2..7 (6 frames)`.

- [ ] **Step 3: Generate and verify exact bytes**

```bash
ffmpeg -version | sed -n '1p'
python3 tools/audio/generate_aac_fixture.py
test "$(wc -c < testdata/audio/aac-lc-48k-stereo-6frames.adts)" = 1214
test "$(sha256sum testdata/audio/aac-lc-48k-stereo-6frames.adts | cut -d' ' -f1)" = \
  587a29c59fa6fb508090f75404643e8b567fc4c621b0df6033ed7c46841e38ac
test "$(sha256sum testdata/audio/aac-lc-48k-stereo-1024.adts | cut -d' ' -f1)" = \
  9b122450a3e73c2a0b2aec45b4439d7b7a3ddd4fc942cce599f5cd9b36a9260c
test "$(sha256sum testdata/audio/aac-lc-48k-stereo-1024.raw | cut -d' ' -f1)" = \
  a81a4ab313901a56dbe128fbd6004fdaad16fde6bd1e58076c39b545f9e6811e
test "$(sha256sum testdata/audio/aac-lc-48k-stereo.asc | cut -d' ' -f1)" = \
  b65f22439176f7827a0d82a66b34477888f0287450985d8e3dc836d7e32ce79b
```

- [ ] **Step 4: Verify GREEN and commit**

```bash
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check
git add tools/audio/generate_aac_fixture.py tools/audio/validate_aac_fixture.py \
  testdata/audio/aac-lc-48k-stereo-6frames.adts \
  testdata/audio/fixture-manifest.json testdata/audio/README.md
git commit -m "test: add continuous aac fixture"
```

Expected: all checks pass and the three Phase 1-N binary hashes remain unchanged.

---

### Task 3: Specify the standalone decoder contract

**Files:**
- Create: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/MediaFoundationAacDecoderTests.cs`

- [ ] **Step 1: Add one complete RED MSTest file**

Create `receiver-windows/tests/OpenAudioLink.Tests/Receiver/MediaFoundationAacDecoderTests.cs`:

```csharp
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
```

The test splitter intentionally handles only the canonical no-CRC boundaries. Python owns full ADTS semantics; production never parses ADTS.

- [ ] **Step 2: Commit and push RED**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/MediaFoundationAacDecoderTests.cs
git commit -m "test: specify media foundation aac decoder"
set -e
ip route add 192.168.3.20/32 via 192.168.4.1 dev eth0 mtu 1200 || true
trap 'ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true' EXIT
git push -u origin phase-1o-windows-mf-aac-decoder
```

- [ ] **Step 3: Prove RED is the missing production class**

After the GitHub mirror reaches local `HEAD`, obtain the exact-head Windows run through:

```bash
HEAD_SHA=$(git rev-parse HEAD)
gh api \
  'repos/imshuai/OpenAudioLink/actions/runs?branch=phase-1o-windows-mf-aac-decoder&per_page=30' \
  --jq ".workflow_runs[] | select(.head_sha == \"$HEAD_SHA\" and .name == \"windows\") | [.id,.status,.conclusion] | @tsv"
```

Wait for completion, then run `gh run view <run-id> --repo imshuai/OpenAudioLink --log-failed`. Expected: `CS0246` for `MediaFoundationAacDecoder`. Fixture lookup, syntax, restore or unrelated failures do not satisfy RED.

---

### Task 4: Implement the direct Media Foundation decoder

**Files:**
- Create: `receiver-windows/src/OpenAudioLink/Receiver/MediaFoundationInterop.cs`
- Create: `receiver-windows/src/OpenAudioLink/Receiver/MediaFoundationAacDecoder.cs`
- Test: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/MediaFoundationAacDecoderTests.cs`

- [ ] **Step 1: Declare the exact native constants and entry points**

Create `MediaFoundationInterop.cs` under namespace `OpenAudioLink.Receiver`. The constants must be:

```csharp
internal const uint CoinitMultithreaded = 0;
internal const uint ClsctxInprocServer = 1;
internal const uint MfVersion = 0x00020070;
internal const uint MfStartupFull = 0;
internal const int ENotImpl = unchecked((int)0x80004001);
internal const int RegdbEClassNotReg = unchecked((int)0x80040154);
internal const int MfEAttributeNotFound = unchecked((int)0xC00D36E6);
internal const int MfENotAccepting = unchecked((int)0xC00D36B5);
internal const int MfETransformStreamChange = unchecked((int)0xC00D6D61);
internal const int MfETransformNeedMoreInput = unchecked((int)0xC00D6D72);
internal const uint MftOutputStreamProvidesSamples = 0x100;
internal const uint MftOutputStreamCanProvideSamples = 0x200;
internal const uint MftMessageCommandDrain = 0x00000001;
internal const uint MftMessageNotifyBeginStreaming = 0x10000000;
internal const uint MftMessageNotifyEndStreaming = 0x10000001;
internal const uint MftMessageNotifyEndOfStream = 0x10000002;
internal const uint MftMessageNotifyStartOfStream = 0x10000003;
```

Use these exact GUID strings:

```text
CLSID_CMSAACDecMFT                         32D186A7-218F-4C75-8876-DD77273A8999
IID_IMFTransform                          BF94C121-5B05-4E6F-8000-BA598961414D
MF_TRANSFORM_ASYNC                        F81A699A-649A-497D-8C73-29F8FED6AD7A
MFMediaType_Audio                         73647561-0000-0010-8000-00AA00389B71
MFAudioFormat_AAC                         00001610-0000-0010-8000-00AA00389B71
MFAudioFormat_PCM                         00000001-0000-0010-8000-00AA00389B71
MF_MT_MAJOR_TYPE                          48EBA18E-F8C9-4687-BF11-0A74C9F96A8F
MF_MT_SUBTYPE                             F7E34C9A-42E8-4714-B74B-CB29D72C35E5
MF_MT_AUDIO_NUM_CHANNELS                  37E48BF5-645E-4C5B-89DE-ADA9E29B696A
MF_MT_AUDIO_SAMPLES_PER_SECOND            5FAEEAE7-0290-4C31-9E8A-C534F68D9DBA
MF_MT_AUDIO_AVG_BYTES_PER_SECOND          1AAB75C8-CFEF-451C-AB95-AC034B8E1731
MF_MT_AUDIO_BLOCK_ALIGNMENT               322DE230-9EEB-43BD-AB7A-FF412251541D
MF_MT_AUDIO_BITS_PER_SAMPLE               F2DEB57F-40FA-4764-AA33-ED4F2D1FF669
MF_MT_AAC_PAYLOAD_TYPE                    BFBABE79-7434-4D1C-94F0-72A3B9E17188
MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION  7632F0E6-9538-4D61-ACDA-EA29C8C14456
MF_MT_USER_DATA                           B6BC765F-4C3B-40A4-BD51-2535B66FE09D
```

Declare `[PreserveSig] int` P/Invokes for:

```csharp
CoInitializeEx(IntPtr, uint)                         // ole32.dll
CoCreateInstance(ref Guid, IntPtr, uint, ref Guid,
                 out IMFTransform)                  // ole32.dll
MFStartup(uint, uint)                                // mfplat.dll
MFShutdown()                                         // mfplat.dll
MFCreateMediaType(out IMFMediaType)                  // mfplat.dll
MFCreateSample(out IMFSample)                        // mfplat.dll
MFCreateMemoryBuffer(uint, out IMFMediaBuffer)       // mfplat.dll
MFCreateAlignedMemoryBuffer(uint, uint,
                            out IMFMediaBuffer)      // mfplat.dll
```

Also declare `void CoUninitialize()`.

- [ ] **Step 2: Preserve the required COM ABI slots**

Add `[ComImport]`, `InterfaceIsIUnknown` interfaces with these SDK IIDs:

```text
IMFAttributes   2CD2D921-C447-44A7-A13C-4ADABFC247E3
IMFMediaType    44AE0FA8-EA31-4109-8D2E-4CAE4997C555
IMFMediaBuffer  045FA593-8799-42B8-BC8D-8968C6453507
IMFSample       C40A00F2-B93A-4D80-AE8C-5A1C634F58E4
IMFTransform    BF94C121-5B05-4E6F-8000-BA598961414D
```

Flat declarations must preserve every slot before a called method. Use parameterless `[PreserveSig] int SlotNN...()` only for never-called slots. Called signatures and positions are:

```text
IMFAttributes: slots 0..3 unused; slot 4 GetUINT32(ref Guid, out uint)

IMFMediaType:
  slots 0..3 unused
  slot 4  GetUINT32(ref Guid, out uint)
  slots 5..6 unused
  slot 7  GetGUID(ref Guid, out Guid)
  slots 8..17 unused
  slot 18 SetUINT32(ref Guid, uint)
  slots 19..20 unused
  slot 21 SetGUID(ref Guid, ref Guid)
  slot 22 unused
  slot 23 SetBlob(ref Guid, byte[], uint)

IMFSample:
  inherited IMFAttributes slots 0..29 unused but present
  sample slots 0..2 unused
  sample slot 3 SetSampleTime(long)
  sample slot 4 unused
  sample slot 5 SetSampleDuration(long)
  sample slots 6..7 unused
  sample slot 8 ConvertToContiguousBuffer(out IMFMediaBuffer)
  sample slot 9 AddBuffer(IMFMediaBuffer)

IMFMediaBuffer:
  Lock(out IntPtr, out uint, out uint)
  Unlock()
  GetCurrentLength(out uint)
  SetCurrentLength(uint)
  GetMaxLength(out uint)

IMFTransform slots 0..22:
  0 GetStreamLimits (unused)
  1 GetStreamCount(out uint, out uint)
  2 GetStreamIDs(uint, uint[], uint, uint[])
  3 GetInputStreamInfo (unused)
  4 GetOutputStreamInfo(uint, out MftOutputStreamInfo)
  5 GetAttributes(out IMFAttributes)
  6..11 unused
  12 SetInputType(uint, IMFMediaType, uint)
  13 SetOutputType(uint, IMFMediaType, uint)
  14 unused
  15 GetOutputCurrentType(uint, out IMFMediaType)
  16..19 unused
  20 ProcessMessage(uint, UIntPtr)
  21 ProcessInput(uint, IMFSample, uint)
  22 ProcessOutput(uint, uint, MftOutputDataBuffer[], out uint)
```

Annotate the `ProcessOutput` array with `[In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]`; annotate `MftOutputDataBuffer.Sample` as `UnmanagedType.Interface`.

Use sequential structures:

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct MftOutputStreamInfo
{
    internal uint Flags;
    internal uint Size;
    internal uint Alignment;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MftOutputDataBuffer
{
    internal uint StreamId;
    [MarshalAs(UnmanagedType.Interface)] internal IMFSample Sample;
    internal uint Status;
    internal IntPtr Events;
}
```

Do not add `PROPVARIANT`, Source Reader, MFT enumeration, async-event interfaces or unused trailing methods.

- [ ] **Step 3: Implement construction and exact type negotiation**

Create `MediaFoundationAacDecoder.cs` with fields `ownerThreadId`, `transform`, `comInitialized`, `mediaFoundationStarted`, `state`, `nextInputIndex`, and the three output-stream-info values. Its private state is exactly `Active`, `Drained`, `Faulted`, `Disposed`.

The constructor order is:

```text
Environment.CurrentManagedThreadId
CoInitializeEx(COINIT_MULTITHREADED); S_OK and S_FALSE both set comInitialized
MFStartup(MF_VERSION, MFSTARTUP_FULL)
CoCreateInstance(CLSID_CMSAACDecMFT, CLSCTX_INPROC_SERVER, IID_IMFTransform)
GetStreamCount == 1/1
GetStreamIDs == E_NOTIMPL, or returned IDs == 0/0
GetAttributes; E_NOTIMPL/missing/zero MF_TRANSFORM_ASYNC is synchronous;
               value 1 is PlatformNotSupportedException
SetInputType
SetOutputType
GetOutputCurrentType and verify all output attributes
GetOutputStreamInfo; when the MFT does not provide samples, require non-zero
caller size and zero-or-power-of-two alignment
MFT_MESSAGE_NOTIFY_BEGIN_STREAMING
MFT_MESSAGE_NOTIFY_START_OF_STREAM
state = Active
```

Input attributes:

```text
major audio; subtype AAC; 48000 Hz; 2 channels; payload 0; profile 0xFE
MF_MT_USER_DATA = 00 00 FE 00 00 00 00 00 00 00 00 00 11 90
```

Output attributes and read-back requirements:

```text
major audio; subtype PCM; 48000 Hz; 2 channels; 16 bits;
block alignment 4; average bytes/second 192000
```

All unexpected HRESULTs use one helper:

```csharp
private static void CheckHr(string operation, int hr)
{
    if (hr < 0)
    {
        throw new InvalidOperationException(
            operation + " failed with HRESULT 0x" + ((uint)hr).ToString("X8") + ".",
            Marshal.GetExceptionForHR(hr));
    }
}
```

Only missing DLL/entry point, `REGDB_E_CLASSNOTREG`, or async MFT map to `PlatformNotSupportedException`.

- [ ] **Step 4: Implement `Submit` and rational timestamps**

Use:

```csharp
private static long SampleTime(long frameIndex)
{
    checked
    {
        return ((frameIndex * 640000L) + 1L) / 3L;
    }
}
```

Validation order is owner thread, disposed, faulted/drained, then null/empty. A valid call creates one sample/buffer, copies under `Lock` with `Unlock` in `finally`, sets current length, adds the buffer, sets `SampleTime(n)` and duration `SampleTime(n + 1) - SampleTime(n)`, and calls `ProcessInput`.

On `MF_E_NOTACCEPTING`, collect output and retry the same sample/index. Fault after two rejection/output cycles with no successful output. Increment the index only after `S_OK`. After acceptance, collect all output until `MF_E_TRANSFORM_NEED_MORE_INPUT`.

- [ ] **Step 5: Implement output allocation and ownership**

Allocation is exact:

```text
PROVIDES_SAMPLES -> pass null and release MFT sample
otherwise -> caller sample; require cbSize > 0
alignment 0 -> MFCreateMemoryBuffer(cbSize)
alignment power-of-two -> MFCreateAlignedMemoryBuffer(cbSize, alignment - 1)
CAN_PROVIDE_SAMPLES still uses caller sample
```

For every `S_OK`, require a sample, convert to contiguous buffer, lock, require non-zero current length divisible by `4`, copy into a new `byte[]`, and unlock in `finally`. Release once per acquired sample/buffer/contiguous buffer and non-null events pointer. `MF_E_TRANSFORM_STREAM_CHANGE` is fatal; no renegotiation.

- [ ] **Step 6: Implement drain, faults and cleanup**

First drain sends `NOTIFY_END_OF_STREAM(0)`, `COMMAND_DRAIN(0)`, reads until `NEED_MORE_INPUT`, sends `NOTIFY_END_STREAMING(0)`, then marks `Drained`. Second drain returns `Array.Empty<byte[]>()`; submit-after-drain fails.

Native/output/drain failures mark `Faulted`; only owner-thread dispose remains. Dispose is owner-thread, idempotent, never drains, releases the transform, calls one `MFShutdown` per successful startup and one `CoUninitialize` per successful COM initialization including `S_FALSE`. Cleanup attempts every later stage even after an earlier cleanup error. Use `Marshal.ReleaseComObject`, never `FinalReleaseComObject`; no finalizer.

- [ ] **Step 7: Verify locally, commit, and push GREEN candidate**

```bash
python3 tools/check_docs_consistency.py
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check
git add receiver-windows/src/OpenAudioLink/Receiver/MediaFoundationInterop.cs \
  receiver-windows/src/OpenAudioLink/Receiver/MediaFoundationAacDecoder.cs
git commit -m "feat: decode aac with media foundation"
```

Push to the phase branch and require the exact-head Windows run to compile and pass on the current default lane. If it fails, invoke `superpowers:systematic-debugging`, inspect the exact HRESULT/log, and change one ABI/state variable at a time. Never skip or weaken native tests.

---

### Task 5: Prove both COM architectures in Windows CI

**Files:**
- Modify: `.github/workflows/windows.yml`
- Test: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/MediaFoundationAacDecoderTests.cs`

- [ ] **Step 1: Replace the single floating runner with a fixed matrix**

Change `.github/workflows/windows.yml` to:

```yaml
name: windows

on:
  pull_request:
  push:
    branches: ['phase-*']

jobs:
  test:
    runs-on: windows-2022
    strategy:
      fail-fast: false
      matrix:
        architecture: [x86, x64]
    steps:
      - uses: actions/checkout@v4
      - uses: microsoft/setup-msbuild@v2
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Test (${{ matrix.architecture }})
        env:
          OAL_TEST_ARCHITECTURE: ${{ matrix.architecture }}
        run: dotnet test receiver-windows/OpenAudioLink.sln -c Release -- RunConfiguration.TargetPlatform=${{ matrix.architecture }}
```

Do not add a `main` push trigger; phase pushes and pull requests remain the only triggers.

- [ ] **Step 2: Commit and push**

```bash
git add .github/workflows/windows.yml
git commit -m "ci: test media foundation on x86 and x64"
```

Push with the known MTU workaround. The architecture environment assertion must prove the testhost is truly 32-bit/64-bit; two green matrix labels alone are insufficient.

- [ ] **Step 3: Verify both matrix jobs on exact HEAD**

Use REST because the installed `gh` does not support `gh run list --branch`:

```bash
HEAD_SHA=$(git rev-parse HEAD)
RUN_ID=$(gh api \
  'repos/imshuai/OpenAudioLink/actions/runs?branch=phase-1o-windows-mf-aac-decoder&per_page=30' \
  --jq ".workflow_runs[] | select(.head_sha == \"$HEAD_SHA\" and .name == \"windows\") | .id" \
  | head -n 1)
test -n "$RUN_ID"
gh run watch "$RUN_ID" --repo imshuai/OpenAudioLink --exit-status
gh api "repos/imshuai/OpenAudioLink/actions/runs/$RUN_ID/jobs?per_page=20" \
  --jq '.jobs[] | [.name,.status,.conclusion] | @tsv'
```

Expected: `test (x86)` and `test (x64)` both complete with `success`, and the native decode test passes in both.

---

### Task 6: Align active Windows, audio and testing documentation

**Files:**
- Modify: `docs/05-Windows.md:1223-1338`
- Modify: `docs/06-Audio.md:552-749`
- Modify: `docs/06-Audio.md:1075-1085`
- Modify: `docs/10-Testing.md:1078-1104`

- [ ] **Step 1: Correct the Windows decoder status**

In `docs/05-Windows.md`:

- Remove `Hardware acceleration when available`; Microsoft documents the built-in decoder, not guaranteed hardware acceleration.
- Replace the stale “future native decoder” wording with:

```text
Phase 1-O proves this media type and native decode path in a standalone
MediaFoundationAacDecoder on Windows CI. ReceiverRuntime still uses
FakeAacDecoder; session ownership, worker-thread integration and playback
remain later phases.
```

- Clarify that `192 kbps` is canonical fixture/Version 1 stream metadata, not ADTS framing or an extra MFT requirement.
- Replace “Decoded PCM is immediately forwarded to the playback queue” with:

```text
The standalone proof returns every currently available PCM chunk and Drain
returns delayed output. A later runtime-integration phase will forward PCM to
the playback queue.
```

- [ ] **Step 2: Correct the audio architecture**

In `docs/06-Audio.md`:

- Mark the documented `IAudioDecoder`/factory tree as future runtime integration, not Phase 1-O code.
- Replace the Source Reader diagram with:

```text
Raw AAC Access Unit

↓

IMFSample / IMFMediaBuffer

↓

AAC Decoder IMFTransform

↓

PCM Media Buffer
```

- Remove the `audio/aac` MIME requirement from direct-MFT setup; retain exact subtype/attributes.
- State that one submit may return zero, one or many chunks and end-of-stream requires drain.
- Replace `Valid AAC headers` in decoder security with `Bounded complete raw AAC access units`; production packets contain no ADTS header.

- [ ] **Step 3: Record test data and native gates**

In `docs/10-Testing.md`, add `aac-lc-48k-stereo-6frames.adts` to the fixture tree and replace “proved in the next phase” with:

```text
The single-frame files prove wire framing and provenance. The six-frame ADTS
file is test-only provenance: Windows tests remove each ADTS header and submit
six raw access units to MediaFoundationAacDecoder. Submit may buffer; Drain is
required. The native oracle is exactly 24576 bytes of 48 kHz stereo PCM with
non-zero energy in both channels, not a PCM hash.

GitHub Windows CI runs the same native test in verified x86 and x64 testhost
processes on windows-2022. This proves standalone decode and COM ABI only;
ReceiverRuntime and audible playback remain unimplemented.
```

- [ ] **Step 4: Run focused stale-language checks**

```bash
python3 - <<'PY'
from pathlib import Path

windows = Path('docs/05-Windows.md').read_text()
audio = Path('docs/06-Audio.md').read_text()
testing = Path('docs/10-Testing.md').read_text()
checks = {
    'hardware decoder claim': 'Hardware acceleration when available' not in windows,
    'future decoder wording': 'this phase does not implement\nthe native decoder' not in windows,
    'source reader pipeline': 'IMFSourceReader' not in audio,
    'raw header wording': 'Valid AAC headers' not in audio,
    'continuous fixture': 'aac-lc-48k-stereo-6frames.adts' in testing,
    'exact pcm oracle': '24576' in testing,
    'standalone boundary': 'ReceiverRuntime' in testing and 'standalone' in testing,
}
failed = [name for name, ok in checks.items() if not ok]
if failed:
    raise SystemExit('stale Phase 1-O documentation: ' + ', '.join(failed))
print('focused Phase 1-O documentation ok')
PY
python3 tools/check_docs_consistency.py
git diff --check
```

Expected: focused and global documentation checks pass.

- [ ] **Step 5: Commit docs**

```bash
git add docs/05-Windows.md docs/06-Audio.md docs/10-Testing.md
git commit -m "docs: record standalone media foundation decode"
```

---

### Task 7: Verify, review and integrate Phase 1-O

**Files:**
- Verify: every Phase 1-O file
- No new implementation files

- [ ] **Step 1: Run complete local verification**

```bash
python3 tools/check_docs_consistency.py
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
git status --short --branch
```

Run Android baseline with the existing ARM64 `aapt2` override:

```bash
ANDROID_HOME=/root/Android/Sdk \
ANDROID_SDK_ROOT=/root/Android/Sdk \
./sender-android/gradlew \
  -p sender-android \
  -Pandroid.aapt2FromMavenOverride=/root/.cache/openaudiolink/android-sdk-tools-35.0.2-aarch64/bin/aapt2 \
  :app:testDebugUnitTest
```

Expected: docs/audio/goldens and Android pass. Windows compilation/native behavior remains CI-only because this host has no `dotnet` and is not Windows.

- [ ] **Step 2: Audit the exact phase diff**

```bash
BASE_SHA=5664fe9a4fc1d2aea9ffaad0c603a09ffbaf9f90
git diff --stat "$BASE_SHA"..HEAD
git diff --check "$BASE_SHA"..HEAD
git log --oneline --reverse "$BASE_SHA"..HEAD
```

Confirm the diff contains only:

```text
spec + plan
continuous fixture/tooling
two production decoder/interop files
one Windows decoder test file
Windows matrix workflow
three focused docs
```

Any fake/runtime/network/renderer/UI, Android, protocol or golden change is scope drift and must be removed.

- [ ] **Step 3: Push and verify source/mirror tips**

```bash
set -e
ip route add 192.168.3.20/32 via 192.168.4.1 dev eth0 mtu 1200 || true
trap 'ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true' EXIT
git push origin phase-1o-windows-mf-aac-decoder
HEAD_SHA=$(git rev-parse HEAD)
GITEA_SHA=$(git ls-remote origin refs/heads/phase-1o-windows-mf-aac-decoder | cut -f1)
test "$GITEA_SHA" = "$HEAD_SHA"

for attempt in $(seq 1 60); do
  GITHUB_SHA=$(gh api \
    repos/imshuai/OpenAudioLink/commits/phase-1o-windows-mf-aac-decoder \
    --jq .sha 2>/dev/null || true)
  [ "$GITHUB_SHA" = "$HEAD_SHA" ] && break
  sleep 5
done
test "$GITHUB_SHA" = "$HEAD_SHA"
```

- [ ] **Step 4: Require exact-head docs, Android and matrix Windows success**

```bash
python3 - <<'PY'
import json
import subprocess
import time

repo = 'imshuai/OpenAudioLink'
branch = 'phase-1o-windows-mf-aac-decoder'
head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
required = {'docs', 'android', 'windows'}
deadline = time.monotonic() + 1800

while True:
    data = json.loads(subprocess.check_output([
        'gh', 'api',
        f'repos/{repo}/actions/runs?branch={branch}&per_page=30',
    ], text=True))
    current = [
        run for run in data['workflow_runs']
        if run['head_sha'] == head and run['name'] in required
    ]
    failed = [
        run for run in current
        if run['status'] == 'completed' and run['conclusion'] != 'success'
    ]
    if failed:
        raise SystemExit(f'CI failed for {head}: {failed}')
    if len(current) > len(required):
        raise SystemExit(f'duplicate CI runs for {head}: {current}')
    if len(current) == 3 and {run['name'] for run in current} == required and all(
        run['status'] == 'completed' and run['conclusion'] == 'success'
        for run in current
    ):
        print(f'CI green for {head}: docs, android, windows')
        break
    if time.monotonic() >= deadline:
        raise SystemExit(f'timed out waiting for CI at {head}: {current}')
    time.sleep(15)
PY
```

Then inspect the Windows run jobs and require both `test (x86)` and `test (x64)` success.

- [ ] **Step 5: Request final two-stage review**

Use `superpowers:requesting-code-review` with:

```bash
BASE_SHA=5664fe9a4fc1d2aea9ffaad0c603a09ffbaf9f90
HEAD_SHA=$(git rev-parse HEAD)
REQUIREMENTS=docs/superpowers/specs/2026-07-15-phase-1o-windows-media-foundation-aac-decoder-design.md
PLAN=docs/superpowers/plans/2026-07-15-phase-1o-windows-media-foundation-aac-decoder.md
```

Fix every Critical/Important finding, rerun all checks, repush and reverify the corrected exact HEAD. Then invoke `superpowers:finishing-a-development-branch`; fast-forward `main` only after branch CI is green, push `main` to Gitea, confirm the GitHub mirror, and verify that `main` push did not trigger duplicate CI.

---

## Execution Order And Review Gates

For every task:

1. Follow RED → minimal GREEN → refactor only if needed.
2. Commit only the task's files.
3. Run a spec-compliance review against the Phase 1-O design.
4. Run a code-quality review focused on correctness, COM ownership and unnecessary abstraction.
5. Fix all Critical/Important findings before starting the next task.

Windows-native truth comes from exact-head GitHub Actions, not local Linux inference. The intentional RED commit is the only expected failed Windows HEAD; every later checkpoint must return to green before advancing.
