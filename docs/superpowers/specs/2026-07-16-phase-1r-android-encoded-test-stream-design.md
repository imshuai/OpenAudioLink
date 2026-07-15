# Phase 1-R Android Encoded Test Stream Design

**Status:** Draft for implementation

**Date:** 2026-07-16

**Scope:** Replace the Android executable path's checked-in fake AAC bytes with
a short deterministic PCM stream encoded by the existing platform
`MediaCodecAacEncoder`, then prove those exact Android wire packets through the
existing Windows TCP decoder runtime and fake renderer.

---

## Goal

Phase 1-R connects the completed Android encoder boundary from Phase 1-P to the
completed TCP and Windows decoder runtime:

```text
MainActivity connection thread
    -> deterministic PCM16 stereo
    -> MediaCodecAacEncoder
    -> raw AAC candidates
    -> HandshakeClient AUDIO packets
    -> TCP
    -> ReceiverRuntime
    -> MediaFoundationAacDecoder
    -> 4096-byte PCM frames
    -> FakeAudioRenderer
```

This is an encoded **test stream**, not live playback capture. It removes
`FakeAacFrameBytes` from the Android executable path without adding
`MediaProjection`, `AudioRecord`, a foreground service, queues, pacing, or
audible Windows output.

---

## Current Runtime Truth

At Phase 1-Q completion, Android still executes:

```text
MainActivity
    -> ManualConnectController
    -> TcpHandshakeClient
    -> HandshakeClient
    -> three copies of FakeAacFrameBytes
    -> TCP
```

`MediaCodecAacEncoder` is production code but is called only by Android
instrumentation tests. Windows already receives canonical `AUDIO` packets,
decodes real AAC with Media Foundation, assembles exact 4096-byte PCM frames,
and records them in `FakeAudioRenderer`.

The API 29 CI codec produces `N + 1` completed AAC candidates for `N` input
frames. For twelve inputs its observed PTS order is:

```text
0, 21333, ..., 234667, 0
```

The final codec-added candidate is retained, but its audio-content and clock
meaning are intentionally unknown. Phase 1-R must not infer that it is safe to
drop or assign its PTS to the wire capture clock.

---

## Selected Design

### Encode after `STREAM_READY`

`HandshakeClient` receives one lazy frame supplier:

```kotlin
class HandshakeClient(
    private val audioFrameSupplier: () -> List<ByteArray>,
)
```

The supplier is invoked exactly once, only after a successful `WELCOME` and a
successful `STREAM_READY`. A busy receiver, protocol rejection, or unsupported
codec response performs no native encoder work.

`TcpHandshakeClient` defaults to the real encoded test stream supplier and
passes it to `HandshakeClient`. Its constructor accepts the same function type
only so a JVM loopback test can force a producer failure; there is no interface,
factory, or production default that can silently fall back to fake AAC. JVM
protocol tests supply checked-in fixture bytes explicitly.

### Keep one owner thread

The existing `MainActivity` connection `Thread` owns:

```text
Socket connect
MediaCodec create / submit / drain / close
Packet writes and response reads
Socket close
```

No coroutine, `Handler`, executor, callback codec mode, or additional worker is
introduced. `MediaCodecAacEncoder` remains synchronous and owner-thread-only.

The supplier fully encodes and closes the four-frame result before the first
`AUDIO` write. Holding at most four bounded access units is smaller than adding
a queue or streaming producer protocol for a finite test stream.

### Generate deterministic non-silent PCM

A small Android production helper creates three PCM frames with the fixed
Version 1 format:

| Property | Value |
|----------|-------|
| Sample rate | 48,000 Hz |
| Channels | 2 |
| Encoding | Signed PCM16 little-endian |
| Samples per channel per input frame | 1024 |
| Bytes per input frame | 4096 |

For zero-based continuous sample index `n`, each sample is generated with:

```text
left(n)  = ((n / 55) % 2 == 0) ? +12000 : -12000
right(n) = ((n / 37) % 2 == 0) ?  +9000 :  -9000
```

