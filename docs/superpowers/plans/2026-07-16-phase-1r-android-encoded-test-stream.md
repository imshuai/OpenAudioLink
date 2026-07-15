# Phase 1-R Android Encoded Test Stream Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Android's executable fixed-AAC path with a finite deterministic PCM stream encoded by the existing `MediaCodecAacEncoder`, then replay the exact Android wire artifact through the existing Windows `ReceiverRuntime`.

**Architecture:** `HandshakeClient` receives one lazy `() -> List<ByteArray>` source invoked only after successful `STREAM_READY`. `TcpHandshakeClient` defaults that source to a concrete three-frame deterministic `MediaCodec` helper, sends all four qualified output candidates with a fixed synthetic timeline, and retains the existing synchronous socket owner thread. An API 29 loopback test exports the exact eight outbound packets; the dependent Windows x64 interop job replays those bytes through the real TCP/Media Foundation runtime and fake renderer.

**Tech Stack:** Kotlin/JVM, Android API 29 `MediaCodec`, Java sockets, JUnit 4, AndroidX instrumentation, C#/.NET Framework 4.8, MSTest, Windows Media Foundation, GitHub Actions, Python documentation/fixture checks, Markdown.

---

## Scope Check

Implement only:

```text
docs/superpowers/specs/2026-07-16-phase-1r-android-encoded-test-stream-design.md
```

In scope:

- Lazy post-`STREAM_READY` AAC frame production.
- Non-empty/65,517-byte wire preflight before the first `AUDIO` write.
- Dynamic four-frame metadata, packet sequences, `PING`, and `STOP_STREAM`.
- Three deterministic stereo PCM frames encoded synchronously into all four
  qualified API 29 candidates.
- Real `TcpHandshakeClient` composition and accurate UI label.
- Strict JVM producer-failure socket teardown.
- API 29 exact outbound-wire capture.
- Windows x64 exact artifact replay through `ReceiverRuntime`.
- Active-document corrections and exact-head CI evidence.

Out of scope:

- `MediaProjection`, `AudioPlaybackCaptureConfiguration`, `AudioRecord`, live
  capture, capture clocks, or permission UI.
- Foreground service, notification, ViewModel, settings, discovery, or logs.
- Queue, coroutine, executor, pacing, continuous streaming, cancellation,
  backpressure, reconnect policy, or retry.
- Windows audible playback, device APIs, PCM queue, or production changes.
- Protocol bytes, packet types, constants, checked-in fixtures, dependencies,
  interfaces, factories, or DI.
- Latency, power, endurance, hardware/OEM support, Phase 1 completion, or
  Version 1.0 completion claims.

---

## Files And Responsibilities

Android production:

- Create `sender-android/app/src/main/java/com/openaudiolink/audio/EncodedTestStream.kt`
  — fixed PCM generation and one finite encoder lifecycle.
- Modify `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`
  — lazy source, preflight, four-frame metadata, dynamic control packets.
- Modify `sender-android/app/src/main/java/com/openaudiolink/network/TcpHandshakeClient.kt`
  — source composition and JVM failure-test seam.
- Modify `sender-android/app/src/main/java/com/openaudiolink/MainActivity.kt`
  — encoded-test-stream label.
- Delete `sender-android/app/src/main/java/com/openaudiolink/network/FakeAacFrame.kt`.

Android tests:

- Modify `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`.
- Create `sender-android/app/src/test/java/com/openaudiolink/network/TcpHandshakeClientTest.kt`.
- Modify `sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt`.
- Create `sender-android/app/src/androidTest/java/com/openaudiolink/network/TcpEncodedTestStreamTest.kt`.

Interop and docs:

- Modify `.github/workflows/android.yml`.
- Modify `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs`.
- Modify `docs/04-Android.md`, `docs/06-Audio.md`, `docs/10-Testing.md`, and
  `docs/11-Roadmap.md`.
- Update the Phase 1-R design status/evidence after final verification.

No production Windows, protocol, manifest, Gradle, fixture, or normal Windows
workflow file changes are planned.

---

## Execution Conventions

Branch:

```text
phase-1r-android-encoded-test-stream
```

Base main SHA:

```text
d160b6b2ffda91f5316949452fc4f9ce6d8d8aa9
```

Execute only in:

```text
/root/.config/superpowers/worktrees/OpenAudioLink/phase-1r-android-encoded-test-stream
```

The design commits precede this plan:

```text
721a03b docs: add phase 1r encoded test stream spec
40ef31a docs: tighten phase 1r wire preflight
```

The local host is `aarch64`; installed Android AAPT2 binaries are x86-64.
Local Android compilation is therefore not evidence. Run documentation,
fixture, golden, and diff checks locally. Establish every intentional Android
RED/GREEN on the exact pushed SHA in GitHub's x86-64 unit/API 29 jobs. Windows
native behavior is authoritative in `windows-2022` CI.

Push to Gitea with the MTU route:

```bash
set -e
ADDED_ROUTE=0
if ip route add 192.168.3.20/32 via 192.168.4.1 dev eth0 mtu 1200 2>/dev/null; then
  ADDED_ROUTE=1
fi
cleanup() {
  if [ "$ADDED_ROUTE" -eq 1 ]; then
    ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true
  fi
}
trap cleanup EXIT
git push origin phase-1r-android-encoded-test-stream
```

Verify source and mirror refs:

```bash
HEAD_SHA=$(git rev-parse HEAD)
GITEA_SHA=$(git ls-remote origin refs/heads/phase-1r-android-encoded-test-stream | cut -f1)
test "$GITEA_SHA" = "$HEAD_SHA"
for attempt in $(seq 1 60); do
  GITHUB_SHA=$(gh api repos/imshuai/OpenAudioLink/commits/phase-1r-android-encoded-test-stream --jq .sha 2>/dev/null || true)
  [ "$GITHUB_SHA" = "$HEAD_SHA" ] && break
  sleep 5
done
test "$GITHUB_SHA" = "$HEAD_SHA"
```

List exact-head runs:

```bash
HEAD_SHA=$(git rev-parse HEAD)
gh api 'repos/imshuai/OpenAudioLink/actions/runs?branch=phase-1r-android-encoded-test-stream&per_page=100' \
  --jq ".workflow_runs[] | select(.head_sha == \"$HEAD_SHA\") | [.id,.name,.event,.status,.conclusion] | @tsv"
```

For intermediate TDD commits, inspect the decisive exact-head Android or
Windows interop run. The final candidate and final status commit each require
exactly one successful `push` run named `docs`, `windows`, and `android`.

Every task ends with specification review, then code-quality review. Fix every
Critical or Important finding and repeat the affected review before advancing.

---

### Task 1: Add The Lazy Wire-Frame Boundary

