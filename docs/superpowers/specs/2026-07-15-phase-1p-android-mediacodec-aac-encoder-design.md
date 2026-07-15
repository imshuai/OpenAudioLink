# Phase 1-P Android MediaCodec AAC Encoder Design

**Status:** Draft for implementation

**Date:** 2026-07-15

**Scope:** Prove Android platform AAC-LC encoding and direct Android-to-Windows codec compatibility without connecting the encoder to capture, networking, or the current fake sender runtime.

---

## Goal

Phase 1-P implements the second follow-up named by the frozen Phase 1-N wire contract:

1. Phase 1-O proved that Windows Media Foundation decodes the canonical raw AAC-LC stream.
2. Phase 1-P proves that Android `MediaCodec` produces raw AAC-LC access units accepted by that Windows decoder.

The phase adds one fixed-format standalone `MediaCodecAacEncoder`. A real Android API 29 emulator encodes deterministic PCM, and an x64 Windows CI job decodes the generated access units with the existing `MediaFoundationAacDecoder`.

This is a codec-boundary proof, not sender runtime integration. `HandshakeClient` continues to send the checked-in fake AAC frame.

---

## Current Runtime Truth

The executable Android path is still:

```text
MainActivity

↓

ManualConnectController

↓

TcpHandshakeClient

↓

HandshakeClient

↓

FakeAacFrameBytes

↓

PacketWriter / Socket OutputStream
```

There is no production `MediaCodec`, `AudioRecord`, playback-capture, foreground-service, PCM queue, AAC queue, or live pacing path. Phase 1-P must not make active docs claim otherwise.

---

## Decisions

### Use a standalone production wrapper

Phase 1-P adds a real production component under `com.openaudiolink.audio`, but does not compose it into `MainActivity`, `HandshakeClient`, or any session object.

This mirrors Phase 1-O: prove the native boundary first, then integrate it with threads, queues, capture, and transport in later phases.

### Use synchronous `MediaCodec`

The wrapper uses the synchronous buffer API:

```text
dequeueInputBuffer
getInputBuffer
queueInputBuffer
dequeueOutputBuffer
getOutputBuffer
releaseOutputBuffer
```

No callback thread, coroutine, `Handler`, or custom executor is introduced. The creating thread owns the codec and every public operation.

### Reuse the Windows decoder as the decisive oracle

The Android emulator emits raw access units. Test code temporarily adds ADTS headers only to preserve frame boundaries while transferring one artifact between CI jobs. The Windows job removes those headers and submits the original raw access units to `MediaFoundationAacDecoder`.

This directly proves the Version 1 Android-to-Windows codec boundary and avoids adding a second test-only Android decoder.

### Accept software codec execution in CI

An API 29 emulator may expose only a software AAC encoder. Passing CI proves Android `MediaCodec` behavior and cross-platform wire compatibility. It does not prove hardware acceleration, OEM codec behavior, power consumption, minimum-device support, or production latency.

Hardware selection and real-device coverage remain release gates.

### Reject multi-access-unit output buffers

Android API 29 permits one compressed audio output buffer to contain multiple
access units, but its synchronous `BufferInfo` exposes no boundaries inside
that buffer. Version 1 cannot safely packetize such output without a raw AAC
parser.

Phase 1-P therefore defines a strict compatibility gate rather than assuming
all codecs behave alike. After twelve exact PCM input frames and output EOS,
the selected codec must have produced exactly twelve complete non-config
output buffers. Each returned buffer must also decode independently on Windows
to exactly one 1024-sample stereo PCM frame. Any codec that batches, drops, or
adds access units is unsupported and fails the phase instead of being
mispacketized.

This restriction proves only the API 29 emulator codec selected in CI. The
standalone wrapper is not connected to the sender runtime, and later device
qualification must run the same boundary gate before another codec can be
enabled.

---

## Rejected Alternatives

### Test-only direct `MediaCodec` use

A test-only encoder would prove platform availability but leave no production
component for the next playback-capture phase. The fixed wrapper is small and
reusable after a target codec passes the same access-unit boundary gate; this
phase does not yet connect it to streaming runtime output.