Division is integer division and `n` is continuous across all three input
frames. This fixes amplitude, phase, and half-period and gives deterministic
non-zero stereo energy without a checked-in PCM fixture or floating-point
signal generator.

Encoder input PTS uses the sample-count formula
`(index * 64,000 + 1) / 3` with integer division for input indices `0..2`.
These values satisfy the wrapper's strictly increasing input contract but do
not become wire timestamps for individual codec outputs.

Three calls to `submit` plus `drain` retain all four candidates required by the
qualified API 29 codec. `MediaCodecAacEncoder` already rejects batching, loss,
non-canonical codec config, encoder-buffer sizes above 65,536 bytes, timeouts,
and any result count other than `N + 1`; Phase 1-R does not duplicate those
encoder checks.

The wire boundary is smaller because the 19-byte `AUDIO` metadata header is
part of the protocol payload. After the supplier returns and before writing any
`AUDIO`, `HandshakeClient` requires a non-empty result and preflights every
candidate:

```text
1 <= Encoded Size <= MaxPacketSize - AudioPayloadHeaderSize
1 <= Encoded Size <= 65,517
```

An invalid candidate aborts before the first `AUDIO` packet and follows the
same socket-close/controller-`Failed` path as a native producer failure.

### Assign wire metadata from output order

MediaCodec output PTS remains diagnostic only. The four candidates are sent in
the exact order returned by `submit` and `drain`, with synthetic test-stream
metadata:

```text
Frame Number:       1, 2, 3, 4
Capture Timestamp:  base + (index * 64,000 + 1) / 3 using integer division
Frame Duration:     21 ms
```

With the existing base `123456003`, timestamps are:

```text
123456003
123477336
123498670
123520003
```

The same capture timestamp appears in the common packet header and `AUDIO`
payload. This is a synthetic, monotonic test timeline; it is not claimed to be
an Android capture timestamp or a mapping from codec output PTS.

The complete outbound oracle is fixed:

| Packet | Sequence | Header Timestamp | Payload Summary |
|--------|---------:|-----------------:|-----------------|
| `HELLO` | 1 | 123456000 | Existing canonical hello |
| `START_STREAM` | 2 | 123456002 | AAC-LC, 48 kHz, stereo, 192 kbps, 21 ms |
| `AUDIO` | 3 | 123456003 | Frame 1, capture 123456003 |
| `AUDIO` | 4 | 123477336 | Frame 2, capture 123477336 |
| `AUDIO` | 5 | 123498670 | Frame 3, capture 123498670 |
| `AUDIO` | 6 | 123520003 | Frame 4, capture 123520003 |
| `PING` | 7 | 123520005 | Payload sequence 6, timestamp 123520004 |
| `STOP_STREAM` | 8 | 123520006 | Empty payload |

Thus `PING` acknowledges the last `AUDIO` packet and retains exact payload
echo verification. `STOP_STREAM` still requires receiver EOF as the clean
completion barrier.

### Preserve the protocol/network boundary

`HandshakeClient` remains Android-framework-free. It owns handshake ordering,
wire metadata, packet writes, `PONG` validation, and clean stop. It does not
know about `MediaCodec`, PCM generation, sockets, activities, or services.

`TcpHandshakeClient` remains the concrete socket entry point and wires the
Android-only supplier to `HandshakeClient`. No encoder interface, transport
interface, factory, DI container, session manager, or configuration object is
added.

The button label changes from `Connect Fake Stream` to
`Connect Encoded Test Stream`. Existing `Connecting`, `Success`, and `Failed`
status behavior remains unchanged.

---

## Error And Teardown Behavior

### Native encoder failure

The helper uses `MediaCodecAacEncoder().use { ... }`, preserving the existing
close and suppressed-failure behavior. If create, submit, drain, or close
fails:

1. the failure propagates out of the lazy supplier;
2. `Socket.use` closes the TCP connection;
3. the Windows receiver ends only that stream and releases its decoder;
4. `ManualConnectController` converts the exception to `Failed`.

