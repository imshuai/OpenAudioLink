# Phase 1-Q Windows Decoder Runtime Integration Design

**Status:** Draft for implementation

**Date:** 2026-07-15

**Scope:** Connect the existing Windows TCP receiver runtime to the existing
Media Foundation AAC decoder while retaining the non-audible fake renderer.

---

## Goal

Phase 1-Q replaces the fake decode step in the executable Windows receiver
path:

```text
TCP AUDIO
  -> AudioFrameQueue
  -> MediaFoundationAacDecoder
  -> 48 kHz stereo PCM16 frames
  -> FakeAudioRenderer
```

After this phase, a sender can transmit canonical raw AAC-LC `AUDIO` packets
over loopback TCP and `ReceiverRuntime` records the real decoded PCM. The phase
does not make sound: `FakeAudioRenderer` remains the final sink and the WinForms
status remains a rendered-frame count.

The Android runtime remains unchanged. Its current fake sender already emits
canonical raw AAC access units, so receiver integration can be proved without
mixing Android capture or encoder-runtime ownership into this phase.

---

## Current Runtime Truth

Phase 1-O added a standalone `MediaFoundationAacDecoder`, and Phase 1-P proved
that Android `MediaCodec` output can be decoded by it. Neither native codec is
connected to its application runtime.

The active Windows path is still:

```text
TcpReceiver
  -> ReceiverSession
  -> ReceiverRuntime callback
  -> AudioFrameQueue
  -> FakeAudioRenderer.Drain
  -> FakeAacDecoder
  -> FakePcmFrame containing encoded AAC bytes
```

`TcpReceiver.Handle` owns one accepted client on one ThreadPool thread, but its
current `Action<byte[]>` callback has no stream-start or stream-end lifecycle.
`MediaFoundationAacDecoder` requires construction, `Submit`, `Drain`, and
`Dispose` on one owner thread. Reusing one decoder across reconnects or closing
it from `ReceiverRuntime.Dispose` would violate that contract.

---

## Selected Design

### Use the existing TCP session thread

The accepted client's existing `TcpReceiver.Handle` thread owns the decoder.
No decoder worker, executor, timer, or second queue is added.

`TcpReceiver.Start` and `StartLoopback` gain two optional callbacks alongside
the existing audio sink:

```csharp
Action streamStarted
Action streamEnded
```

Their exact lifecycle is:

1. A busy/rejected client invokes neither callback.
2. One private lifecycle lock serializes receiver disposal, accepted-client
   publication, and the complete `streamStarted` callback. `Handle` publishes
   `currentClient` under that lock only while not disposed. Before starting a
   stream it reacquires the same lock, rechecks `disposed`, constructs the
   decoder through `streamStarted`, and marks the stream active before release.
   `Dispose` marks the receiver disposed and detaches the published client under
   the same lock, then stops/closes outside it. Thus either stream startup is
   linearized before disposal or it does not run; no decoder starts after the
   disposal transition.
3. For an accepted client, `streamStarted` runs exactly once after a supported
   `START_STREAM` changes `ReceiverSession` to `Streaming`, but before the
   successful `STREAM_READY` bytes are written.
4. Every subsequent audio-sink call runs on that same `Handle` thread.
5. If `streamStarted` completed, `streamEnded` runs exactly once on that same
   thread when `STOP_STREAM`, EOF, timeout, malformed input, decode failure, or
   receiver disposal ends the connection.
6. `streamEnded` finishes before the receiver clears its active-client slot,
   so a reconnect cannot overlap decoder teardown.
7. A lifecycle callback may use `PacketParseException` to reject the current
   stream. That expected session failure closes the client and releases the
   active slot without escaping the ThreadPool entry point; unrelated
   programming exceptions are not silently reclassified as packet failures.

The callbacks are the minimum lifecycle seam required by the native decoder.
No session interface, decoder interface, factory, DI container, or new worker
abstraction is introduced.

### Keep composition inside `ReceiverRuntime`

`ReceiverRuntime.Start` retains one runtime-level `AudioFrameQueue` and
`FakeAudioRenderer`. A private session helper inside `ReceiverRuntime.cs` owns:

- one `MediaFoundationAacDecoder`;
- FIFO metadata for accepted AAC access units;
- one reusable 4096-byte PCM frame assembly buffer;
- the existing renderer and rendered-count callback.

`streamStarted` constructs the helper. The audio sink enqueues the validated
payload and synchronously drains the existing queue through that helper.
`streamEnded` drains delayed decoder output and disposes the decoder. The
helper reference is then cleared before another session may start.

