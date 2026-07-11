# Phase 1-E Android Multi-Frame Fake Stream Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the existing Android fake sender path write three deterministic `AUDIO` packets and prove the Windows receiver queues those three packets in order.

**Architecture:** Keep `HandshakeClient.run(input, output)` as the only Android sender entry point for this phase. Add a private hard-coded fake frame list inside `HandshakeClient`, loop over it after `STREAM_READY`, and keep the existing synchronous stream-to-stream protocol flow. Windows production code remains unchanged; only the TCP loopback test is extended to assert the Phase 1-D `AudioFrameQueue` receives three frames in FIFO order.

**Tech Stack:** Kotlin/JVM tests with existing Android Gradle project, C#/.NET Framework 4.8 MSTest, existing OpenAudioLink protocol helpers, Python docs/golden checks.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-11-phase-1e-android-multi-frame-fake-stream-design.md`.

In scope:

- Android `HandshakeClient` writes three deterministic fake `AUDIO` packets.
- Android unit tests assert exact packet order, sequence numbers, and payload bytes.
- Windows TCP loopback test asserts three fake payloads reach `AudioFrameQueue` in order.

Out of scope:

- Android `MediaProjection`, `AudioPlaybackCaptureConfiguration`, `AudioRecord`, or `MediaCodec`.
- Android services, coroutines, capture threads, encoder threads, foreground notifications, UI, or settings.
- Windows AAC decode, playback APIs, jitter buffers, renderer threads, or device output.
- Discovery, installer, configuration, and protocol wire-format changes.
- New dependencies.

---

## Files and Responsibilities

- Modify `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`
  - Proves the sender writes `HELLO`, `START_STREAM`, three exact `AUDIO` packets, `PING`, and `STOP_STREAM`.
  - Keeps existing negative path tests.

- Modify `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`
  - Replaces the single `fakeAudioPayload` with three private deterministic fake frames.
  - Loops over fake frames after `STREAM_READY`.

- Modify `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`
  - Uses `CountdownEvent` and `AudioFrameQueue(3)` to assert all three fake frames are queued in FIFO order.

No project file edits are needed.

---

### Task 1: Update Android sender test for three fake frames

**Files:**
- Modify: `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`

- [ ] **Step 1: Replace the happy-path test with exact multi-frame assertions**

In `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`, replace `runWritesHandshakePacketsOnSuccess` with this version and add the two private helpers before the final closing brace of `HandshakeClientTest`:

```kotlin
    @Test
    fun runWritesHandshakePacketsOnSuccess() {
        val input = ByteArrayInputStream(
            PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "receiver", "1.0", 7)) +
                PacketWriter.writePacket(ProtocolConstants.PacketTypeStreamReady, 2, 2, HandshakePayloads.streamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000, 2)) +
                PacketWriter.writePacket(ProtocolConstants.PacketTypePong, 6, 6, HandshakePayloads.ping(5, 123456005))
        )
        val output = ByteArrayOutputStream()

        assertTrue(HandshakeClient().run(input, output))

        val written = ByteArrayInputStream(output.toByteArray())
        assertPacket(written, ProtocolConstants.PacketTypeHello, 1)
        assertPacket(written, ProtocolConstants.PacketTypeStartStream, 2)
        assertArrayEquals(
            HandshakePayloads.audio(ProtocolConstants.CodecAacLc, 1, 123456003, 20, byteArrayOf(0x11, 0x22, 0x33, 0x44)),
            assertPacket(written, ProtocolConstants.PacketTypeAudio, 3)
        )
        assertArrayEquals(
            HandshakePayloads.audio(ProtocolConstants.CodecAacLc, 2, 123456023, 20, byteArrayOf(0x21, 0x22, 0x23, 0x24)),
            assertPacket(written, ProtocolConstants.PacketTypeAudio, 4)
        )
        assertArrayEquals(
            HandshakePayloads.audio(ProtocolConstants.CodecAacLc, 3, 123456043, 20, byteArrayOf(0x31, 0x32, 0x33, 0x34)),
            assertPacket(written, ProtocolConstants.PacketTypeAudio, 5)
        )
        assertArrayEquals(HandshakePayloads.ping(5, 123456005), assertPacket(written, ProtocolConstants.PacketTypePing, 6))
        assertPacket(written, ProtocolConstants.PacketTypeStopStream, 7)
        assertEquals(0, written.available())
    }

    private fun assertPacket(input: ByteArrayInputStream, packetType: Int, sequenceNumber: Long): ByteArray {
        val packet = PacketReader.readPacket(input)
        val header = PacketParser.parseHeader(packet)
        assertEquals(packetType, header.packetType)
        assertEquals(sequenceNumber, header.sequenceNumber)
        return PacketParser.payload(packet)
    }
```

