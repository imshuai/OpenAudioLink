# Phase 1-D Receiver AAC Queue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a minimal bounded AAC payload queue behind the existing Windows receiver audio sink seam.

**Architecture:** `ReceiverSession` keeps validating `AUDIO` packets and delivering accepted payloads through the existing `Action<byte[]>` sink. Phase 1-D adds `AudioFrameQueue`, a small lock-protected FIFO queue that clones enqueued payloads, enforces capacity, drops oldest frames on overflow, and records drop/underflow counters. `TcpReceiver` keeps its existing optional sink API; tests pass queue enqueue callbacks into that seam.

**Tech Stack:** C# on .NET Framework 4.8, MSTest, existing OpenAudioLink protocol helpers, Python docs/golden checks.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-10-phase-1d-receiver-aac-queue-design.md`.

In scope:

- Windows receiver `AudioFrameQueue`.
- Unit tests for bounded FIFO, cloning, overflow, and underflow.
- ReceiverSession test proving accepted `AUDIO` can enqueue through the existing sink seam.
- TcpReceiver loopback test proving TCP `AUDIO` can enqueue through the existing sink seam.

Out of scope:

- AAC decode.
- Playback APIs such as WASAPI, NAudio, or Media Foundation.
- Background decoder/playback threads.
- Android capture or encoder changes.
- Protocol wire-format changes.
- UI, discovery, installer, or configuration changes.

---

## Files and Responsibilities

- Create `receiver-windows/src/OpenAudioLink/Receiver/AudioFrameQueue.cs`
  - Owns bounded in-memory AAC payload queue behavior.
  - Uses `Queue<byte[]>` plus `lock`; no external dependencies.

- Create `receiver-windows/tests/OpenAudioLink.Tests/Receiver/AudioFrameQueueTests.cs`
  - Covers constructor validation, enqueue validation, FIFO, clone ownership, overflow, and underflow.

- Modify `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs`
  - Adds one focused test that passes `AudioFrameQueue.Enqueue` as the existing `ReceiverSession` sink.

- Modify `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`
  - Updates the existing audio loopback test to enqueue into `AudioFrameQueue` while preserving bounded async waiting.

No `.csproj` edits are needed because SDK-style projects include `*.cs` files by default.

---

### Task 1: Add `AudioFrameQueue`

**Files:**
- Create: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/AudioFrameQueueTests.cs`
- Create: `receiver-windows/src/OpenAudioLink/Receiver/AudioFrameQueue.cs`

- [ ] **Step 1: Write failing queue unit tests**

Create `receiver-windows/tests/OpenAudioLink.Tests/Receiver/AudioFrameQueueTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails before implementation**

Run on a Windows/.NET environment:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter FullyQualifiedName~AudioFrameQueueTests
```

Expected: FAIL at compile time with a message containing:

```text
The type or namespace name 'AudioFrameQueue' could not be found
```

- [ ] **Step 3: Write minimal queue implementation**

Create `receiver-windows/src/OpenAudioLink/Receiver/AudioFrameQueue.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace OpenAudioLink.Receiver
{
    public sealed class AudioFrameQueue
    {
        private readonly object gate = new object();
        private readonly Queue<byte[]> frames = new Queue<byte[]>();
        private ulong droppedFrames;
        private ulong underflowCount;

        public AudioFrameQueue(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
        }

        public int Capacity { get; }

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return frames.Count;
                }
            }
        }

        public ulong DroppedFrames
        {
            get
            {
                lock (gate)
                {
                    return droppedFrames;
                }
            }
        }

        public ulong UnderflowCount
        {
            get
            {
                lock (gate)
                {
                    return underflowCount;
                }
            }
        }

        public void Enqueue(byte[] payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            byte[] acceptedPayload = (byte[])payload.Clone();

            lock (gate)
            {
                if (frames.Count == Capacity)
                {
                    frames.Dequeue();
                    droppedFrames++;
                }

                frames.Enqueue(acceptedPayload);
            }
        }

        public bool TryDequeue(out byte[] payload)
        {
            lock (gate)
            {
                if (frames.Count == 0)
                {
                    underflowCount++;
                    payload = null;
                    return false;
                }

                payload = frames.Dequeue();
                return true;
            }
        }
    }
}
```

- [ ] **Step 4: Run queue tests and verify they pass**

Run:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter FullyQualifiedName~AudioFrameQueueTests
```

Expected: PASS; all tests in `AudioFrameQueueTests` pass.

- [ ] **Step 5: Commit queue implementation**

Run:

```bash
git add receiver-windows/src/OpenAudioLink/Receiver/AudioFrameQueue.cs receiver-windows/tests/OpenAudioLink.Tests/Receiver/AudioFrameQueueTests.cs
git commit -m "feat: add bounded receiver audio frame queue"
```

---

### Task 2: Prove `ReceiverSession` can enqueue accepted `AUDIO`

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs`

- [ ] **Step 1: Write focused ReceiverSession queue test**

In `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs`, insert this test after `ProcessAudioWhileStreaming_RecordsFrameCallsSinkAndReturnsNull`:

```csharp
        [TestMethod]
        public void ProcessAudioWhileStreaming_EnqueuesAcceptedPayload()
        {
            AudioFrameQueue queue = new AudioFrameQueue(2);
            ReceiverSession session = StreamingSession(queue.Enqueue);
            byte[] payload = ValidAudioPayload();

            byte[] response = session.Process(PacketWriter.WritePacket(ProtocolConstants.PacketTypeAudio, 5u, 123456789UL, payload));

            Assert.IsNull(response);
            Assert.AreEqual(1, queue.Count);
            Assert.IsTrue(queue.TryDequeue(out byte[] queued));
            CollectionAssert.AreEqual(payload, queued);
        }
