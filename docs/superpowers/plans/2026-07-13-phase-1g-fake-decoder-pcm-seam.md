# Phase 1-G Windows Fake Decoder PCM Seam Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Insert a minimal Windows fake decoder boundary so queued encoded `AUDIO` payloads become `FakePcmFrame` values before fake rendering.

**Architecture:** Keep `TcpReceiver`, `AudioFrameQueue`, and protocol wire format unchanged. Add `FakePcmFrame` and `FakeAacDecoder` under `OpenAudioLink.Receiver`; the decoder validates and parses existing AAC `AUDIO` payloads, then wraps encoded bytes as deterministic fake PCM bytes. Update `FakeAudioRenderer` to drain through `FakeAacDecoder` and store `FakePcmFrame` history.

**Tech Stack:** C#/.NET Framework 4.8 MSTest, existing `OpenAudioLink.Protocol` helpers, existing receiver queue/TCP tests, Python docs/golden checks.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-13-phase-1g-fake-decoder-pcm-seam-design.md`.

In scope:

- `FakePcmFrame` immutable PCM-shaped test data model.
- `FakeAacDecoder.Decode(byte[] audioPayload)` fake decoder seam.
- `FakeAudioRenderer` renders decoded `FakePcmFrame` values.
- Unit and TCP loopback tests proving encoded payload -> fake PCM frame -> renderer.

Out of scope:

- Real AAC decode.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- Renderer threads, timers, clocks, jitter buffers, async loops, backpressure, or device selection.
- Android sender changes.
- Protocol wire-format changes.
- Discovery, UI, tray, installer, configuration, and new dependencies.

---

## Files and Responsibilities

- Create `receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAacDecoderTests.cs`
  - Tests fake decoder payload parsing, validator reuse, and PCM byte clone isolation.

- Create `receiver-windows/src/OpenAudioLink/Receiver/FakePcmFrame.cs`
  - Stores frame number, capture timestamp, duration, and fake PCM bytes.
  - Clones bytes on construction and return.

- Create `receiver-windows/src/OpenAudioLink/Receiver/FakeAacDecoder.cs`
  - Reuses `AudioPayloadValidator.ValidateAacPayload`.
  - Parses existing `AUDIO` payload header.
  - Returns `FakePcmFrame` with encoded bytes as fake PCM bytes.

- Modify `receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAudioRendererTests.cs`
  - Updates existing renderer tests to require decoder-backed drain and `FakePcmFrame` history.

- Modify `receiver-windows/src/OpenAudioLink/Receiver/FakeAudioRenderer.cs`
  - Stores `FakePcmFrame` values.
  - Adds `Render(FakePcmFrame frame)`.
  - Replaces `Drain(AudioFrameQueue queue)` with `Drain(AudioFrameQueue queue, FakeAacDecoder decoder)`.

- Modify `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`
  - Drains queued TCP audio payloads through `FakeAacDecoder`.
  - Asserts rendered frame metadata and fake PCM bytes.

No `.csproj` edits are needed because the projects are SDK-style and include `.cs` files by default.

---

### Task 1: Add failing fake decoder tests

**Files:**
- Create: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAacDecoderTests.cs`

- [ ] **Step 1: Create decoder tests before implementation**

Create `receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAacDecoderTests.cs`:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;
using OpenAudioLink.Receiver;

namespace OpenAudioLink.Tests.Receiver
{
    [TestClass]
    public sealed class FakeAacDecoderTests
    {
        [TestMethod]
        public void DecodeMapsAudioPayloadToFakePcmFrame()
        {
            byte[] encoded = new byte[] { 0x11, 0x22, 0x33 };
            byte[] payload = HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 7u, 123456789UL, 20, encoded);

            FakePcmFrame frame = new FakeAacDecoder().Decode(payload);

            Assert.AreEqual(7u, frame.FrameNumber);
            Assert.AreEqual(123456789UL, frame.CaptureTimestamp);
            Assert.AreEqual((ushort)20, frame.FrameDuration);
            CollectionAssert.AreEqual(encoded, frame.PcmBytes);
        }

        [TestMethod]
        public void DecodeRejectsUnsupportedCodec()
        {
            byte[] payload = HandshakePayloads.Audio(ProtocolConstants.CodecOpus, 1u, 2UL, 20, new byte[] { 0x01 });

            Assert.ThrowsException<PacketParseException>(() => new FakeAacDecoder().Decode(payload));
        }

        [TestMethod]
        public void DecodeRejectsLengthMismatch()
        {
            byte[] payload = HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 1u, 2UL, 20, new byte[] { 0x01, 0x02 });
            byte[] truncated = new byte[payload.Length - 1];
            System.Buffer.BlockCopy(payload, 0, truncated, 0, truncated.Length);

            Assert.ThrowsException<PacketParseException>(() => new FakeAacDecoder().Decode(truncated));
        }

        [TestMethod]
        public void FakePcmFrameClonesPcmBytes()
        {
            byte[] pcmBytes = new byte[] { 0x31, 0x32 };
            FakePcmFrame frame = new FakePcmFrame(1u, 2UL, 20, pcmBytes);

            pcmBytes[0] = 0x7f;
            CollectionAssert.AreEqual(new byte[] { 0x31, 0x32 }, frame.PcmBytes);

            byte[] returned = frame.PcmBytes;
            returned[0] = 0x7e;

            CollectionAssert.AreEqual(new byte[] { 0x31, 0x32 }, frame.PcmBytes);
        }
    }
}
```