**Files:**
- Modify: `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`
- Create: `sender-android/app/src/test/java/com/openaudiolink/network/TcpHandshakeClientTest.kt`
- Modify: `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`
- Modify: `sender-android/app/src/main/java/com/openaudiolink/network/TcpHandshakeClient.kt`

- [ ] **Step 1: Update existing JVM calls to use an explicit fixture source**

Add this test-local helper:

```kotlin
private fun fixtureFrames(count: Int = 3): List<ByteArray> {
    val encoded = TestFixtures.read("testdata/audio/aac-lc-48k-stereo-1024.raw")
    return List(count) { encoded.clone() }
}

private fun client(
    supplier: () -> List<ByteArray> = { fixtureFrames() },
): HandshakeClient = HandshakeClient(supplier)
```

Replace each `HandshakeClient().run(...)` in `HandshakeClientTest` with
`client().run(...)`. Keep every existing assertion.

- [ ] **Step 2: Add lazy timing and rejection tests**

Add:

```kotlin
@Test
fun supplierRunsOnceAfterSuccessfulStreamReady() {
    val pong = PacketWriter.writePacket(
        ProtocolConstants.PacketTypePong,
        6,
        6,
        HandshakePayloads.ping(5, 123498671),
    )
    val input = ByteArrayInputStream(
        welcomeSuccess() + streamReadySuccess() + pong,
    )
    var calls = 0

    assertTrue(client {
        calls++
        assertEquals(pong.size, input.available())
        fixtureFrames()
    }.run(input, ByteArrayOutputStream()))

    assertEquals(1, calls)
}

@Test
fun failedStreamReadyDoesNotCallSupplier() {
    val input = ByteArrayInputStream(
        welcomeSuccess() + PacketWriter.writePacket(
            ProtocolConstants.PacketTypeStreamReady,
            2,
            2,
            HandshakePayloads.streamReady(
                ProtocolConstants.StreamResultUnsupportedCodec,
                ProtocolConstants.CodecAacLc,
                48000,
                2,
            ),
        ),
    )

    assertFalse(client {
        fail("supplier ran after failed STREAM_READY")
        emptyList()
    }.run(input, ByteArrayOutputStream()))
}
```

Extract exact test-local response helpers:

```kotlin
private fun welcomeSuccess(): ByteArray = PacketWriter.writePacket(
    ProtocolConstants.PacketTypeWelcome,
    1,
    1,
    HandshakePayloads.welcome(
        ProtocolConstants.ResultSuccess,
        "receiver",
        "1.0",
        7,
    ),
)

private fun streamReadySuccess(): ByteArray = PacketWriter.writePacket(
    ProtocolConstants.PacketTypeStreamReady,
    2,
    2,
    HandshakePayloads.streamReady(
        ProtocolConstants.StreamResultSuccess,
        ProtocolConstants.CodecAacLc,
        48000,
        2,
    ),
)
```

Use these helpers inside `successfulResponses` to preserve existing bytes.
Update the existing busy and protocol-rejection tests to pass suppliers that
call `fail(...)`, proving no producer work occurs before successful readiness.

- [ ] **Step 3: Add the exact four-frame packet test**

Add:

```kotlin
@Test
fun fourFrameSourceUsesExactSyntheticTimelineAndDynamicControlSequences() {
    val input = ByteArrayInputStream(successfulResponses(frameCount = 4))
    val output = ByteArrayOutputStream()
    val frames = fixtureFrames(4)

    assertTrue(client { frames }.run(input, output))

    val packets = ByteArrayInputStream(output.toByteArray())
    assertPacket(packets, ProtocolConstants.PacketTypeHello, 1, 123456000)
    assertPacket(packets, ProtocolConstants.PacketTypeStartStream, 2, 123456002)
    val timestamps = longArrayOf(123456003, 123477336, 123498670, 123520003)
    timestamps.forEachIndexed { index, timestamp ->
        assertArrayEquals(
            HandshakePayloads.audio(
                ProtocolConstants.CodecAacLc,
                (index + 1).toLong(),
                timestamp,
                21,
                frames[index],
            ),
            assertPacket(
                packets,
                ProtocolConstants.PacketTypeAudio,
                (index + 3).toLong(),
                timestamp,
            ),
        )
    }
    assertArrayEquals(
        HandshakePayloads.ping(6, 123520004),
        assertPacket(packets, ProtocolConstants.PacketTypePing, 7, 123520005),
    )
    assertPacket(packets, ProtocolConstants.PacketTypeStopStream, 8, 123520006)
    assertEquals(0, packets.available())
}
```

Change `successfulResponses` to accept `frameCount` and produce the matching
PONG payload:

```kotlin
private fun successfulResponses(frameCount: Int = 3): ByteArray {
    val lastAudioSequence = frameCount.toLong() + 2
    val lastTimestamp = 123456003L +
        ((frameCount - 1).toLong() * 64_000L + 1L) / 3L
    return welcomeSuccess() + streamReadySuccess() + PacketWriter.writePacket(
        ProtocolConstants.PacketTypePong,
        lastAudioSequence + 1,
        lastTimestamp + 3,
        HandshakePayloads.ping(lastAudioSequence, lastTimestamp + 1),
    )
}
```

`HandshakeClient` validates only PONG type/payload, so the response header
timestamp is diagnostic; keep the sender's outbound exact oracle unchanged.

- [ ] **Step 4: Add preflight failure tests**

Add:

```kotlin
@Test
fun invalidFrameSourcesFailBeforeFirstAudioPacket() {
    val invalidSources = listOf<() -> List<ByteArray>>(
        { emptyList() },
        { listOf(ByteArray(0)) },
        { listOf(ByteArray(ProtocolConstants.MaxPacketSize -
            ProtocolConstants.AudioPayloadHeaderSize + 1)) },
    )

    invalidSources.forEach { supplier ->
        val output = ByteArrayOutputStream()
        assertThrows(IllegalArgumentException::class.java) {
            client(supplier).run(
                ByteArrayInputStream(welcomeSuccess() + streamReadySuccess()),
                output,
            )
        }

        val packets = ByteArrayInputStream(output.toByteArray())
        assertPacket(packets, ProtocolConstants.PacketTypeHello, 1, 123456000)
        assertPacket(packets, ProtocolConstants.PacketTypeStartStream, 2, 123456002)
        assertEquals(0, packets.available())
    }
}
```

- [ ] **Step 5: Add the real socket producer-failure test**

Create `TcpHandshakeClientTest.kt` with:

```kotlin
package com.openaudiolink.network

import com.openaudiolink.ManualConnectController
import com.openaudiolink.ManualConnectStatus
import com.openaudiolink.protocol.HandshakePayloads
import com.openaudiolink.protocol.PacketParser
import com.openaudiolink.protocol.PacketReader
import com.openaudiolink.protocol.PacketWriter
import com.openaudiolink.protocol.ProtocolConstants
import java.net.ServerSocket
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.atomic.AtomicReference
import kotlin.concurrent.thread
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Test

class TcpHandshakeClientTest {
    @Test
    fun producerFailureClosesSocketOnOwnerThreadAndReportsFailed() {
        val failure = AtomicReference<Throwable?>()
        val supplierThread = AtomicLong(-1L)
        ServerSocket(0).use { listener ->
            listener.soTimeout = SOCKET_TIMEOUT_MS
            val server = thread(isDaemon = true) {
                try {
                    listener.accept().use { client ->
                        client.soTimeout = SOCKET_TIMEOUT_MS
                        val input = client.getInputStream()
                        val output = client.getOutputStream()
                        assertType(PacketReader.readPacket(input), ProtocolConstants.PacketTypeHello)
                        output.write(PacketWriter.writePacket(
                            ProtocolConstants.PacketTypeWelcome,
                            1,
                            1,
                            HandshakePayloads.welcome(
                                ProtocolConstants.ResultSuccess,
                                "receiver",
                                "1.0",
                                7,
                            ),
                        ))
                        assertType(PacketReader.readPacket(input), ProtocolConstants.PacketTypeStartStream)
                        output.write(PacketWriter.writePacket(
                            ProtocolConstants.PacketTypeStreamReady,
                            2,
                            2,
                            HandshakePayloads.streamReady(
                                ProtocolConstants.StreamResultSuccess,
                                ProtocolConstants.CodecAacLc,
                                48000,
                                2,
                            ),
                        ))
                        assertEquals(-1, input.read())
                    }
                } catch (error: Throwable) {
                    failure.set(error)
                }
            }

            val owner = Thread.currentThread().id
            val status = ManualConnectController { host ->
                TcpHandshakeClient {
                    supplierThread.set(Thread.currentThread().id)
                    throw IllegalStateException("producer failed")
                }.connect(host, listener.localPort)
            }.connect("127.0.0.1")

            server.join(JOIN_TIMEOUT_MS)
            assertFalse("server thread timed out", server.isAlive)
            assertNull(failure.get())
            assertEquals(owner, supplierThread.get())
            assertEquals(ManualConnectStatus.Failed, status)
        }
    }

    private fun assertType(packet: ByteArray, expected: Int) {
        assertEquals(expected, PacketParser.parseHeader(packet).packetType)
    }

    private companion object {
        const val SOCKET_TIMEOUT_MS = 15_000
        const val JOIN_TIMEOUT_MS = 30_000L
    }
}
```

- [ ] **Step 6: Commit and prove intentional RED**

```bash
git add sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt \
  sender-android/app/src/test/java/com/openaudiolink/network/TcpHandshakeClientTest.kt
git commit -m "test: specify lazy encoded frame source"
```

Push. Require exact-head Android failure because the constructors and dynamic
wire behavior do not exist; inspect `gh run view <id> --log-failed` and require
no unrelated failure.

- [ ] **Step 7: Implement the minimum lazy boundary**

Replace fixed frame fields in `HandshakeClient` with:

```kotlin
class HandshakeClient(
    private val audioFrameSupplier: () -> List<ByteArray>,
) {
    fun run(input: InputStream, output: OutputStream): Boolean {
        try {
            output.write(PacketWriter.writePacket(
                ProtocolConstants.PacketTypeHello,
                1,
                HELLO_TIMESTAMP,
                HandshakePayloads.hello(
                    "Android Phone",
                    "1.0.0",
                    ProtocolConstants.PlatformAndroid,
                    ProtocolConstants.CapabilityAacSupported,
                ),
            ))
            output.flush()
            if (!readResult(input, ProtocolConstants.PacketTypeWelcome,
                    ProtocolConstants.ResultSuccess)) return false

            output.write(PacketWriter.writePacket(
                ProtocolConstants.PacketTypeStartStream,
                2,
                START_TIMESTAMP,
                HandshakePayloads.startStream(
                    ProtocolConstants.CodecAacLc,
                    48000,
                    2,
                    192000,
                    21,
                ),
            ))
            output.flush()
            if (!readResult(input, ProtocolConstants.PacketTypeStreamReady,
                    ProtocolConstants.StreamResultSuccess)) return false

            val frames = audioFrameSupplier()
            require(frames.isNotEmpty()) { "Encoded test stream is empty." }
            frames.forEach { frame ->
                require(frame.isNotEmpty()) { "AAC access unit is empty." }
                require(frame.size <= MAX_ENCODED_SIZE) {
                    "AAC access unit exceeds wire payload size."
                }
            }

            frames.forEachIndexed { index, encoded ->
                val timestamp = audioTimestamp(index)
                output.write(PacketWriter.writePacket(
                    ProtocolConstants.PacketTypeAudio,
                    index.toLong() + 3,
                    timestamp,
                    HandshakePayloads.audio(
                        ProtocolConstants.CodecAacLc,
                        index.toLong() + 1,
                        timestamp,
                        21,
                        encoded,
                    ),
                ))
                output.flush()
            }

            val lastAudioSequence = frames.size.toLong() + 2
            val lastTimestamp = audioTimestamp(frames.lastIndex)
            val pingPayload = HandshakePayloads.ping(
                lastAudioSequence,
                lastTimestamp + 1,
            )
            output.write(PacketWriter.writePacket(
                ProtocolConstants.PacketTypePing,
                lastAudioSequence + 1,
                lastTimestamp + 2,
                pingPayload,
            ))
            output.flush()
            val pong = PacketReader.readPacket(input)
            if (PacketParser.parseHeader(pong).packetType !=
                    ProtocolConstants.PacketTypePong ||
                !PacketParser.payload(pong).contentEquals(pingPayload)) return false

            output.write(PacketWriter.writePacket(
                ProtocolConstants.PacketTypeStopStream,
                lastAudioSequence + 2,
                lastTimestamp + 3,
            ))
            output.flush()
            return input.read() == -1
        } catch (_: IOException) {
            return false
        } catch (_: PacketParseException) {
            return false
        }
    }

    private fun audioTimestamp(index: Int): Long =
        AUDIO_BASE_TIMESTAMP + (index.toLong() * 64_000L + 1L) / 3L

    private companion object {
        const val HELLO_TIMESTAMP = 123456000L
        const val START_TIMESTAMP = 123456002L
        const val AUDIO_BASE_TIMESTAMP = 123456003L
        const val MAX_ENCODED_SIZE =
            ProtocolConstants.MaxPacketSize - ProtocolConstants.AudioPayloadHeaderSize
    }
}
```

Keep the existing `readResult` method unchanged.

Temporarily preserve the executable fake path while exposing the source seam:

```kotlin
class TcpHandshakeClient(
    private val audioFrameSupplier: () -> List<ByteArray> = {
        List(3) { FakeAacFrameBytes.clone() }
    },
) {
    fun connect(host: String, port: Int = ProtocolConstants.DefaultPort): Boolean =
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host, port), 10_000)
            socket.soTimeout = 15_000
            HandshakeClient(audioFrameSupplier).run(
                socket.getInputStream(),
                socket.getOutputStream(),
            )
        }
}
```

This temporary default is removed in Task 3; it is not fallback behavior after
Phase 1-R completion.

- [ ] **Step 8: Commit and prove GREEN**

```bash
git add sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt \
  sender-android/app/src/main/java/com/openaudiolink/network/TcpHandshakeClient.kt
git commit -m "feat: add lazy encoded frame source"
```

Push and require exact-head Android success, including all JVM tests and the
existing API 29 encoder tests.

- [ ] **Step 9: Review Task 1**

Specification review confirms post-ready invocation, exact packet table,
65,517-byte preflight, strict peer EOF, and no broad exception catch. Quality
review inspects overflow arithmetic, no partial `AUDIO` writes after preflight,
socket ownership, bounded joins, and the explicitly temporary fake default.

---

### Task 2: Add The Deterministic MediaCodec Test Stream

**Files:**
- Modify: `sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt`
- Create: `sender-android/app/src/main/java/com/openaudiolink/audio/EncodedTestStream.kt`

- [ ] **Step 1: Add the focused API 29 test**

Add:

```kotlin
@Test
fun deterministicEncodedTestStreamProducesFourBoundedCandidates() {
    val frames = encodeDeterministicTestStream()

    assertEquals(4, frames.size)
    frames.forEach { frame ->
        assertTrue(frame.isNotEmpty())
        assertTrue(
            frame.size <=
                ProtocolConstants.MaxPacketSize - ProtocolConstants.AudioPayloadHeaderSize,
        )
    }
}
```

Add `import com.openaudiolink.protocol.ProtocolConstants`.

- [ ] **Step 2: Commit and prove intentional RED**

```bash
git add sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt
git commit -m "test: require deterministic encoded test stream"
```

Push. Require exact-head Android failure because
`encodeDeterministicTestStream` is unresolved; unit and unrelated workflows
must not introduce a second failure.

- [ ] **Step 3: Implement the finite concrete helper**

Create `EncodedTestStream.kt`:

```kotlin
package com.openaudiolink.audio

internal fun encodeDeterministicTestStream(): List<ByteArray> {
    val output = mutableListOf<EncodedAccessUnit>()
    MediaCodecAacEncoder().use { encoder ->
        repeat(INPUT_FRAME_COUNT) { index ->
            output += encoder.submit(pcmFrame(index), inputTimeUs(index))
        }
        output += encoder.drain()
    }
    return output.map { it.bytes }
}

private fun pcmFrame(frameIndex: Int): ByteArray {
    val bytes = ByteArray(PCM_BYTES_PER_FRAME)
    val firstSample = frameIndex * SAMPLES_PER_FRAME
    repeat(SAMPLES_PER_FRAME) { offset ->
        val sample = firstSample + offset
        putPcm16(bytes, offset * BYTES_PER_STEREO_SAMPLE, square(sample, 55, 12_000))
        putPcm16(bytes, offset * BYTES_PER_STEREO_SAMPLE + 2, square(sample, 37, 9_000))
    }
    return bytes
}

private fun square(sample: Int, halfPeriod: Int, amplitude: Int): Short =
    if ((sample / halfPeriod) % 2 == 0) amplitude.toShort() else (-amplitude).toShort()

private fun putPcm16(bytes: ByteArray, offset: Int, sample: Short) {
    val value = sample.toInt()
    bytes[offset] = value.toByte()
    bytes[offset + 1] = (value ushr 8).toByte()
}

private fun inputTimeUs(index: Int): Long =
    (index.toLong() * 64_000L + 1L) / 3L

private const val INPUT_FRAME_COUNT = 3
private const val SAMPLES_PER_FRAME = 1024
private const val BYTES_PER_STEREO_SAMPLE = 4
private const val PCM_BYTES_PER_FRAME =
    SAMPLES_PER_FRAME * BYTES_PER_STEREO_SAMPLE
```

No interface, factory, queue, thread, fixture, or new dependency is added.

- [ ] **Step 4: Commit and prove GREEN**

```bash
git add sender-android/app/src/main/java/com/openaudiolink/audio/EncodedTestStream.kt
git commit -m "feat: encode deterministic Android test stream"
```

Push and require exact-head API 29 success. Confirm the existing standalone
encoder tests still run twice and the Windows standalone artifact oracle stays
green.

- [ ] **Step 5: Review Task 2**

Specification review checks exact integer PCM/PTS formulas, three submits,
drain, four candidates, and close-before-return. Quality review checks signed
PCM little-endian writes, continuous sample index, no redundant codec checks,
and no floating-point or abstraction growth.

---

### Task 3: Replace The Executable Fake AAC Path

**Files:**
- Create: `sender-android/app/src/androidTest/java/com/openaudiolink/network/TcpEncodedTestStreamTest.kt`
- Modify: `sender-android/app/src/main/java/com/openaudiolink/network/TcpHandshakeClient.kt`
- Modify: `sender-android/app/src/main/java/com/openaudiolink/MainActivity.kt`
- Modify: `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`
- Delete: `sender-android/app/src/main/java/com/openaudiolink/network/FakeAacFrame.kt`

- [ ] **Step 1: Add the real executable-path loopback test**

Create `TcpEncodedTestStreamTest.kt`. The core test is:

```kotlin
package com.openaudiolink.network

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.openaudiolink.protocol.AudioPayloadValidator
import com.openaudiolink.protocol.HandshakePayloads
import com.openaudiolink.protocol.PacketParser
import com.openaudiolink.protocol.PacketReader
import com.openaudiolink.protocol.PacketWriter
import com.openaudiolink.protocol.ProtocolConstants
import java.io.File
import java.io.FileOutputStream
import java.io.InputStream
import java.io.OutputStream
import java.net.ServerSocket
import java.util.concurrent.atomic.AtomicReference
import kotlin.concurrent.thread
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class TcpEncodedTestStreamTest {
    @Test
    fun executablePathSendsExactEncodedTestStreamAndExportsWire() {
        val packets = mutableListOf<ByteArray>()
        val failure = AtomicReference<Throwable?>()
        ServerSocket(0).use { listener ->
            listener.soTimeout = SOCKET_TIMEOUT_MS
            val server = thread(isDaemon = true) {
                try {
                    listener.accept().use { client ->
                        client.soTimeout = SOCKET_TIMEOUT_MS
                        val input = client.getInputStream()
                        val output = client.getOutputStream()
                        packets += readType(input, ProtocolConstants.PacketTypeHello)
                        writeWelcome(output)
                        packets += readType(input, ProtocolConstants.PacketTypeStartStream)
                        writeReady(output)
                        repeat(4) {
                            packets += readType(input, ProtocolConstants.PacketTypeAudio)
                        }
                        val ping = readType(input, ProtocolConstants.PacketTypePing)
                        packets += ping
                        output.write(PacketWriter.writePacket(
                            ProtocolConstants.PacketTypePong,
                            7,
                            7,
                            PacketParser.payload(ping),
                        ))
                        packets += readType(input, ProtocolConstants.PacketTypeStopStream)
                    }
                } catch (error: Throwable) {
                    failure.set(error)
                }
            }

            assertTrue(TcpHandshakeClient().connect("127.0.0.1", listener.localPort))
            server.join(JOIN_TIMEOUT_MS)
            assertFalse("server thread timed out", server.isAlive)
            failure.get()?.let { throw AssertionError("server failed", it) }
        }

        assertExactPackets(packets)
        writeArtifact(packets)
    }
}
```

Add complete helpers with these exact behaviors:

```kotlin
private fun readType(input: InputStream, expected: Int): ByteArray =
    PacketReader.readPacket(input).also {
        assertEquals(expected, PacketParser.parseHeader(it).packetType)
    }

private fun writeWelcome(output: OutputStream) {
    output.write(PacketWriter.writePacket(
        ProtocolConstants.PacketTypeWelcome,
        1,
        1,
        HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "receiver", "1.0", 7),
    ))
}

private fun writeReady(output: OutputStream) {
    output.write(PacketWriter.writePacket(
        ProtocolConstants.PacketTypeStreamReady,
        2,
        2,
        HandshakePayloads.streamReady(
            ProtocolConstants.StreamResultSuccess,
            ProtocolConstants.CodecAacLc,
            48000,
            2,
        ),
    ))
}

private fun writeArtifact(packets: List<ByteArray>) {
    val context = InstrumentationRegistry.getInstrumentation().targetContext
    FileOutputStream(File(context.filesDir, ARTIFACT_NAME), false).use { output ->
        packets.forEach { packet -> output.write(packet) }
    }
}
```

`assertExactPackets` must require the full spec table:

```kotlin
private fun assertExactPackets(packets: List<ByteArray>) {
    assertEquals(8, packets.size)
    val types = intArrayOf(
        ProtocolConstants.PacketTypeHello,
        ProtocolConstants.PacketTypeStartStream,
        ProtocolConstants.PacketTypeAudio,
        ProtocolConstants.PacketTypeAudio,
        ProtocolConstants.PacketTypeAudio,
        ProtocolConstants.PacketTypeAudio,
        ProtocolConstants.PacketTypePing,
        ProtocolConstants.PacketTypeStopStream,
    )
    val sequences = longArrayOf(1, 2, 3, 4, 5, 6, 7, 8)
    val timestamps = longArrayOf(
        123456000,
        123456002,
        123456003,
        123477336,
        123498670,
        123520003,
        123520005,
        123520006,
    )
    packets.indices.forEach { index ->
        val header = PacketParser.parseHeader(packets[index])
        assertEquals(types[index], header.packetType)
        assertEquals(sequences[index], header.sequenceNumber)
        assertEquals(timestamps[index], header.timestamp)
    }
    assertArrayEquals(
        HandshakePayloads.hello(
            "Android Phone",
            "1.0.0",
            ProtocolConstants.PlatformAndroid,
            ProtocolConstants.CapabilityAacSupported,
        ),
        PacketParser.payload(packets[0]),
    )
    assertArrayEquals(
        HandshakePayloads.startStream(
            ProtocolConstants.CodecAacLc,
            48000,
            2,
            192000,
            21,
        ),
        PacketParser.payload(packets[1]),
    )
    repeat(4) { index ->
        val payload = PacketParser.payload(packets[index + 2])
        AudioPayloadValidator.validateAacPayload(payload)
        assertEquals(index + 1L, readUInt32(payload, 1))
        assertEquals(timestamps[index + 2], readUInt64(payload, 5))
        assertEquals(21, readUInt16(payload, 13))
        assertTrue(payload.size > ProtocolConstants.AudioPayloadHeaderSize)
    }
    assertArrayEquals(
        HandshakePayloads.ping(6, 123520004),
        PacketParser.payload(packets[6]),
    )
    assertEquals(0, PacketParser.payload(packets[7]).size)
}

private fun readUInt16(bytes: ByteArray, offset: Int): Int =
    ((bytes[offset].toInt() and 0xff) shl 8) or
        (bytes[offset + 1].toInt() and 0xff)

private fun readUInt32(bytes: ByteArray, offset: Int): Long =
    ((bytes[offset].toLong() and 0xff) shl 24) or
        ((bytes[offset + 1].toLong() and 0xff) shl 16) or
        ((bytes[offset + 2].toLong() and 0xff) shl 8) or
        (bytes[offset + 3].toLong() and 0xff)

private fun readUInt64(bytes: ByteArray, offset: Int): Long =
    (readUInt32(bytes, offset) shl 32) or readUInt32(bytes, offset + 4)
```

Keep these readers test-local; do **not** widen protocol production APIs merely
for the test.

Use constants:

```kotlin
private const val SOCKET_TIMEOUT_MS = 15_000
private const val JOIN_TIMEOUT_MS = 30_000L
private const val ARTIFACT_NAME = "mediacodec-runtime-wire.bin"
```

- [ ] **Step 2: Commit and prove intentional runtime RED**

```bash
git add sender-android/app/src/androidTest/java/com/openaudiolink/network/TcpEncodedTestStreamTest.kt
git commit -m "test: require encoded Android TCP stream"
```

Push. Require exact-head API 29 failure because the current default source
still sends only three checked-in fake frames, so the server encounters `PING`
where the fourth `AUDIO` is required.

- [ ] **Step 3: Wire the concrete source and remove fake AAC**

Change `TcpHandshakeClient` to:

```kotlin
import com.openaudiolink.audio.encodeDeterministicTestStream

class TcpHandshakeClient(
    private val audioFrameSupplier: () -> List<ByteArray> =
        ::encodeDeterministicTestStream,
) {
    // connect remains exactly as completed in Task 1
}
```

Delete `FakeAacFrame.kt`. Change only the button text in `MainActivity`:

```kotlin
val connectButton = Button(this).apply { text = "Connect Encoded Test Stream" }
```

Delete the obsolete production-constant identity test from
`HandshakeClientTest`:

```kotlin
@Test
fun fakeAacFrame_matchesCanonicalFixture()
```