```

No production code change is expected in this task. `ReceiverSession` already owns the sink seam from Phase 1-C.

- [ ] **Step 2: Run ReceiverSession tests**

Run:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter FullyQualifiedName~ReceiverSessionTests
```

Expected: PASS; the new queue test and existing Phase 1-A/1-B/1-C receiver session tests pass.

- [ ] **Step 3: Commit ReceiverSession queue coverage**

Run:

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs
git commit -m "test: cover receiver session audio queue sink"
```

---

### Task 3: Prove TCP loopback can enqueue accepted `AUDIO`

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`

- [ ] **Step 1: Replace the existing audio sink loopback test with queue-backed assertions**

In `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`, replace the whole `ClientCompletesPhase1aHandshakeAndDeliversAudioToSink` method with:

```csharp
        [TestMethod]
        public void ClientCompletesPhase1aHandshakeAndDeliversAudioToQueue()
        {
            int audioCalls = 0;
            AudioFrameQueue queue = new AudioFrameQueue(2);
            using (ManualResetEventSlim audioReceived = new ManualResetEventSlim(false))
            using (TcpReceiver receiver = TcpReceiver.StartLoopback(payload =>
            {
                queue.Enqueue(payload);
                Interlocked.Increment(ref audioCalls);
                audioReceived.Set();
            }))
            using (TcpClient client = Connect(receiver))
            {
                NetworkStream stream = client.GetStream();

                Write(stream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
                AssertPacket(stream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 1));

                Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20));
                AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

                byte[] audioPayload = HandshakePayloads.Audio(
                    ProtocolConstants.CodecAacLc,
                    1u,
                    123456789UL,
                    20,
                    new byte[] { 0x11, 0x22, 0x33, 0x44 });
                Write(stream, ProtocolConstants.PacketTypeAudio, 3u, audioPayload);
                Assert.IsTrue(audioReceived.Wait(SocketTimeoutMilliseconds), "Timed out waiting for audio queue callback.");
                Assert.AreEqual(1, audioCalls);
                Assert.AreEqual(1, queue.Count);
                Assert.IsTrue(queue.TryDequeue(out byte[] receivedAudio));
                CollectionAssert.AreEqual(audioPayload, receivedAudio);

                byte[] ping = HandshakePayloads.Ping(4u, 123UL);
                Write(stream, ProtocolConstants.PacketTypePing, 4u, ping);
                AssertPacket(stream, ProtocolConstants.PacketTypePong, ping);

                Write(stream, ProtocolConstants.PacketTypeStopStream, 5u, new byte[0]);
            }
        }
```

- [ ] **Step 2: Run TcpReceiver tests**

Run:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter FullyQualifiedName~TcpReceiverTests
```

Expected: PASS; TCP loopback still completes `HELLO`, `START_STREAM`, `AUDIO`, `PING`, and `STOP_STREAM`.

- [ ] **Step 3: Commit TCP queue coverage**

Run:

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
git commit -m "test: cover tcp receiver audio queue sink"
```

---

### Task 4: Final verification and branch push

**Files:**
- Verify only; no planned file edits.

- [ ] **Step 1: Run docs and golden checks**

Run:

```bash
python3 tools/check_docs_consistency.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
```

Expected:

```text
docs consistency ok: 12 markdown files checked
protocol golden packets ok
```

`git diff --check HEAD` prints nothing and exits `0`.

- [ ] **Step 2: Run Windows test suite where .NET is available**

Run:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Expected: PASS; all Windows receiver tests pass.

- [ ] **Step 3: Run Android CI-equivalent check where Android build host is available**

Run:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Expected: PASS; Android tests remain green because Phase 1-D does not modify Android code.

- [ ] **Step 4: Confirm no scope creep**

Run:

```bash
rg -n "WASAPI|NAudio|Media Foundation|AudioPlaybackCapture|AudioRecord|MediaCodec|mDNS|Foreground|Jitter|Clock|Renderer|Playback" receiver-windows/src receiver-windows/tests sender-android || true
```

Expected: no new production hits from Phase 1-D except existing documentation-independent identifiers already present before this branch. If this command reports a new Phase 1-D production hit, remove that scope expansion before pushing.

- [ ] **Step 5: Push branch**

Run:

```bash
git status --short --branch
git push -u origin phase-1d-receiver-aac-queue
```

Expected:

```text
branch 'phase-1d-receiver-aac-queue' set up to track 'origin/phase-1d-receiver-aac-queue'
```

GitHub mirror should run the `phase-*` workflows after the Gitea push.

---

## Self-Review Checklist

Spec coverage:

- Queue exists and is bounded: Task 1.
- Invalid capacity and null payload rejection: Task 1.
- Enqueue clones payload: Task 1.
- FIFO dequeue: Task 1.
- Drop-oldest overflow and `DroppedFrames`: Task 1.
- Empty dequeue and `UnderflowCount`: Task 1.
- `ReceiverSession` valid `AUDIO` flows into queue through existing sink seam: Task 2.
- `TcpReceiver` TCP `AUDIO` flows into queue through existing sink seam: Task 3.
- No wire, Android, decoder, playback, discovery, UI, or configuration changes: Task 4 scope scan.

Type consistency:

- Production type: `OpenAudioLink.Receiver.AudioFrameQueue`.
- Constructor: `AudioFrameQueue(int capacity)`.
- Properties: `Capacity`, `Count`, `DroppedFrames`, `UnderflowCount`.
- Methods: `Enqueue(byte[] payload)`, `TryDequeue(out byte[] payload)`.
- Existing sink type remains `Action<byte[]>`.