- [ ] **Step 2: Run decoder tests and verify RED**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter FakeAacDecoderTests
```

Expected: FAIL because `FakeAacDecoder` and `FakePcmFrame` do not exist.

On this Linux workspace, `dotnet` may be unavailable; if so, record `dotnet not found` and rely on branch CI.

- [ ] **Step 3: Commit failing decoder tests**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAacDecoderTests.cs
git commit -m "test: cover fake aac decoder pcm frames"
```

---

### Task 2: Implement fake PCM frame and decoder

**Files:**
- Create: `receiver-windows/src/OpenAudioLink/Receiver/FakePcmFrame.cs`
- Create: `receiver-windows/src/OpenAudioLink/Receiver/FakeAacDecoder.cs`

- [ ] **Step 1: Add `FakePcmFrame`**

Create `receiver-windows/src/OpenAudioLink/Receiver/FakePcmFrame.cs`:

```csharp
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
```

- [ ] **Step 2: Add `FakeAacDecoder`**

Create `receiver-windows/src/OpenAudioLink/Receiver/FakeAacDecoder.cs`:

```csharp
using System;
using OpenAudioLink.Protocol;

namespace OpenAudioLink.Receiver
{
    public sealed class FakeAacDecoder
    {
        public FakePcmFrame Decode(byte[] audioPayload)
        {
            AudioPayloadValidator.ValidateAacPayload(audioPayload);

            uint frameNumber = PacketParser.ReadUInt32(audioPayload, 1);
            ulong captureTimestamp = ((ulong)PacketParser.ReadUInt32(audioPayload, 5) << 32) | PacketParser.ReadUInt32(audioPayload, 9);
            ushort frameDuration = ReadUInt16(audioPayload, 13);
            uint encodedSize = PacketParser.ReadUInt32(audioPayload, 15);
            byte[] fakePcmBytes = new byte[encodedSize];
            Buffer.BlockCopy(audioPayload, ProtocolConstants.AudioPayloadHeaderSize, fakePcmBytes, 0, fakePcmBytes.Length);

            return new FakePcmFrame(frameNumber, captureTimestamp, frameDuration, fakePcmBytes);
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        }
    }
}
```

- [ ] **Step 3: Run decoder tests and verify GREEN**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter FakeAacDecoderTests
```

Expected: PASS.

- [ ] **Step 4: Commit decoder implementation**

```bash
git add receiver-windows/src/OpenAudioLink/Receiver/FakePcmFrame.cs receiver-windows/src/OpenAudioLink/Receiver/FakeAacDecoder.cs
git commit -m "feat: add fake aac decoder pcm seam"
```

---

### Task 3: Update renderer tests for decoded PCM frames

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAudioRendererTests.cs`

- [ ] **Step 1: Replace renderer tests with decoder-backed expectations**

Replace `receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAudioRendererTests.cs` with:

```csharp
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
            FakePcmFrame frame = new FakePcmFrame(1u, 100UL, 20, pcmBytes);

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
            return HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, frameNumber, captureTimestamp, 20, encoded);
        }

        private static byte[] Payload(byte first)
        {
            return new byte[] { first, (byte)(first + 1) };
        }

        private static void AssertFrame(FakePcmFrame frame, uint frameNumber, ulong captureTimestamp, byte[] pcmBytes)
        {
            Assert.AreEqual(frameNumber, frame.FrameNumber);
            Assert.AreEqual(captureTimestamp, frame.CaptureTimestamp);
            Assert.AreEqual((ushort)20, frame.FrameDuration);
            CollectionAssert.AreEqual(pcmBytes, frame.PcmBytes);
        }
    }
}
```

- [ ] **Step 2: Run renderer tests and verify RED**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter FakeAudioRendererTests
```

Expected: FAIL because `FakeAudioRenderer` still exposes raw-byte `Drain(queue)` and `RenderedFrames`.

- [ ] **Step 3: Commit renderer test update**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAudioRendererTests.cs
git commit -m "test: expect renderer pcm frames"
```

---

### Task 4: Update `FakeAudioRenderer` for PCM frames