No `PING` or `STOP_STREAM` is fabricated after producer or preflight failure.
No retry, fallback to `FakeAacFrameBytes`, partial-send recovery, or live
encoder recreation is added.

### Network and protocol failure

Existing behavior remains:

- `IOException` or `PacketParseException` returns `false` from
  `HandshakeClient.run`.
- Busy or rejected handshake results return `false`.
- A mismatched `PONG`, bytes after stop, timeout, or missing EOF returns
  `false`.
- `Socket.use` closes every exit path.

Programming errors and native producer failures are not broadly swallowed in
`HandshakeClient`; the existing controller boundary reports them as `Failed`.

---

## Test And Artifact Design

### JVM protocol tests

`HandshakeClientTest` supplies three canonical fixture frames explicitly and
retains the existing exact packet oracle. Additional focused tests prove:

- the supplier runs once after successful `STREAM_READY`;
- it is not called for busy `WELCOME`, protocol rejection, or failed
  `STREAM_READY`;
- a four-frame supplier produces sequences `3..6`, `PING` sequence `7`, and
  `STOP_STREAM` sequence `8` with the complete exact packet table above;
- an empty result, empty candidate, or larger-than-65,517-byte candidate fails
  before any `AUDIO` write;
- a supplier exception is not misreported as successful protocol completion.

The fixture is test data only. `FakeAacFrame.kt` is deleted from production.

### API 29 executable-path test

A new instrumentation test starts a bounded loopback `ServerSocket`, then calls
the real:

```text
TcpHandshakeClient.connect("127.0.0.1", port)
```

The server worker exclusively owns the listening and accepted sockets. Both
accept and read operations have a 15-second timeout. The test thread owns the
client call, waits for the worker with a bounded 30-second join, and rethrows a
worker failure stored in an `AtomicReference<Throwable?>`.

The server drives `WELCOME`, `STREAM_READY`, and `PONG`, captures the exact
sender packets, and closes the accepted socket only after reading the complete
`STOP_STREAM`. The client must observe `read() == -1`; reset, timeout, or any
other exception is failure. The test requires:

- one `HELLO` and one `START_STREAM` before native encoding;
- four non-empty `AUDIO` packets from the qualified codec;
- exact frame numbers, synthetic timestamps, duration, codec, and sequence
  numbers;
- `PING` payload echo and one `STOP_STREAM`;
- `TcpHandshakeClient.connect` returns `true`;
- bounded thread/socket completion with no timeout accepted as EOF.

A second JVM loopback test injects a supplier that records its thread and then
throws after `STREAM_READY`. It runs through `ManualConnectController` and the
real `TcpHandshakeClient`, requiring:

- the supplier runs on the caller/connection thread;
- the controller returns `Failed`;
- the peer reads strict EOF;
- no `AUDIO`, `PING`, or `STOP_STREAM` arrives.

This directly verifies the synchronous call stack and `Socket.use` teardown
without invoking Android `MediaCodec` on the JVM.

The test writes the exact eight outbound packets, concatenated in arrival
order, to:

```text
mediacodec-runtime-wire.bin
```

This artifact contains no ADTS wrapper and no test reconstruction of Android
packet bytes. Its host and device paths are:

```text
Device: <targetContext.filesDir>/mediacodec-runtime-wire.bin
Host:   $GITHUB_WORKSPACE/mediacodec-runtime-wire.bin
```

### Windows runtime oracle

The proven `android-emulator-runner` behavior executes each newline in its
`script` input as a separate `sh -c` command. The existing Gradle/logcat command
continues to return the captured test status; artifact extraction commands run
only after a successful test command. On success, `run-as` verifies and exports
both files before `actions/upload-artifact` runs:

```text
$GITHUB_WORKSPACE/mediacodec-aac-interop.adts
$GITHUB_WORKSPACE/mediacodec-runtime-wire.bin
```

Both files remain in artifact `mediacodec-aac-interop`, downloaded to
`interop/` by the dependent `windows-2022` x64 job. Existing standalone env
variables remain unchanged; runtime replay adds:

```text
OAL_MEDIACODEC_RUNTIME_INTEROP=1
OAL_MEDIACODEC_RUNTIME_WIRE_PATH=<workspace>\interop\mediacodec-runtime-wire.bin
```

The Windows test reads the packet stream with the existing `PacketReader`, then
replays the exact sender packets against a real `ReceiverRuntime` loopback
connection while observing the required response barriers.

The Windows test requires:

- exactly eight packets in canonical order;
- four `AUDIO` access units accepted by `ReceiverRuntime`;
- successful `WELCOME`, `STREAM_READY`, and `PING`/`PONG`;
- strict `ReadByte() == -1` after the artifact's `STOP_STREAM`; reset,
  `IOException`, and timeout all fail this new gate;
- four rendered frames in exact metadata order;
- every rendered frame is exactly 4096-byte PCM16 stereo;
- total left and right channel energy are both non-zero;
- `AudioFrameQueue.Count == 0` after final drain.

The existing standalone MediaCodec-to-Media-Foundation artifact gate remains
unchanged. The normal Windows x86/x64 workflow still runs all receiver tests;
the Android-dependent artifact replay is x64 because it is a cross-workflow
interop gate, not a Windows architecture claim.

---

## Files And Responsibilities

Create:

- `sender-android/app/src/main/java/com/openaudiolink/audio/EncodedTestStream.kt`
  — deterministic PCM generation and one finite encoder lifecycle.
- `sender-android/app/src/androidTest/java/com/openaudiolink/network/TcpEncodedTestStreamTest.kt`
  — real Android socket/encoder/wire capture and artifact creation.

Modify:

- `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`
  — lazy frame supplier, dynamic metadata, sequence, ping, and stop numbers.
- `sender-android/app/src/main/java/com/openaudiolink/network/TcpHandshakeClient.kt`
  — compose the real encoded test stream supplier.
- `sender-android/app/src/main/java/com/openaudiolink/MainActivity.kt`
  — accurate button label only.
- `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`
  — explicit fixture supplier and new lifecycle/sequence cases.
- `sender-android/app/src/test/java/com/openaudiolink/network/TcpHandshakeClientTest.kt`
  — owner-thread producer failure, strict peer EOF, and controller status.
- `.github/workflows/android.yml`
  — export/download the exact runtime wire artifact and enable its Windows
  oracle.
- `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs`
  — conditional exact Android wire replay through the real runtime.
- `docs/04-Android.md` — update the encoder/transport current-runtime notes;
  preserve capture/service sections as future design.
- `docs/06-Audio.md` — record the finite encoded test-stream seam without
  claiming live capture, pacing, or sender queues.
- `docs/10-Testing.md` — replace the `Connect Fake Stream` smoke-test wording
  and add the Android exact-wire/Windows runtime artifact gate.
- `docs/11-Roadmap.md` — update only the Android Sender current-status cell;
  leave Phase 1 and Version 1.0 incomplete.
- This design and its implementation plan/status.

Delete:

- `sender-android/app/src/main/java/com/openaudiolink/network/FakeAacFrame.kt`
  — no executable production path retains checked-in AAC bytes.

No protocol production source, Windows production source, manifest, Gradle
dependency, layout resource, checked-in binary fixture, or normal Windows
workflow change is required.

---

## Rejected Alternatives

### Add playback capture now

`MediaProjection`, consent UI, `AudioPlaybackCaptureConfiguration`,
`AudioRecord`, PCM assembly, capture clock ownership, permission revocation,
and foreground execution are independent platform boundaries. Adding them now
would make encoder, capture, and transport failures indistinguishable. They
remain the next Android product-data phase after this executable codec seam.

### Add Windows audible rendering now

Hosted Windows CI has no reliable speaker endpoint. WASAPI or `waveOut` also
introduces device selection, buffering, playback timing, and shutdown policy.
Phase 1-R first removes the fake Android codec path while retaining the
deterministic Windows fake renderer.

### Drop the codec-added candidate