The remaining tests already load the canonical bytes through the test-only
`fixtureFrames` helper created in Task 1.

- [ ] **Step 4: Commit and prove GREEN**

```bash
git add sender-android/app/src/main/java/com/openaudiolink/network/TcpHandshakeClient.kt \
  sender-android/app/src/main/java/com/openaudiolink/MainActivity.kt \
  sender-android/app/src/main/java/com/openaudiolink/network/FakeAacFrame.kt \
  sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt
git commit -m "feat: send MediaCodec encoded test stream"
```

Push and require exact-head Android success. Confirm the new instrumentation
test captures four real `AUDIO` packets and creates a non-empty device artifact.
Run this zero-reference gate:

```bash
test -z "$(rg -n 'FakeAacFrameBytes|FakeAacFrame' sender-android/app/src || true)"
```

- [ ] **Step 5: Review Task 3**

Specification review traces `MainActivity -> TcpHandshakeClient -> supplier ->
MediaCodec -> HandshakeClient -> socket` and checks all eight packets. Quality
review checks strict server timeout/join/EOF, failure propagation, no test-only
default, fake-file deletion, and no queue/thread/interface/dependency.

---

### Task 4: Replay The Exact Android Wire Artifact On Windows

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs`
- Modify: `.github/workflows/android.yml`

- [ ] **Step 1: Add the conditional Windows runtime replay test**

Add constants:

```csharp
private const string MediaCodecRuntimeInteropEnabled =
    "OAL_MEDIACODEC_RUNTIME_INTEROP";
private const string MediaCodecRuntimeWirePath =
    "OAL_MEDIACODEC_RUNTIME_WIRE_PATH";
```

Add:

```csharp
[TestMethod]
public void AndroidEncodedTestStreamArtifactRunsThroughReceiverRuntime()
{
    string enabled = Environment.GetEnvironmentVariable(
        MediaCodecRuntimeInteropEnabled);
    if (string.IsNullOrEmpty(enabled))
    {
        return;
    }
    Assert.AreEqual("1", enabled);
    string path = Environment.GetEnvironmentVariable(MediaCodecRuntimeWirePath);
    Assert.IsFalse(string.IsNullOrEmpty(path), "runtime wire path is missing");
    Assert.IsTrue(File.Exists(path), "runtime wire artifact does not exist: " + path);

    IReadOnlyList<byte[]> packets = ReadWirePackets(File.ReadAllBytes(path));
    AssertExactAndroidPackets(packets);

    using (ReceiverRuntime runtime = ReceiverRuntime.StartLoopback())
    using (TcpClient client = Connect(runtime.Port))
    {
        NetworkStream stream = client.GetStream();
        for (int i = 0; i < packets.Count; i++)
        {
            byte type = PacketParser.ParseHeader(packets[i]).PacketType;
            stream.Write(packets[i], 0, packets[i].Length);
            if (type == ProtocolConstants.PacketTypeHello)
            {
                AssertPacket(stream, ProtocolConstants.PacketTypeWelcome,
                    HandshakePayloads.Welcome(
                        ProtocolConstants.ResultSuccess,
                        "Windows PC",
                        "1.0.0",
                        1UL));
            }
            else if (type == ProtocolConstants.PacketTypeStartStream)
            {
                AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady,
                    HandshakePayloads.StreamReady(
                        ProtocolConstants.StreamResultSuccess,
                        ProtocolConstants.CodecAacLc,
                        48000u,
                        2));
            }
            else if (type == ProtocolConstants.PacketTypePing)
            {
                AssertPacket(stream, ProtocolConstants.PacketTypePong,
                    PacketParser.Payload(packets[i]));
            }
        }
        Assert.AreEqual(-1, stream.ReadByte(), "ReceiverRuntime did not close cleanly.");

        Assert.AreEqual(4, runtime.Renderer.RenderedCount);
        Assert.AreEqual(0, runtime.Queue.Count);
        ulong[] timestamps =
        {
            123456003UL, 123477336UL, 123498670UL, 123520003UL,
        };
        long left = 0;
        long right = 0;
        for (int i = 0; i < 4; i++)
        {
            FakePcmFrame frame = runtime.Renderer.RenderedFrames[i];
            Assert.AreEqual((uint)(i + 1), frame.FrameNumber);
            Assert.AreEqual(timestamps[i], frame.CaptureTimestamp);
            Assert.AreEqual((ushort)21, frame.FrameDuration);
            Assert.AreEqual(4096, frame.PcmBytes.Length);
            left += ChannelEnergy(frame.PcmBytes, 0);
            right += ChannelEnergy(frame.PcmBytes, 1);
        }
        Assert.IsTrue(left > 0, "left channel is silent");
        Assert.IsTrue(right > 0, "right channel is silent");
    }
}
```

Add exact helpers:

```csharp
private static IReadOnlyList<byte[]> ReadWirePackets(byte[] bytes)
{
    List<byte[]> packets = new List<byte[]>();
    using (MemoryStream stream = new MemoryStream(bytes, false))
    {
        while (stream.Position < stream.Length)
        {
            packets.Add(PacketReader.ReadPacket(stream));
        }
        Assert.AreEqual(stream.Length, stream.Position);
    }
    Assert.AreEqual(8, packets.Count);
    return packets;
}

private static void AssertExactAndroidPackets(IReadOnlyList<byte[]> packets)
{
    byte[] types =
    {
        ProtocolConstants.PacketTypeHello,
        ProtocolConstants.PacketTypeStartStream,
        ProtocolConstants.PacketTypeAudio,
        ProtocolConstants.PacketTypeAudio,
        ProtocolConstants.PacketTypeAudio,
        ProtocolConstants.PacketTypeAudio,
        ProtocolConstants.PacketTypePing,
        ProtocolConstants.PacketTypeStopStream,
    };
    uint[] sequences = { 1u, 2u, 3u, 4u, 5u, 6u, 7u, 8u };
    ulong[] timestamps =
    {
        123456000UL,
        123456002UL,
        123456003UL,
        123477336UL,
        123498670UL,
        123520003UL,
        123520005UL,
        123520006UL,
    };

    Assert.AreEqual(8, packets.Count);
    for (int i = 0; i < packets.Count; i++)
    {
        PacketHeader header = PacketParser.ParseHeader(packets[i]);
        Assert.AreEqual(types[i], header.PacketType);
        Assert.AreEqual(sequences[i], header.SequenceNumber);
        Assert.AreEqual(timestamps[i], header.Timestamp);
    }

    CollectionAssert.AreEqual(
        HandshakePayloads.Hello(
            "Android Phone",
            "1.0.0",
            ProtocolConstants.PlatformAndroid,
            ProtocolConstants.CapabilityAacSupported),
        PacketParser.Payload(packets[0]));
    CollectionAssert.AreEqual(
        HandshakePayloads.StartStream(
            ProtocolConstants.CodecAacLc,
            48000u,
            2,
            192000u,
            21),
        PacketParser.Payload(packets[1]));

    for (int i = 0; i < 4; i++)
    {
        byte[] payload = PacketParser.Payload(packets[i + 2]);
        AudioPayloadValidator.ValidateAacPayload(payload);
        Assert.AreEqual(ProtocolConstants.CodecAacLc, payload[0]);
        Assert.AreEqual((uint)(i + 1), ReadUInt32(payload, 1));
        Assert.AreEqual(timestamps[i + 2], ReadUInt64(payload, 5));
        Assert.AreEqual((ushort)21, ReadUInt16(payload, 13));
    }

    CollectionAssert.AreEqual(
        HandshakePayloads.Ping(6u, 123520004UL),
        PacketParser.Payload(packets[6]));
    Assert.AreEqual(0, PacketParser.Payload(packets[7]).Length);
}