### Immediate fake-runtime replacement

Replacing `FakeAacFrameBytes` also requires PCM ownership, pacing, queues, socket backpressure, session cancellation, and thread lifetime. Mixing those concerns would make a codec failure indistinguishable from a runtime-integration failure.

### Android encode/decode round trip

An Android decoder round trip would prove one platform can consume its own output. Reusing the completed Windows decoder provides the stronger product-relevant result with less duplicate native wrapper code.

### Checked-in MediaCodec AAC golden bytes

Different valid encoders may emit different AAC bytes for the same PCM. Phase 1-P validates structure, configuration, timestamps, decode length, and channel energy; it does not freeze an encoder-specific hash.

### Custom encoder interfaces or factories

There is one fixed Version 1 implementation and no runtime composition in this phase. `IAudioEncoder`, factories, DI, codec-selection policy, and user-configurable format objects would be unused scaffolding.

### Raw AAC access-unit parser

Parsing every AAC raw-data-block element merely to recover boundaries from a
batched platform buffer would add a second codec-sized subsystem. Phase 1-P
instead rejects codecs that need such splitting. A boundary-aware platform API
or parser belongs in a later phase only if the release device matrix proves it
necessary.

---

## Scope

### In scope

- One fixed-format synchronous `MediaCodecAacEncoder`.
- AAC-LC, 48 kHz, stereo, signed PCM16 input, and 192 kbps target bitrate.
- Exactly one 1024-sample-per-channel PCM frame per `submit` call.
- Caller-owned presentation timestamps.
- Exact `AudioSpecificConfig = 11 90` validation.
- Zero, one, or many output access units per `submit`.
- Exactly one completed output buffer per submitted PCM frame across a full
  encode-and-drain cycle; batching, loss, or added output is a compatibility
  failure.
- Explicit input EOS and output drain.
- Owner-thread, state, timeout, and deterministic cleanup behavior.
- JVM tests for the exact codec-config validator.
- Android instrumentation tests against a real emulator codec.
- Test-only ADTS artifact creation and validation.
- Independent and continuous Windows Media Foundation decode of every
  generated access unit.
- Android workflow unit, emulator, and Windows interop jobs.
- Focused corrections to active Android, testing, and roadmap documentation.
- Status corrections for completed Phase 1-N and Phase 1-O design specs.

### Out of scope

- `MediaProjection` permission or callbacks.
- `AudioPlaybackCaptureConfiguration`.
- `AudioRecord` creation, reading, or PCM frame assembly.
- Capture timestamp generation or clock reconciliation.
- PCM/AAC queues, capacity, overflow, or backpressure policy.
- Foreground service, notification, process recreation, or UI changes.
- Coroutine, worker-thread, `Handler`, or executor ownership.
- `HandshakeClient`, `TcpHandshakeClient`, packet, protocol, heartbeat, or reconnect changes.
- Replacing `FakeAacFrameBytes`.
- Windows receiver runtime or renderer integration.
- Audible playback, latency, performance, or endurance claims.
- Hardware encoder selection or requirement.
- Bitrate presets or user configuration.
- Real-device or OEM codec matrices.
- Raw AAC syntax parsing or splitting a batched output buffer.
- New production dependencies, encoder interfaces, factories, or DI.

---

## Files And Responsibilities

Create:

- `sender-android/app/src/main/java/com/openaudiolink/audio/MediaCodecAacEncoder.kt` — fixed native encoder, access-unit value, state, and exact ASC validator.
- `sender-android/app/src/test/java/com/openaudiolink/audio/MediaCodecAacEncoderContractTest.kt` — JVM checks for exact `11 90` acceptance and rejection.
- `sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt` — native encode, lifecycle, timestamp, artifact, and misuse tests.

Modify:

- `sender-android/gradle.properties` — enable AndroidX for the existing AndroidX instrumentation runner and test-only dependencies.
- `sender-android/app/build.gradle.kts` — minimum AndroidX instrumentation dependencies only.
- `.github/workflows/android.yml` — unit, API 29 emulator, and Windows interop jobs.
- `receiver-windows/tests/OpenAudioLink.Tests/Receiver/MediaFoundationAacDecoderTests.cs` — optional CI-artifact decode test using the existing decoder and ADTS parser.
- `docs/04-Android.md` — standalone status, correct buffering/lifecycle, and no hardware guarantee.
- `docs/06-Audio.md` — future decoder-loop pseudocode preserves zero/one/many output and explicit drain.
- `docs/10-Testing.md` — native emulator and cross-platform oracle.
- `docs/11-Roadmap.md` — current implementation status.
- `docs/superpowers/specs/2026-07-14-phase-1n-aac-lc-wire-contract-design.md` — implemented status and correction that fake-frame replacement belongs to runtime integration.
- `docs/superpowers/specs/2026-07-15-phase-1o-windows-media-foundation-aac-decoder-design.md` — implemented status.
- This Phase 1-P spec — implemented status only after every acceptance gate passes.

No protocol source, production Windows source, Android fake sender, UI, manifest permission, or checked-in binary fixture changes are required.

---

## Public Surface

The complete new production surface is:

```kotlin
class EncodedAccessUnit(
    val bytes: ByteArray,
    val presentationTimeUs: Long,
)

class MediaCodecAacEncoder : Closeable {
    val codecName: String

    fun submit(
        pcm: ByteArray,
        presentationTimeUs: Long,
    ): List<EncodedAccessUnit>

    fun drain(): List<EncodedAccessUnit>

    override fun close()
}
```

Both declarations live in `MediaCodecAacEncoder.kt`. There is no general encoder interface or mutable configuration object.

`codecName` exists only for reproducible diagnostics and test logs. It is not a hardware-support promise.

---

## Fixed Media Format

Construction uses:

```text
MIME                         audio/mp4a-latm
KEY_AAC_PROFILE              AACObjectLC
KEY_SAMPLE_RATE              48000
KEY_CHANNEL_COUNT            2
KEY_BIT_RATE                 192000
KEY_PCM_ENCODING             ENCODING_PCM_16BIT
CONFIGURE_FLAG_ENCODE        set
```

Constants are local to the implementation:

```text
samples per channel/frame    1024
channels                     2
bytes/sample                 2
PCM bytes/frame              4096
canonical ASC                11 90
maximum assembled raw AU     65,536 bytes
dequeue poll                 10,000 microseconds
operation deadline           5 seconds
```

`KEY_MAX_INPUT_SIZE` is not used as a frame-boundary declaration. The wrapper
checks the actual dequeued input buffer before copying all 4096 bytes.

The implementation calls `MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC)`. It does not enumerate, rank, or require a hardware codec.

Construction order is:

```text
record owner thread
create encoder
record codec name
create fixed MediaFormat
configure for encode
start
validate actual input format
enter Active state
```

After `start`, `getInputFormat()` must report 48 kHz, two channels, and PCM16.
`KEY_PCM_ENCODING` may be absent because Android defines PCM16 as the default;
an explicit different value is rejected. This prevents a codec that ignored the
requested PCM encoding from interpreting the submitted bytes as another raw
format.

If configuration or startup fails, every acquired codec is released. A codec that started is stopped before release when possible. Missing or unusable AAC encoding is a failed native proof, not a skipped test.

---

## PCM Input Contract

Every `submit` accepts exactly one complete frame:

```text
1024 samples/channel
2 channels
signed PCM16 little-endian
4096 bytes
```

Frame assembly from arbitrarily sized `AudioRecord` reads belongs to the later capture phase.

The caller supplies `presentationTimeUs`. The first value must be non-negative and later values must be strictly increasing. The encoder does not invent capture time or accumulate nominal `21 ms` wire metadata.

The native instrumentation test supplies zero-based sample-derived timestamps:

```text
round(frameIndex * 1024 * 1,000,000 / 48,000)
= round(frameIndex * 64,000 / 3)
```

The resulting deltas repeat `21,333`, `21,334`, `21,333` microseconds.

Argument validation happens before native mutation. Wrong frame size or timestamp order throws `IllegalArgumentException` without faulting an otherwise active encoder.

---