All managed fields and buffers needed by the helper are initialized before its
native decoder is created. `MediaFoundationAacDecoder` already unwinds its own
partially completed constructor. If any later helper-start step fails after
decoder construction, `streamStarted` disposes that decoder on the same thread
before rethrowing and does not publish the helper. `streamEnded` is therefore
reserved for a fully completed start without leaving a partial-start leak.

Keeping the helper private avoids creating a second public decoder API. The
existing `FakeAacDecoder` and `FakeAudioRenderer.Drain(queue, fakeDecoder)` may
remain for their focused historical tests, but `ReceiverRuntime` no longer
constructs or calls `FakeAacDecoder`.

### Preserve access-unit metadata through decoder latency

One `AUDIO` packet supplies one raw AAC access unit and these metadata fields:

```text
Frame Number
Capture Timestamp
Frame Duration
```

Before calling `Submit`, the helper appends that metadata to a FIFO. Decoder
output may arrive during the same call, a later call, or `Drain`; no individual
`Submit` call is assumed to return one PCM frame.

The fixed Version 1 PCM frame size is:

```text
1024 samples/channel * 2 channels * 2 bytes/sample = 4096 bytes
```

Media Foundation output-call boundaries are not treated as audio-frame
boundaries. Every returned block-aligned PCM chunk is copied into one reusable
4096-byte assembly buffer. Each completed PCM frame consumes the oldest FIFO
metadata entry and is rendered as one `FakePcmFrame` containing real PCM bytes.

This handles zero/one/many decoder chunks, partial chunks, and multiple PCM
frames in one chunk without adding a general PCM queue. It also preserves the
wire order when the decoder delays output.

After final `Drain`:

- the PCM assembly buffer must be empty;
- the metadata FIFO must be empty;
- every rendered PCM frame must contain exactly 4096 bytes.

Output without metadata, leftover partial PCM, or metadata without output is a
fatal error for the current stream because the implementation can no longer
prove frame/timestamp correspondence.

---

## Input Validation

`ReceiverSession` continues validating every `AUDIO` payload before invoking
the runtime sink. `AudioPayloadValidator` additionally rejects
`Encoded Size == 0`; an empty byte string cannot be one complete raw AAC access
unit and `MediaFoundationAacDecoder.Submit` already rejects it.

The helper copies exactly `Encoded Size` bytes beginning at
`ProtocolConstants.AudioPayloadHeaderSize`. It does not accept ADTS, codec
configuration buffers, concatenated access units, or partial access units.
The existing Version 1 codec and payload-length checks remain unchanged.

No new maximum below the protocol payload limit, AAC syntax parser, or content
pre-validation is added. Native decode remains the semantic validation oracle.

---

## Error And Teardown Behavior

### Normal termination

For `STOP_STREAM`, peer EOF, socket timeout, or `ReceiverRuntime.Dispose` after
stream start, the owner thread performs:

```text
decoder.Drain
  -> assemble and render delayed PCM
  -> require empty PCM/metadata state
  -> decoder.Dispose
  -> clear session helper
```

Cleanup captures any primary drain/render/callback failure, always attempts
decoder disposal, then rethrows the primary with its original stack. A cleanup
failure becomes the session failure only when no primary failure exists;
otherwise it is attached as secondary exception data and cannot mask the
primary. Active-client release still runs afterward. A final rendered-count
notification is made after successful delayed output is rendered.

`TcpReceiver.Dispose` may wait for an already-entered `streamStarted` callback
to finish while holding the lifecycle ordering point; it does not wait for an
established stream's full end callback. The accepted worker observes the closed
published client and performs decoder teardown on its owner thread. Disposal is
not permitted to leave an accepted unpublished client running after the
listener has stopped.

### Decode failure

Expected decoder initialization, submit, drain, and output-shape failures plus
all exceptions surfaced by the decoder's native teardown boundary are converted
to the receiver's existing `PacketParseException` session-termination path,
preserving the original exception as `InnerException`.
`PacketParseException` therefore gains only the standard two-argument
constructor needed for this wrapping.

If `Submit` faults the decoder, finalization skips another `Drain`, disposes it
on the owner thread, closes the current connection, and permits a later client
to create a fresh decoder. No exception is allowed to escape the ThreadPool
entry point, and no `ERROR` packet is invented after the connection has become
unusable.

The broader Version 1 documents describe future per-frame decoder recovery and
an error threshold. Phase 1-Q deliberately implements the safer intermediate
policy of terminating the current stream on the first native decoder failure.
Decoder recreation within a live stream, configurable thresholds, statistics,
and user-visible diagnostics remain later stabilization work; active status
documentation must not claim they are implemented here.