**Files:**
- Modify: `receiver-windows/src/OpenAudioLink/Receiver/FakeAudioRenderer.cs`

- [ ] **Step 1: Replace raw-byte renderer with PCM-frame renderer**

Replace `receiver-windows/src/OpenAudioLink/Receiver/FakeAudioRenderer.cs` with:

```csharp
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
```

- [ ] **Step 2: Run renderer tests and verify GREEN**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter FakeAudioRendererTests
```

Expected: PASS.

- [ ] **Step 3: Commit renderer implementation update**

```bash
git add receiver-windows/src/OpenAudioLink/Receiver/FakeAudioRenderer.cs
git commit -m "feat: render fake pcm frames"
```

---

### Task 5: Update TCP loopback integration for fake decode path

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`

- [ ] **Step 1: Update encoded payload setup and render assertions**

In `ClientCompletesPhase1aHandshakeAndDeliversAudioToQueue`, replace the `audioPayloads` setup with:

```csharp
                byte[][] encodedFrames =
                {
                    new byte[] { 0x11, 0x22, 0x33, 0x44 },
                    new byte[] { 0x21, 0x22, 0x23, 0x24 },
                    new byte[] { 0x31, 0x32, 0x33, 0x34 },
                };
                byte[][] audioPayloads =
                {
                    HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 1u, 123456003UL, 20, encodedFrames[0]),
                    HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 2u, 123456023UL, 20, encodedFrames[1]),
                    HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 3u, 123456043UL, 20, encodedFrames[2]),
                };
```

Then replace:

```csharp
                FakeAudioRenderer renderer = new FakeAudioRenderer();
                Assert.AreEqual(3, renderer.Drain(queue));
                Assert.AreEqual(0, queue.Count);
                Assert.AreEqual(3, renderer.RenderedCount);

                IReadOnlyList<byte[]> renderedFrames = renderer.RenderedFrames;
                for (int i = 0; i < audioPayloads.Length; i++)
                {
                    CollectionAssert.AreEqual(audioPayloads[i], renderedFrames[i]);
                }
```

with:

```csharp
                FakeAudioRenderer renderer = new FakeAudioRenderer();
                Assert.AreEqual(3, renderer.Drain(queue, new FakeAacDecoder()));
                Assert.AreEqual(0, queue.Count);
                Assert.AreEqual(3, renderer.RenderedCount);

                IReadOnlyList<FakePcmFrame> renderedFrames = renderer.RenderedFrames;
                for (int i = 0; i < audioPayloads.Length; i++)
                {
                    Assert.AreEqual((uint)(i + 1), renderedFrames[i].FrameNumber);
                    Assert.AreEqual(123456003UL + (ulong)(20 * i), renderedFrames[i].CaptureTimestamp);
                    Assert.AreEqual((ushort)20, renderedFrames[i].FrameDuration);
                    CollectionAssert.AreEqual(encodedFrames[i], renderedFrames[i].PcmBytes);
                }
```

- [ ] **Step 2: Run TCP loopback test**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter ClientCompletesPhase1aHandshakeAndDeliversAudioToQueue
```

Expected: PASS.

- [ ] **Step 3: Commit TCP integration update**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
git commit -m "test: decode tcp audio before rendering"
```

---

### Task 6: Final verification and branch push

**Files:**
- Verify only.

- [ ] **Step 1: Run local deterministic checks**

```bash
python3 tools/check_docs_consistency.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
```

Expected:

```text
docs consistency ok: ... markdown files checked
protocol golden packets ok
```

`git diff --check HEAD` prints no output.

- [ ] **Step 2: Run platform tests where available**

Windows CI/build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Android CI/build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Expected: PASS. If local Linux lacks `dotnet` or Android SDK, record the missing tool output and rely on branch CI.

- [ ] **Step 3: Scope guard**

Run:

```bash
rg -n "WASAPI|NAudio|Media Foundation|MediaProjection|AudioRecord|MediaCodec|Task\.Run|Thread|Timer|Jitter|Clock" receiver-windows/src receiver-windows/tests sender-android/app/src || true
```

Expected: no new real playback, decode, Android capture, thread, timer, or jitter implementation is added. Existing `TcpReceiver` thread-pool code may still appear.

- [ ] **Step 4: Push branch**

Because this environment has a known path MTU issue when pushing to `192.168.3.20`, add a temporary host route before push and remove it after push:

```bash
ip route add 192.168.3.20/32 via 192.168.4.1 dev eth0 mtu 1200 || true
git push -u origin phase-1g-fake-decoder-pcm-seam
ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true
```

Expected: branch `phase-1g-fake-decoder-pcm-seam` exists on Gitea and GitHub mirror CI starts for the `phase-*` push.

- [ ] **Step 5: Wait for branch CI**

Wait for the three GitHub mirror CI workflows:

- docs
- android
- windows

Expected: all green before merging to `main`.