- [ ] **Step 2: Run Android happy-path test and verify it fails before implementation**

Run on an Android build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.network.HandshakeClientTest.runWritesHandshakePacketsOnSuccess
```

Expected: FAIL because current `HandshakeClient` writes only one `AUDIO` packet and still uses `PING` sequence `4` / `STOP_STREAM` sequence `5`.

- [ ] **Step 3: Commit the failing test**

Do not commit if the test unexpectedly passes. If it fails for the expected single-frame reason, commit:

```bash
git add sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt
git commit -m "test: expect android fake stream frames"
```

---

### Task 2: Make Android `HandshakeClient` write three fake frames

**Files:**
- Modify: `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`

- [ ] **Step 1: Replace the single fake payload with private fake frames**

In `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`, replace:

```kotlin
    private val pingPayload = HandshakePayloads.ping(5, 123456005)
    private val fakeAudioPayload = HandshakePayloads.audio(ProtocolConstants.CodecAacLc, 1, 123456789, 20, byteArrayOf(0x11, 0x22, 0x33, 0x44))
```

with:

```kotlin
    private val pingPayload = HandshakePayloads.ping(5, 123456005)
    private val fakeAudioFrames = listOf(
        FakeAudioFrame(1, 123456003, byteArrayOf(0x11, 0x22, 0x33, 0x44)),
        FakeAudioFrame(2, 123456023, byteArrayOf(0x21, 0x22, 0x23, 0x24)),
        FakeAudioFrame(3, 123456043, byteArrayOf(0x31, 0x32, 0x33, 0x34)),
    )
```

Add this private data class inside `HandshakeClient`, below the fields and above `run`:

```kotlin
    private data class FakeAudioFrame(
        val frameNumber: Long,
        val captureTimestamp: Long,
        val encoded: ByteArray,
    )
```

- [ ] **Step 2: Replace the single audio write with a loop**

In `run`, replace:

```kotlin
            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeAudio, 3, 123456789, fakeAudioPayload))
            output.flush()

            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypePing, 4, 123456004, pingPayload))
```

with:

```kotlin
            for (frame in fakeAudioFrames) {
                output.write(PacketWriter.writePacket(
                    ProtocolConstants.PacketTypeAudio,
                    frame.frameNumber + 2,
                    frame.captureTimestamp,
                    HandshakePayloads.audio(ProtocolConstants.CodecAacLc, frame.frameNumber, frame.captureTimestamp, 20, frame.encoded)
                ))
                output.flush()
            }

            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypePing, 6, 123456006, pingPayload))
```

Then replace:

```kotlin
            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeStopStream, 5, 123456006))
```

with:

```kotlin
            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeStopStream, 7, 123456007))
```

- [ ] **Step 3: Run Android network tests**

Run on an Android build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.network.HandshakeClientTest
```

Expected: PASS. Existing failure tests still return `false` when `WELCOME`, `STREAM_READY`, timeout, or mismatched `PONG` occurs.

- [ ] **Step 4: Commit Android implementation**

Run:

```bash
git add sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt
git commit -m "feat: send android fake audio stream frames"
```

---

