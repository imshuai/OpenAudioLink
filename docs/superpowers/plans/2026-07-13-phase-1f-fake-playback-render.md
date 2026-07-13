# Phase 1-F Windows Fake Playback Render Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Windows fake renderer seam that drains multiple queued audio payloads from `AudioFrameQueue` and records them in FIFO order without real decode or playback.

**Architecture:** Keep `TcpReceiver` and protocol code unchanged. Add one small synchronous `FakeAudioRenderer` in `OpenAudioLink.Receiver`; it repeatedly calls `AudioFrameQueue.TryDequeue`, clones each dequeued payload, and stores render history for tests. Extend the existing TCP loopback test to prove three fake `AUDIO` packets flow from socket receiver to queue and then to the fake renderer.

**Tech Stack:** C#/.NET Framework 4.8 MSTest, existing `OpenAudioLink.Receiver` queue and TCP receiver, existing protocol helpers, Python docs/golden checks.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-13-phase-1f-fake-playback-render-design.md`.

In scope:

- A synchronous Windows `FakeAudioRenderer` test seam.
- Unit tests for queue drain behavior, FIFO render history, empty drain, null validation, and clone isolation.
- A TCP loopback integration assertion that drains the three Phase 1-E fake payloads into the fake renderer.

Out of scope:

- AAC decode.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- Renderer threads, timers, clocks, jitter buffers, async loops, or backpressure.
- Android sender changes.
- Protocol wire-format changes.
- Discovery, UI, tray, installer, configuration, and new dependencies.

---

## Files and Responsibilities

- Create `receiver-windows/src/OpenAudioLink/Receiver/FakeAudioRenderer.cs`
  - Synchronous fake render seam.
  - Drains `AudioFrameQueue` until empty.
  - Stores cloned rendered payloads.
  - Returns cloned render-history snapshots.

- Create `receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAudioRendererTests.cs`
  - Unit tests for drain count, FIFO ordering, append behavior, empty drain, null validation, and clone isolation.

- Modify `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`
  - Existing loopback test drains queued frames into `FakeAudioRenderer` instead of manually dequeuing them.
  - Keeps `PING -> PONG` and `STOP_STREAM` assertions.

No `.csproj` edits are needed because both projects are SDK-style and include `.cs` files by default.

---

### Task 1: Add failing fake renderer unit tests

**Files:**
- Create: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAudioRendererTests.cs`

- [ ] **Step 1: Create renderer tests before implementation**

Create `receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAudioRendererTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run renderer tests and verify RED**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter FakeAudioRendererTests
```

Expected: FAIL because `FakeAudioRenderer` does not exist.

On this Linux workspace, `dotnet` may be unavailable; if so, record `dotnet not found` and rely on branch CI for compile/test execution.

- [ ] **Step 3: Commit failing tests**

If tests fail for the expected missing-type reason, commit:

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAudioRendererTests.cs
git commit -m "test: cover fake audio renderer drain"
```

---

### Task 2: Implement `FakeAudioRenderer`

**Files:**
- Create: `receiver-windows/src/OpenAudioLink/Receiver/FakeAudioRenderer.cs`

- [ ] **Step 1: Add minimal renderer implementation**

Create `receiver-windows/src/OpenAudioLink/Receiver/FakeAudioRenderer.cs`:

```csharp
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
```

- [ ] **Step 2: Run renderer tests and verify GREEN**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter FakeAudioRendererTests
```

Expected: PASS.

- [ ] **Step 3: Commit renderer implementation**

```bash
git add receiver-windows/src/OpenAudioLink/Receiver/FakeAudioRenderer.cs
git commit -m "feat: add fake audio renderer drain seam"
```

---

### Task 3: Drain TCP loopback queue into fake renderer

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`

- [ ] **Step 1: Update loopback test to render queued frames**

In `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`, replace this block in `ClientCompletesPhase1aHandshakeAndDeliversAudioToQueue`:

```csharp
                for (int i = 0; i < audioPayloads.Length; i++)
                {
                    Assert.IsTrue(queue.TryDequeue(out byte[] receivedAudio));
                    CollectionAssert.AreEqual(audioPayloads[i], receivedAudio);
                }
```

with:

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

Add this using at the top of the file:

```csharp
using System.Collections.Generic;
```

- [ ] **Step 2: Run TCP receiver test**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter ClientCompletesPhase1aHandshakeAndDeliversAudioToQueue
```

Expected: PASS. The test still proves `PING -> PONG` after the queue has been drained into `FakeAudioRenderer`, then writes `STOP_STREAM`.

- [ ] **Step 3: Commit integration test**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
git commit -m "test: render queued tcp audio frames"
```

---

### Task 4: Final verification and branch push

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
rg -n "WASAPI|NAudio|Media Foundation|MediaProjection|AudioRecord|MediaCodec|Task\.Run|Thread|Timer|Jitter|Clock|Renderer" receiver-windows/src receiver-windows/tests sender-android/app/src || true
```

Expected: only the intentional `FakeAudioRenderer` names and documentation/test references appear; no real playback, decode, Android capture, thread, timer, or jitter implementation is added.

- [ ] **Step 4: Push branch**

Because this environment has a known path MTU issue when pushing to `192.168.3.20`, add a temporary host route before push and remove it after push:

```bash
ip route add 192.168.3.20/32 via 192.168.4.1 dev eth0 mtu 1200 || true
git push -u origin phase-1f-fake-playback-render
ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true
```

Expected: branch `phase-1f-fake-playback-render` exists on Gitea and GitHub mirror CI starts for the `phase-*` push.

- [ ] **Step 5: Wait for branch CI**

Wait for the three GitHub mirror CI workflows:

- docs
- android
- windows

Expected: all green before merging to `main`.