---

## Threading And Queue Semantics

All queue draining, decoder calls, PCM assembly, fake rendering, and final
drain execute synchronously on the accepted client's ThreadPool thread.
ThreadPool workers are MTA on the supported Windows runtime, and the decoder's
existing owner-thread checks remain authoritative.

`AudioFrameQueue` remains in the path to preserve the current runtime seam and
observable queue counters. Because the audio callback drains it before
returning and only one client may stream, Phase 1-Q does not introduce queue
concurrency or meaningful decode backpressure. A slow decoder can still delay
network reads. A dedicated decoder thread and bounded asynchronous backlog
belong to a later phase only when capture and playback timing require them.

`FakeAudioRenderer` remains non-audible and records frames across reconnects.
No renderer locking, PCM playback queue, WASAPI clock, jitter buffer, or latency
policy is added.

---

## Public Surface

The only production API expansion is the two optional stream-lifecycle
callbacks on `TcpReceiver.Start` and `StartLoopback`. Existing callers that
provide only `audioSink` remain source-compatible.

`ReceiverRuntime` keeps its existing public surface:

```csharp
public int Port { get; }
public AudioFrameQueue Queue { get; }
public FakeAudioRenderer Renderer { get; }
public static ReceiverRuntime StartLoopback(int queueCapacity = 8)
public static ReceiverRuntime Start(
    IPAddress address,
    int port,
    int queueCapacity = 8,
    Action<int> renderedCountChanged = null)
public void Dispose()
```

`FakePcmFrame.PcmBytes` now contains genuine PCM when produced by
`ReceiverRuntime`; the type remains named `FakePcmFrame` because the final
renderer is still a test/status sink rather than an audio device.

---

## Testing

### Protocol validation

Add one focused test proving an otherwise well-shaped AAC payload with
`Encoded Size == 0` is rejected before the audio sink or native decoder runs.
Existing codec, short-header, and encoded-length mismatch tests remain green.

### TCP lifecycle

Extend `TcpReceiverTests` to prove:

- `streamStarted`, audio sink, and `streamEnded` run in that order;
- all three run on the same managed thread;
- `streamStarted` runs before `STREAM_READY` becomes observable;
- `streamEnded` runs for clean `STOP_STREAM` and abrupt disconnect;
- a busy client invokes neither callback;
- a callback failure closes only that client and a later client can reconnect.
- disposing after a client connects but before `START_STREAM` prevents the
  stream-start callback and closes the client.

Tests use bounded events and socket timeouts, never sleeps.

### Real runtime loopback

Replace the fake expectation in `ReceiverRuntimeTests` with a native Windows
loopback proof:

1. Complete `HELLO` and supported `START_STREAM`.
2. Send consecutive canonical raw AAC-LC `AUDIO` packets with distinct frame
   numbers and capture timestamps.
3. Verify `PING -> PONG` while the stream is active.
4. Send `STOP_STREAM` and use connection EOF as the session-finalization
   barrier.
5. Require one rendered 4096-byte PCM frame per accepted access unit, in wire
   order, with exact original metadata.
6. Require non-zero signed PCM16 energy independently in both channels rather
   than a platform-specific byte hash.
7. Require queue count zero after finalization.
8. Repeat the complete connection in the same runtime to prove a fresh decoder
   can be created after owner-thread drain/disposal without assuming that the
   ThreadPool reuses the previous worker.

A separate corrupt-stream test sends a non-empty truncated access unit that is
valid at the packet-shape boundary but cannot yield the required complete PCM
frame. Whether the MFT rejects during submit or final drain finds unmatched
metadata, the current connection must close without an invented `ERROR`
response, render no corrupt frame, and permit a following healthy connection
to decode successfully.

The native integration test is mandatory in both x86 and x64 processes. A
missing Media Foundation decoder is a failure, not a skip. Existing standalone
decoder tests continue proving detailed COM/MFT behavior.

`MainFormTests` currently asserts that the runtime renderer stores the encoded
fixture bytes. Update that existing status test to retain its UI-count assertion
while requiring 4096-byte, non-silent decoded PCM instead. No WinForms
production code or layout changes are needed.

### Local and CI gates

Linux-local checks cover repository-independent validation:

```bash
python3 tools/check_docs_consistency.py
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check
```

Exact-head GitHub Actions must pass all phase-branch workflows. The decisive
Windows matrix remains:

```text
windows-2022 / x86
windows-2022 / x64
```

The SHA tested by successful `docs`, `windows`, and `android` runs must equal
the pushed phase-branch head before fast-forwarding `main`. Pushing `main` must
not trigger duplicate CI under the current workflow policy.