private static ushort ReadUInt16(byte[] bytes, int offset)
{
    return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
}

private static uint ReadUInt32(byte[] bytes, int offset)
{
    return ((uint)bytes[offset] << 24)
        | ((uint)bytes[offset + 1] << 16)
        | ((uint)bytes[offset + 2] << 8)
        | bytes[offset + 3];
}

private static ulong ReadUInt64(byte[] bytes, int offset)
{
    return ((ulong)ReadUInt32(bytes, offset) << 32)
        | ReadUInt32(bytes, offset + 4);
}
```

Keep these readers test-local; do not widen production parser visibility.

- [ ] **Step 2: Configure the Windows gate before artifact extraction**

In the existing `windows-interop` step, add:

```yaml
OAL_MEDIACODEC_RUNTIME_INTEROP: '1'
OAL_MEDIACODEC_RUNTIME_WIRE_PATH: ${{ github.workspace }}\interop\mediacodec-runtime-wire.bin
```

Add before `dotnet test`:

```powershell
if (!(Test-Path -LiteralPath $env:OAL_MEDIACODEC_RUNTIME_WIRE_PATH)) {
  throw "MediaCodec runtime wire artifact is missing"
}
```

Do not yet change Android extraction or upload paths.

- [ ] **Step 3: Commit and prove intentional interop RED**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs \
  .github/workflows/android.yml
git commit -m "test: require Android wire runtime interop"
```

Push. Require exact-head `android` workflow failure in `windows-interop`
because `mediacodec-runtime-wire.bin` is not yet included in the downloaded
artifact. Require the API 29 test itself to remain green and create the device
file.

- [ ] **Step 4: Export and upload the runtime artifact**

After the existing successful test-status command in the emulator script, add:

```bash
adb shell "run-as com.openaudiolink /system/bin/sh -c 'test -s files/mediacodec-runtime-wire.bin'"
adb exec-out run-as com.openaudiolink cat files/mediacodec-runtime-wire.bin > "$GITHUB_WORKSPACE/mediacodec-runtime-wire.bin"
test -s "$GITHUB_WORKSPACE/mediacodec-runtime-wire.bin"
```

Change upload paths to:

```yaml
path: |
  mediacodec-aac-interop.adts
  mediacodec-runtime-wire.bin
```

Keep artifact name `mediacodec-aac-interop`, retention, the existing ADTS
checks, and the proven line-by-line `android-emulator-runner` script behavior.

- [ ] **Step 5: Commit and prove GREEN**

```bash
git add .github/workflows/android.yml
git commit -m "ci: export Android encoded wire stream"
```

Push and require exact-head Android success. Confirm:

```text
unit             success
media-codec      success
windows-interop  success
```

Inspect Windows output for four rendered 4096-byte frames and non-zero total
stereo energy. Require the normal exact-head `windows` workflow x86/x64 matrix
to remain green.

- [ ] **Step 6: Review Task 4**

Specification review compares the downloaded bytes with the exact Android
artifact path/env and traces all eight packets through `ReceiverRuntime`.
Quality review checks strict EOF (no reset acceptance), artifact EOF, bounded
packet parsing, conditional test skip, no production visibility widening, and
no duplicated ADTS/parser subsystem.

---

### Task 5: Update Active Documentation

**Files:**
- Modify: `docs/04-Android.md`
- Modify: `docs/06-Audio.md`
- Modify: `docs/10-Testing.md`
- Modify: `docs/11-Roadmap.md`

- [ ] **Step 1: Record Android executable runtime truth**

In `docs/04-Android.md`, replace the Phase 1-P-only current-runtime statements
near the encoder and transport sections with:

```text
Phase 1-R connects the concrete MediaCodecAacEncoder to the manual TCP test
stream after successful STREAM_READY. It encodes three deterministic PCM16
frames, retains all four qualified API 29 output candidates, assigns a
synthetic monotonic output-order timeline, and sends the exact raw access units
as AUDIO packets. This is not AudioPlaybackCapture: MediaProjection,
AudioRecord, live pacing, queues and foreground execution remain future work.
```

Keep capture/service architecture explicitly future.

- [ ] **Step 2: Correct audio architecture status**

In `docs/06-Audio.md`, record that Phase 1-R proves a finite synchronous
encoded test stream over TCP. Keep the future capture/encoder queue loop marked
future and do not claim MediaCodec PTS equals capture time.

- [ ] **Step 3: Record the exact runtime gate**

In `docs/10-Testing.md`:

- replace `Connect Fake Stream` with `Connect Encoded Test Stream` in the
  current manual smoke procedure;
- add `Phase 1-R Encoded Test Stream Gate` under Android/interop coverage;
- record exact eight Android packets, four MediaCodec candidates, Windows
  runtime replay, four 4096-byte PCM frames, strict EOF, and x64 artifact scope;
- explicitly exclude playback capture, foreground execution, audible output,
  pacing, latency, hardware/OEM, and real-device claims.

- [ ] **Step 4: Update roadmap status only**

Change the Android current-status cell to:

```text
Phase 1 in progress; MediaCodec encoded test stream over TCP
```

Do not mark Phase 1 or Version 1.0 complete.

- [ ] **Step 5: Run checks and commit**

```bash
python3 tools/check_docs_consistency.py
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check
git add docs/04-Android.md docs/06-Audio.md docs/10-Testing.md docs/11-Roadmap.md
git commit -m "docs: record Android encoded test stream"
```

- [ ] **Step 6: Review Task 5**

Specification review compares every active claim with executable code and CI.
Quality review scans for stale current-runtime `FakeAacFrameBytes`/
`Connect Fake Stream` wording while preserving historical phase documents and
clearly marked future architecture.

---