### Task 3: Update Windows TCP loopback queue coverage for three frames

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`

- [ ] **Step 1: Replace the single-frame loopback test with three-frame queue assertions**

In `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`, replace `ClientCompletesPhase1aHandshakeAndDeliversAudioToQueue` with:

```csharp
        [TestMethod]
        public void ClientCompletesPhase1aHandshakeAndDeliversAudioToQueue()
        {
            int audioCalls = 0;
            AudioFrameQueue queue = new AudioFrameQueue(3);
            using (CountdownEvent audioReceived = new CountdownEvent(3))
            using (TcpReceiver receiver = TcpReceiver.StartLoopback(payload =>
            {
                queue.Enqueue(payload);
                Interlocked.Increment(ref audioCalls);
                audioReceived.Signal();
            }))
            using (TcpClient client = Connect(receiver))
            {
                NetworkStream stream = client.GetStream();

                Write(stream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
                AssertPacket(stream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 1));

                Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20));
                AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

                byte[][] audioPayloads = new[]
                {
                    HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 1u, 123456003UL, 20, new byte[] { 0x11, 0x22, 0x33, 0x44 }),
                    HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 2u, 123456023UL, 20, new byte[] { 0x21, 0x22, 0x23, 0x24 }),
                    HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 3u, 123456043UL, 20, new byte[] { 0x31, 0x32, 0x33, 0x34 }),
                };

                for (int i = 0; i < audioPayloads.Length; i++)
                {
                    Write(stream, ProtocolConstants.PacketTypeAudio, (uint)(i + 3), audioPayloads[i]);
                }

                Assert.IsTrue(audioReceived.Wait(SocketTimeoutMilliseconds), "Timed out waiting for audio queue callbacks.");
                Assert.AreEqual(3, audioCalls);
                Assert.AreEqual(3, queue.Count);
                foreach (byte[] audioPayload in audioPayloads)
                {
                    Assert.IsTrue(queue.TryDequeue(out byte[] receivedAudio));
                    CollectionAssert.AreEqual(audioPayload, receivedAudio);
                }

                byte[] ping = HandshakePayloads.Ping(5u, 123456005UL);
                Write(stream, ProtocolConstants.PacketTypePing, 6u, ping);
                AssertPacket(stream, ProtocolConstants.PacketTypePong, ping);

                Write(stream, ProtocolConstants.PacketTypeStopStream, 7u, new byte[0]);
            }
        }
```

- [ ] **Step 2: Run Windows TCP receiver tests**

Run on a Windows/.NET environment:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter FullyQualifiedName~TcpReceiverTests
```

Expected: PASS. The loopback test waits for exactly three queue callbacks and then verifies `PING -> PONG` and `STOP_STREAM` still work.

- [ ] **Step 3: Commit Windows loopback coverage**

Run:

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
git commit -m "test: cover tcp receiver fake audio stream frames"
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

- [ ] **Step 2: Run Android unit tests where Android build host is available**

Run:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Expected: PASS.

- [ ] **Step 3: Run Windows test suite where .NET is available**

Run:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Expected: PASS.

- [ ] **Step 4: Confirm no scope creep**

Run:

```bash
rg -n "MediaProjection|AudioPlaybackCapture|AudioRecord|MediaCodec|WASAPI|NAudio|Media Foundation|mDNS|Foreground|foreground|Jitter|Clock|Renderer|Playback|Task\.Run" sender-android/app/src receiver-windows/src receiver-windows/tests || true
```

Expected: no new Phase 1-E production hits. Existing `ThreadPool` usage in `TcpReceiver` is allowed because it predates Phase 1-E.

- [ ] **Step 5: Push branch**

Run:

```bash
git status --short --branch
git push -u origin phase-1e-android-multi-frame-fake-stream
```

Expected:

```text
branch 'phase-1e-android-multi-frame-fake-stream' set up to track 'origin/phase-1e-android-multi-frame-fake-stream'
```

GitHub mirror should run the `phase-*` workflows after the Gitea push.

---

## Self-Review Checklist

Spec coverage:

- Android writes exactly three fake `AUDIO` packets: Task 1 and Task 2.
- Frame numbers `1`, `2`, `3`: Task 1 and Task 2.
- Capture timestamps `123456003`, `123456023`, `123456043`: Task 1 and Task 2.
- Encoded bytes `11 22 33 44`, `21 22 23 24`, `31 32 33 34`: Task 1 and Task 2.
- `PING` / `STOP_STREAM` sequence numbers `6` / `7`: Task 1, Task 2, and Task 3.
- Android exact packet order and bytes: Task 1.
- Windows TCP loopback FIFO queue proof: Task 3.
- No wire-format, real capture/encoder/decoder/playback/discovery/UI/config changes: Task 4 scope scan.

Type consistency:

- Kotlin fake frame type: `FakeAudioFrame(frameNumber: Long, captureTimestamp: Long, encoded: ByteArray)`.
- Android helper remains `HandshakePayloads.audio(codec: Int, frameNumber: Long, captureTimestamp: Long, frameDurationMs: Int, encodedData: ByteArray)`.
- Windows helper remains `HandshakePayloads.Audio(byte codec, uint frameNumber, ulong captureTimestamp, ushort frameDuration, byte[] encodedData)`.
- Existing sink and queue APIs remain `Action<byte[]>`, `AudioFrameQueue.Enqueue`, and `AudioFrameQueue.TryDequeue`.