The qualified codec returns a fourth candidate with PTS `0`, but Phase 1-P did
not prove its content semantics. Dropping it would convert an observation into
an unsupported codec assumption. Phase 1-R retains every completed candidate
and assigns a synthetic output-order timeline.

### Add a streaming queue or service

Four bounded access units need neither backpressure nor long-lived process
ownership. A queue, coroutine, foreground service, cancellation protocol, or
session abstraction would be unused scaffolding until live capture exists.

### Store ADTS for runtime interop

ADTS would preserve only access-unit boundaries. Capturing the exact sender
packet stream proves packetization, sequence numbers, timestamps, and payload
bytes with less reconstruction in the Windows oracle.

---

## Non-Goals

Phase 1-R must not add or claim:

- `MediaProjection`, playback-capture permission, `AudioRecord`, or live PCM.
- Foreground service, notification, process recreation, ViewModel, or settings.
- Continuous streaming, pacing, queue capacity, overflow, backpressure,
  cancellation, reconnect policy, or network-change handling.
- Android capture-clock semantics or equivalence with MediaCodec PTS.
- Hardware encoder selection, OEM/device coverage, power, latency, endurance,
  or real-device qualification.
- Windows audible playback, WASAPI, WaveOut, PCM queue, device selection, or
  playback clock.
- Discovery, mDNS, UDP broadcast, installer, logging, or protocol changes.
- New dependencies, interfaces, factories, DI, or configuration objects.
- A Phase 1 or Version 1.0 completion claim.

---

## Local And CI Gates

The local host is `aarch64`, while the installed Android SDK and Gradle AAPT2
are x86-64. Local documentation, fixture, and golden checks remain available;
Android JVM/instrumentation compilation and execution are authoritative on the
existing x86-64 GitHub runners.

Every intentional Android RED and GREEN uses an exact pushed phase-branch SHA.
The final phase head requires exactly one successful push run for each existing
workflow:

```text
docs
windows  (x86 and x64)
android  (unit, API 29 MediaCodec/runtime, Windows x64 interop)
```

The Gitea phase ref and GitHub mirror ref must equal the tested SHA. After the
phase branch is fast-forwarded to `main`, all local/source/mirror refs must
agree, and the existing workflow triggers must produce no duplicate `main`
runs.

---

## Acceptance Criteria

Phase 1-R is complete only when:

- `TcpHandshakeClient` invokes the real `MediaCodecAacEncoder` path after a
  successful `STREAM_READY`.
- Android executable production code no longer references
  `FakeAacFrameBytes`, and `FakeAacFrame.kt` is deleted.
- Three deterministic PCM inputs produce and retain four qualified codec
  candidates on API 29.
- All encoder operations and socket operations remain on the existing
  connection owner thread, with encoder close before `AUDIO` writes.
- The supplied list is non-empty, and every candidate is preflighted as
  non-empty and no larger than 65,517 bytes before the first `AUDIO` write.
- Four candidates are packetized in output order with exact monotonic synthetic
  metadata and the complete fixed eight-packet oracle.
- Rejected handshakes perform no encoder work; encoder failure closes the
  socket, gives the peer strict EOF, sends no media/control tail, and reports
  `Failed` without fake fallback.
- The real Android TCP composition test captures exactly eight outbound packets
  with bounded socket/thread ownership and exports their exact bytes.
- The Windows x64 interop gate replays those Android packets through
  `ReceiverRuntime` and records four ordered 4096-byte PCM frames with non-zero
  total energy in both channels, followed by strict clean EOF.
- Existing standalone encoder/decoder, protocol, Android JVM, Windows x86/x64,
  fixture, golden, and documentation gates remain green.
- Active docs call this an encoded test stream and do not claim capture,
  foreground execution, live pacing, audible playback, latency, hardware/OEM
  support, Phase 1 completion, or Version 1.0 completion.
- Exact-head phase CI is green, the phase branch is fast-forwarded to `main`,
  source and mirror refs agree, and `main` push creates no duplicate workflows.