### Task 6: Review Final Candidate And Run Exact-Head CI

**Files:**
- Review: complete diff from `d160b6b2ffda91f5316949452fc4f9ce6d8d8aa9`

- [ ] **Step 1: Run the complete local gate**

```bash
python3 tools/check_docs_consistency.py
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check d160b6b2ffda91f5316949452fc4f9ce6d8d8aa9..HEAD
test -z "$(git status --porcelain)"
git status --short --branch
```

The local Android build remains unavailable only because the host is aarch64
and AAPT2 is x86-64; do not misreport this as source verification.

- [ ] **Step 2: Run full specification review**

Map every Phase 1-R acceptance criterion to Android production, JVM/API 29
tests, exact wire artifact, Windows runtime oracle, active docs, and CI. Fix and
repeat for every missing or contradictory item.

- [ ] **Step 3: Run full quality review**

Inspect lazy timing, owner thread, preflight-before-write, `use` teardown,
integer PCM/timestamp arithmetic, exact packet bytes, strict EOF, server thread
join/failure transfer, artifact extraction, conditional Windows test, and all
unnecessary scope. Fix and repeat every Critical or Important finding.

- [ ] **Step 4: Push reviewed candidate and require all workflows**

Verify Gitea/GitHub SHA equality, then require exactly one successful push run
at that SHA:

```text
docs     success
windows  success (x86 and x64)
android  success (unit, media-codec, windows-interop)
```

For failure, inspect `gh run view <run-id> -R imshuai/OpenAudioLink
--log-failed`, make one root-cause correction, rerun focused checks, commit,
push, and restart exact-head verification. Never accept an older green SHA.

---

### Task 7: Record Completion, Reverify, Merge, And Continue

**Files:**
- Modify: `docs/superpowers/specs/2026-07-16-phase-1r-android-encoded-test-stream-design.md`

- [ ] **Step 1: Record verified status**

After Task 6 exact-head success, change:

```text
**Status:** Draft for implementation
```

to:

```text
**Status:** Implemented
```

Add one compact evidence paragraph with candidate SHA, `docs`/`windows`/
`android` run IDs, API 29 success, Windows x86/x64 success, and Windows x64
runtime-artifact replay success. Add no capture, audible, latency, or device
claim.

- [ ] **Step 2: Commit, push, and reverify final head**

```bash
git add docs/superpowers/specs/2026-07-16-phase-1r-android-encoded-test-stream-design.md
git commit -m "docs: record phase 1r verification"
```

Run the Task 6 local gate, push, verify source/mirror refs, and require new
successful `docs`, `windows`, and `android` runs whose `head_sha` equals this
status commit.

- [ ] **Step 3: Fast-forward main**

From `/opt/projects/OpenAudioLink`:

```bash
set -e
EXPECTED=$(git -C /root/.config/superpowers/worktrees/OpenAudioLink/phase-1r-android-encoded-test-stream rev-parse HEAD)
LOCAL_MAIN=$(git rev-parse main)
GITEA_MAIN=$(git ls-remote origin refs/heads/main | cut -f1)
test "$LOCAL_MAIN" = "$GITEA_MAIN"
git merge-base --is-ancestor "$GITEA_MAIN" "$EXPECTED"
git switch main
git merge --ff-only phase-1r-android-encoded-test-stream
test "$(git rev-parse HEAD)" = "$EXPECTED"
```

If the local/Gitea equality or ancestry check fails, stop and audit with
`git cherry` before changing refs; preserve every unique patch. After the
fast-forward, push Gitea `main`:

```bash
set -e
ADDED_ROUTE=0
if ip route add 192.168.3.20/32 via 192.168.4.1 dev eth0 mtu 1200 2>/dev/null; then
  ADDED_ROUTE=1
fi
cleanup() {
  if [ "$ADDED_ROUTE" -eq 1 ]; then
    ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true
  fi
}
trap cleanup EXIT
git push origin main
```

- [ ] **Step 4: Verify refs and no duplicate main CI**

Run:

```bash
set -e
EXPECTED=$(git rev-parse main)
LOCAL_MAIN=$(git rev-parse main)
GITEA_MAIN=$(git ls-remote origin refs/heads/main | cut -f1)
GITEA_PHASE=$(git ls-remote origin refs/heads/phase-1r-android-encoded-test-stream | cut -f1)
test "$LOCAL_MAIN" = "$EXPECTED"
test "$GITEA_MAIN" = "$EXPECTED"
test "$GITEA_PHASE" = "$EXPECTED"

for attempt in $(seq 1 60); do
  GITHUB_MAIN=$(gh api repos/imshuai/OpenAudioLink/commits/main --jq .sha 2>/dev/null || true)
  GITHUB_PHASE=$(gh api repos/imshuai/OpenAudioLink/commits/phase-1r-android-encoded-test-stream --jq .sha 2>/dev/null || true)
  [ "$GITHUB_MAIN" = "$EXPECTED" ] && [ "$GITHUB_PHASE" = "$EXPECTED" ] && break
  sleep 5
done
test "$GITHUB_MAIN" = "$EXPECTED"
test "$GITHUB_PHASE" = "$EXPECTED"

sleep 65
MAIN_RUNS=$(gh api 'repos/imshuai/OpenAudioLink/actions/runs?per_page=100' \
  --jq "[.workflow_runs[] | select(.head_sha == \"$EXPECTED\" and .head_branch == \"main\")] | length")
test "$MAIN_RUNS" -eq 0
printf 'local=%s gitea-main=%s github-main=%s gitea-phase=%s github-phase=%s main-runs=%s\n' \
  "$LOCAL_MAIN" "$GITEA_MAIN" "$GITHUB_MAIN" "$GITEA_PHASE" "$GITHUB_PHASE" "$MAIN_RUNS"
```

- [ ] **Step 5: Clean local phase state and continue the roadmap**

From `/opt/projects/OpenAudioLink`, remove only owned Phase 1-R local state:

```bash
set -e
WT=/root/.config/superpowers/worktrees/OpenAudioLink/phase-1r-android-encoded-test-stream
test -z "$(git -C "$WT" status --porcelain)"
test "$(git -C "$WT" rev-parse HEAD)" = "$(git rev-parse main)"
git worktree remove "$WT"
git worktree prune
git branch -d phase-1r-android-encoded-test-stream
test ! -e "$WT"
! git show-ref --verify --quiet refs/heads/phase-1r-android-encoded-test-stream
git status --short --branch
```

Retain the remote phase branch as CI evidence. Report the final SHA, run IDs,
decisive Android/Windows evidence, five refs, no-main-CI result, and cleanup.
Then return to the Roadmap to design the next smallest Phase 1 capability; do
not mark the overall project goal complete while capture, audible playback,
discovery, or other required Version 1 work remains.