## Input Submission

`submit` performs:

1. Owner-thread and state validation.
2. PCM length and presentation-time validation.
3. Input-buffer acquisition under one five-second monotonic operation deadline.
4. Output collection while the codec temporarily has no input buffer.
5. Exact PCM copy into a cleared input buffer.
6. `queueInputBuffer` with the caller timestamp and no flags.
7. Collection of every output currently available without waiting for future input.

`dequeueInputBuffer(10_000)` provides bounded blocking. The implementation never busy-spins. If no input buffer becomes available before the operation deadline, the encoder faults.

The same deadline is checked before and after every input/output dequeue and
therefore covers the platform call itself, continuous output, repeated format
changes, and buffer-change notifications. A buffer returned after the deadline
is rejected rather than accepted as a late success; receiving output never
resets or bypasses the deadline.

The input index and timestamp are committed only after `queueInputBuffer` succeeds.

---

## Codec Configuration Validation

The required MPEG-4 `AudioSpecificConfig` is exactly:

```text
11 90
```

The encoder accepts configuration from either platform representation:

- output-format `csd-0`; or
- an output buffer carrying `BUFFER_FLAG_CODEC_CONFIG`.

Every non-empty representation encountered must equal exactly two bytes `11 90`. A wrong length or byte value faults the encoder.

An empty codec-config buffer does not count as validation. Codec-config buffers are always released and never returned as audio.

No audio access unit may be exposed before at least one exact configuration representation has been validated. If audio arrives first, the encoder faults before returning it.

On `INFO_OUTPUT_FORMAT_CHANGED`, the implementation requires:

```text
MIME            audio/mp4a-latm
sample rate     48000
channel count   2
```

If `csd-0` is present, it is validated immediately. Every later
`INFO_OUTPUT_FORMAT_CHANGED` event is read and validated again. A repeated
compatible format is accepted; an incompatible change faults the encoder.

---

## Access-Unit Output

An output buffer without `BUFFER_FLAG_PARTIAL_FRAME` ends on an access-unit
boundary, but Android does not guarantee that it contains only one audio access
unit. Phase 1-P treats it as one candidate only under the strict full-cycle and
independent-decode compatibility gates above. A buffer carrying
`BUFFER_FLAG_PARTIAL_FRAME` is a legal prefix, not an error: the wrapper appends
its indicated bytes to a private accumulator and waits for the next
non-partial media buffer.

Before any `ByteArray` allocation, non-config output is limited to 65,536 bytes
and codec-config output is limited to the two canonical bytes. Partial data is
kept in exact-sized `ByteArray` values rather than a geometrically growing
stream buffer, so no candidate backing array exceeds 65,536 bytes.

For each output buffer, the implementation:

1. Validates `BufferInfo.offset` and `BufferInfo.size` against the buffer bounds.
2. Requires canonical configuration to have been validated.
3. Copies exactly the indicated byte range.
4. Appends partial media bytes or completes the pending access unit.
5. Returns one candidate `EncodedAccessUnit` only when a full platform buffer is complete.
6. Releases the native output buffer in `finally`.

For an unfragmented unit, `presentationTimeUs` is the platform
`BufferInfo.presentationTimeUs`. For a fragmented unit, it is the first
non-empty fragment's platform timestamp. The wrapper exposes this value for
diagnostics but does not require it to equal an input timestamp, be
non-negative, or serve as the future wire clock.

Empty non-config output is ignored. An EOS buffer with complete data returns
that access unit before completing drain. EOS while a partial unit remains
unfinished faults the encoder. An empty EOS buffer is never returned as audio.

`INFO_TRY_AGAIN_LATER` ends non-blocking collection. `INFO_OUTPUT_BUFFERS_CHANGED` is tolerated for the synchronous compatibility path. Every other unexpected result faults the encoder.

The wrapper does not add ADTS, LATM/LOAS framing, container bytes, or protocol fields.

The wrapper counts successfully queued PCM frames and completed output
candidates. Drain faults unless the counts are equal. Because some candidates
may already have been returned by earlier `submit` calls, this late full-cycle
check is sufficient only for the standalone proof; runtime packetization stays
out of scope until the codec has passed the same device gate.