---

## Files And Responsibilities

Modify production code:

- `receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs` — exact stream
  lifecycle callbacks on the existing session thread and the accepted-client
  publication/disposal race fix.
- `receiver-windows/src/OpenAudioLink/Receiver/ReceiverRuntime.cs` — private
  session-owned Media Foundation decode, PCM assembly, metadata FIFO, fake
  render, drain, and cleanup.
- `receiver-windows/src/OpenAudioLink/Protocol/AudioPayloadValidator.cs` — reject
  empty raw AAC access units.
- `receiver-windows/src/OpenAudioLink/Protocol/PacketParseException.cs` — retain
  native failure context through the standard inner-exception constructor.

Modify tests:

- `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs`
- `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`
- `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs`
- `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`

Update active documentation:

- `docs/03-Protocol.md` — make the non-empty access-unit rule explicit.
- `docs/05-Windows.md` — record session-thread runtime decode and fake render.
- `docs/06-Audio.md` — distinguish this synchronous integration from the future
  decoder-thread/playback design.
- `docs/10-Testing.md` — record native x86/x64 TCP runtime coverage.
- `docs/11-Roadmap.md` — update the current Windows status.
- This design and its implementation plan/status.

No Android source, protocol wire bytes, fixture bytes, workflow trigger,
WinForms layout, or project dependency changes are required.

---

## Rejected Alternatives

### Dedicated decoder worker thread

This would require control messages for start, stop, drain, reconnect, decoder
failure, and runtime shutdown plus a second queue and join policy. The current
single-client `Handle` thread already provides correct ownership; the extra
scheduler is deferred until playback timing demonstrates the need.

### Shared or runtime-owned decoder

Reconnects may execute on different ThreadPool threads. A decoder stored for
the lifetime of `ReceiverRuntime` could not be safely reused or disposed while
respecting the existing owner-thread contract.

### Treat each decoder output chunk as one frame

The standalone decoder intentionally permits arbitrary zero/one/many output
chunks. Mapping metadata directly to chunk calls would make correctness depend
on undocumented MFT buffer boundaries. The fixed 4096-byte assembler is both
smaller and correct for split or coalesced chunks.

### Add decoder interfaces or factories

Version 1 has one fixed Windows implementation. An `IAudioDecoder`, factory,
configuration object, or DI container would add unused flexibility without
solving lifecycle or PCM framing.

### Add WASAPI now

Audible rendering introduces device selection, format negotiation, playback
threading, buffering, shutdown, underflow, and hardware-dependent tests. Keeping
the fake renderer makes native network decode independently reproducible.

---

## Non-Goals

Phase 1-Q must not add:

- Android capture, encoder-runtime integration, packet pacing, or sender changes.
- WASAPI, NAudio, WaveOut, speaker playback, device selection, or hot-plug.
- A decoder worker, coroutine, executor, timer, PCM queue, jitter buffer, or
  playback clock.
- Decoder recreation inside a live stream, configurable error thresholds, or
  per-frame corruption recovery.
- Discovery, mDNS, service, foreground process, settings, installer, or logs.
- UI layout or status changes beyond the existing rendered-frame callback.
- Protocol packet types, result values, error codes, fixture bytes, or codec
  negotiation changes.
- New production dependencies, interfaces, factories, or DI.
- Latency, CPU, memory, endurance, Windows 7, or real-device claims.

---

## Acceptance Criteria

Phase 1-Q is complete only when:

- `ReceiverRuntime` no longer constructs or invokes `FakeAacDecoder`.
- Every supported TCP stream owns exactly one `MediaFoundationAacDecoder` on
  its accepted-client thread.
- Decoder construction, every submit, final drain, and disposal occur on that
  same thread for clean stop, disconnect, failure, and receiver shutdown.
- Receiver disposal cannot race an accepted but unpublished client into
  starting a decoder after shutdown.
- PCM chunk boundaries are assembled into exact 4096-byte frames before FIFO
  metadata is consumed.
- Final drain leaves no partial PCM and no unmatched metadata.
- Empty encoded access units are rejected before native decode.
- Real loopback tests prove ordered AAC-to-PCM rendering and reconnect in both
  x86 and x64 Windows processes.
- Existing protocol, standalone codec, TCP, UI, Android, fixture, and docs
  checks remain green.
- Active docs describe real Windows runtime decode but do not claim audible
  playback, Android runtime encoding, asynchronous decode, or recovery that is
  not implemented.
- Exact-head phase-branch CI is green, the branch is fast-forwarded to `main`,
  source and mirror refs agree, and `main` push does not create duplicate runs.