---

## Drain And Cleanup

The first `drain`:

1. Acquires an input buffer under the same monotonic deadline.
2. Queues a zero-length input with `BUFFER_FLAG_END_OF_STREAM`.
3. Continues dequeuing output until an output buffer carries EOS.
4. Returns all delayed non-config access units.
5. Requires completed output-candidate count to equal queued PCM-frame count.
6. Enters `Drained`.

The zero-length EOS input uses timestamp `0`. That timestamp has no media or
wire meaning and is never exposed as an access-unit timestamp.

A second `drain` returns `emptyList()`. `submit` after drain fails.

`close` is owner-thread-only, idempotent, and never drains implicitly. It attempts `stop` for a started codec and always attempts `release`, preserving later cleanup attempts if an earlier one fails. No finalizer is added.

---

## State And Error Model

Private state is exactly:

```text
Active
Drained
Faulted
Closed
```

Validation order is:

```text
owner thread
closed
faulted/drained
arguments
native operation
```

Rules:

- Wrong thread or invalid state throws `IllegalStateException`.
- Invalid caller data throws `IllegalArgumentException` and does not fault the codec.
- MediaCodec exceptions, format/config mismatches, invalid native buffer bounds,
  oversized or unfinished partial output, input/output count mismatch, and
  operation timeouts enter `Faulted`.
- After `Faulted`, only owner-thread `close` is allowed.
- Error messages name the failed operation; original platform exceptions remain causes.
- The native test never converts codec absence or mismatch into assumption, skip, or success.

---

## JVM Contract Test

One focused JVM test exercises the pure exact-ASC validator in the production file:

- accepts exactly `11 90`;
- rejects empty, one-byte, extra-byte, wrong-profile, wrong-rate, and wrong-channel encodings.

No Robolectric or codec mock is introduced. JVM tests do not claim native encoder coverage.

---

## Android Native Test

`MediaCodecAacEncoderTest` runs through `AndroidJUnitRunner` on a real API 29 x86_64 emulator.

The success path:

1. Generates twelve contiguous stereo PCM frames in memory.
2. Uses different deterministic sine frequencies for left and right channels.
3. Computes canonical rational presentation timestamps.
4. Submits all twelve frames without assuming immediate output.
5. Calls `drain` and appends delayed output.
6. Requires exactly twelve non-empty completed output candidates. This is the
   Version 1 compatibility gate, not a general Android scheduling guarantee.
7. Records the access-unit count and platform output timestamps without
   requiring output timestamps to equal input timestamps.
8. Calls idempotent second `drain` and `close`.
9. Repeats the complete create/encode/drain/close cycle in the same test process.
10. Writes one successful run as a test-only ADTS file in app-private storage.

Focused native tests also prove:

- wrong PCM length rejection;
- negative and non-increasing input timestamp rejection;
- submit after drain;
- use after close;
- wrong-thread submit, drain, and close;
- idempotent drain and close.

Tests log `codecName`, but do not assert hardware acceleration or a specific implementation name.

---

## Test-Only ADTS Artifact

Production output stays raw. Instrumentation code adds one seven-byte CRC-free
ADTS header per returned candidate only to carry the twelve proven boundaries
between CI jobs.

Every generated header encodes:

```text
syncword                 0xFFF
MPEG-4                   ID 0
layer                    0
protection_absent        1
profile                  1 (AAC-LC object type minus one)
sampling_frequency_index 3 (48 kHz)
channel_configuration    2
frame_length             7 + raw AU length
buffer fullness          0x7FF
raw blocks               0
```

The Android test reads the completed file back, validates every boundary and raw payload, and requires no trailing bytes before exposing it to CI.

The CI script copies it from app-private storage with `adb exec-out run-as com.openaudiolink` and requires a non-empty host file before upload.

The artifact is ephemeral and must not be checked into the repository.

---

## Windows Interoperability Oracle

The Android workflow's dependent Windows job downloads the exact artifact produced by the emulator job.

One MSTest method is active only when the interop environment flag is set. In that mode it:

1. Requires the artifact path and file.
2. Parses strict CRC-free AAC-LC/48 kHz/stereo ADTS boundaries.
3. Requires exactly twelve frames and removes every seven-byte header.
4. Decodes each unchanged candidate with a fresh
   `MediaFoundationAacDecoder` and requires exactly 4096 PCM bytes, proving the
   selected codec did not batch multiple decodable AUs into that buffer.
5. Submits all twelve unchanged raw access units to one decoder and drains
   delayed output.
6. Requires continuous total PCM bytes to equal:

```text
12 access units * 1024 * 2 channels * 2 bytes = 49,152 bytes
```

7. Requires non-zero signed PCM16 energy independently in both channels.

The job runs in an x64 testhost. Existing Phase 1-O CI already proves the decoder ABI independently in x86 and x64; duplicating both architectures in this artifact-transfer job adds no codec-contract evidence.

No AAC or PCM byte hash is asserted. The exact frame count is a deliberate
compatibility restriction for the selected codec, not a claim that Android
requires all AAC encoders to schedule one output per input.

---

## GitHub Actions

The existing `android` workflow keeps its current triggers:

```yaml
pull_request:
push:
  branches: ['phase-*']
```

It must not add a `main` push trigger.

The workflow contains three jobs.

### Unit job

Runs the existing JVM suite plus the ASC validator test:

```text
./gradlew :app:testDebugUnitTest
```

### MediaCodec emulator job

Uses:

```text
ubuntu-22.04
Java 17
API level 29
default x86_64 system image
AndroidJUnitRunner
connectedDebugAndroidTest
```

`reactivecircus/android-emulator-runner@v2` provides the emulator lifecycle. The job enables KVM access, runs instrumentation, extracts the validated ADTS file while the emulator is alive, and uploads it with `actions/upload-artifact@v4`.

The Gradle invocation sets
`android.injected.androidTest.leaveApksInstalledAfterRun=true`; AGP 8.5.2
otherwise uninstalls the tested package before the following `run-as` command
can read app-private storage. CI also proves the package remains installed and
the host artifact is non-empty.

The runner clears logcat immediately before instrumentation and exports the
`MediaCodecAacTest`, `TestRunner`, and `AndroidJUnitRunner` tags whether
instrumentation succeeds or fails. The log dump runs after capturing the test
status and the workflow preserves that original exit code, making codec
lifecycle records, failure diagnostics, actual access-unit counts, and platform
timestamp lists visible in the exact workflow log instead of relying on a local
HTML test report.

### Windows interop job

The job:

- depends on successful emulator output;
- uses `windows-2022`;
- downloads the artifact with `actions/download-artifact@v4`;
- requires the file before testing;
- sets the x64 architecture and interop environment gates;
- runs the Windows tests through the existing solution with detailed console
  output so the artifact-specific result line is preserved;
- requires the artifact-specific MSTest to execute and pass.

The completion review records the exact workflow run, all three job names, and the Windows test count so a missing environment gate cannot silently turn the interop test into a no-op.

---

## Local Verification Boundary

The current development host is ARM64 Linux. Its installed Android platform tools are x64 and it has no emulator image, so the native test cannot run locally.

Local gates are:

- JVM Android tests with the existing ARM64 `aapt2` override;
- debug app and androidTest APK compilation;
- docs, fixture, and protocol consistency checks;
- Kotlin/Gradle compilation of all new sources.

Runtime truth comes from the exact-head GitHub API 29 emulator and dependent Windows job. Native-test failure must be investigated from the exact logs; it is never attributed to the environment without evidence.

---

## Documentation Alignment

`docs/04-Android.md` must state:

- Phase 1-P proves a standalone fixed encoder, not runtime capture or sending.
- `MediaCodec` may select software or hardware; this phase makes no hardware guarantee.
- the encoder accepts exact PCM frames and caller-owned timestamps;
- submit may return zero, one, or many access units;
- EOS/drain, not `flush`, returns delayed output;
- native codec buffering is expected;
- protocol packetization happens in a later runtime-integration phase.

`docs/10-Testing.md` must record:

- the API 29 emulator gate;
- exact ASC validation;
- repeated lifecycle and misuse tests;
- ephemeral ADTS artifact provenance;
- direct Windows decode byte-count and stereo-energy oracle;
- the software-codec, device-matrix, capture, runtime, playback, and latency limitations.

`docs/06-Audio.md` must not retain the stale one-input/one-output decoder loop.
Its future integration pseudocode must append every chunk returned by submit
and append delayed output from drain.

`docs/11-Roadmap.md` must no longer describe both implementations as merely planned. It should show Phase 0 complete, Phase 1 in progress, protocol/fake transport implemented, and Android/Windows audio paths partially implemented without claiming Version 1 completion.

The completed Phase 1-N and Phase 1-O design specs change status from `Draft for implementation` to `Implemented`. Phase 1-N's stale statement that the MediaCodec phase itself replaces the fake frame changes to the later sender runtime-integration phase. This Phase 1-P spec changes to `Implemented` only in the final documentation task after exact-head native and interop success.

---

## Test Strategy

The implementation follows RED to GREEN in this order:

1. Add and prove the emulator runner baseline without feature claims.
2. Add JVM ASC contract tests and observe the missing validator failure.
3. Add native encoder contract tests and observe the missing class failure on exact-head CI.
4. Implement the minimum encoder and return native CI to green.
5. Add artifact export and Windows interop RED test.
6. Connect the dependent CI job and require twelve independently decodable
   one-frame candidates plus the continuous 49,152-byte decode.
7. Align active docs and spec statuses.

Each task receives specification review followed by code-quality review. Critical and Important findings are fixed and re-reviewed before advancing.

---

## Acceptance Criteria

Phase 1-P is complete when:

- A production `MediaCodecAacEncoder` exists with only the specified fixed API.
- It configures AAC-LC, 48 kHz, stereo, PCM16, and 192 kbps through Android `MediaCodec`.
- It confirms the started encoder's actual input format is 48 kHz, stereo PCM16.
- Every input is exactly one 4096-byte PCM frame with a caller-owned monotonic timestamp.
- Output configuration is proven to be exactly `AudioSpecificConfig = 11 90` before audio is exposed.
- Codec-config, empty, partial, and EOS-only buffers are never exposed as audio access units.
- Submit handles zero, one, or many outputs without assuming one-in/one-out scheduling.
- Drain queues input EOS, reads through output EOS, and returns delayed access units.
- State, thread, timeout, fault, and cleanup behavior follow this design.
- The API 29 emulator submits twelve deterministic frames twice in one process.
- Each run drains through output EOS, produces exactly twelve completed output
  candidates, and records their platform timestamps; this strict compatibility
  gate rejects batching/loss/addition without claiming a general Android
  one-in/one-out guarantee.
- The test-only ADTS artifact contains the exact returned raw bytes and valid frame boundaries.
- The dependent Windows x64 job independently decodes every candidate to
  exactly 4096 PCM bytes, then continuously decodes all twelve with the
  existing Media Foundation decoder.
- Continuous Windows output is exactly 49,152 bytes with non-zero energy in both channels.
- JVM, emulator, interop, existing Windows matrix, docs, fixture, protocol, and Android regression gates pass on the exact phase-branch HEAD.
- Active docs describe only the standalone proof, preserve buffered submit/drain semantics, and make no capture, runtime, hardware, audible-playback, latency, or device-support claim.
- No fake sender, Android UI, protocol production, network, capture, service, renderer, or checked-in audio fixture changes occur.
- Phase 1-N, Phase 1-O, and Phase 1-P design statuses reflect completed reality.

---

## Follow-Up Order

After Phase 1-P:

1. Android playback capture feeds real PCM frames and capture timestamps into the standalone encoder.
2. The sender runtime connects encoded access units to packetization and transport with bounded queues and cancellation.
3. The Windows receiver runtime replaces `FakeAacDecoder` with the standalone Media Foundation decoder.
4. A Windows renderer sends decoded PCM to the selected audio device.

Discovery, foreground service, device policy, settings, logging, installers, performance, and release validation remain separate roadmap work.
