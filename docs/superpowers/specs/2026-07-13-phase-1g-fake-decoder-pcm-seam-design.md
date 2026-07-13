# Phase 1-G Windows Fake Decoder PCM Seam Design

**Status:** Draft for implementation

**Date:** 2026-07-13

**Scope:** Windows receiver fake decode and fake render seam only.

---

## Goal

Phase 1-G inserts the first decoder-shaped boundary between queued encoded `AUDIO` payloads and the fake renderer.

After this phase, Windows tests can prove this fake path:

```text
TcpReceiver audio sink -> AudioFrameQueue -> FakeAacDecoder -> FakePcmFrame -> FakeAudioRenderer
```

The decoder is deliberately fake. It parses the existing AAC `AUDIO` payload header, extracts deterministic metadata, and wraps the encoded bytes as fake PCM bytes. No real AAC decoding or speaker playback is added.

---

## Non-Goals

Phase 1-G must not add:

- Real AAC decode.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- Renderer threads, timers, clocks, jitter buffers, backpressure, async loops, or device selection.
- Android sender changes.
- Protocol wire-format changes.
- Discovery, UI, tray, installer, or configuration changes.
- New external dependencies.

The fake decoder exists only to create a typed PCM-shaped boundary for later real decode work.

---

## Current Baseline

Phase 1-F added `FakeAudioRenderer`, which drains encoded payload bytes directly from `AudioFrameQueue` and records them.

The remaining gap is that the renderer still receives encoded `AUDIO` payload bytes. There is no decoder-shaped component or PCM-shaped data model between the queue and renderer.

---

## Design

### Add `FakePcmFrame`

Add a small immutable receiver model:

```csharp
namespace OpenAudioLink.Receiver
{
    public sealed class FakePcmFrame
    {
        public FakePcmFrame(uint frameNumber, ulong captureTimestamp, ushort frameDuration, byte[] pcmBytes);
        public uint FrameNumber { get; }
        public ulong CaptureTimestamp { get; }
        public ushort FrameDuration { get; }
        public byte[] PcmBytes { get; }
    }
}
```

`FakePcmFrame` clones `pcmBytes` on construction and when returning `PcmBytes`. This keeps render history stable even if callers mutate source arrays or returned arrays.

`FakePcmFrame(null bytes)` throws `ArgumentNullException`.

### Add `FakeAacDecoder`

Add one synchronous decoder seam:

```csharp
namespace OpenAudioLink.Receiver
{
    public sealed class FakeAacDecoder
    {
        public FakePcmFrame Decode(byte[] audioPayload);
    }
}
```

`Decode` accepts the existing protocol `AUDIO` payload, not a whole packet.

It must:

1. Reuse `AudioPayloadValidator.ValidateAacPayload(audioPayload)` for trust-boundary validation.
2. Parse fields from the existing `AUDIO` payload layout:
   - byte `0`: codec, already validated as `CodecAacLc`.
   - bytes `1..4`: frame number, big-endian `uint`.
   - bytes `5..12`: capture timestamp, big-endian `ulong`.
   - bytes `13..14`: frame duration milliseconds, big-endian `ushort`.
   - bytes `15..18`: encoded byte length, big-endian `uint`.
   - bytes `19..end`: encoded bytes.
3. Return `FakePcmFrame(frameNumber, captureTimestamp, frameDuration, encodedBytes)`.

The fake PCM bytes are exactly the encoded bytes. This is intentional: this phase proves the typed boundary and metadata propagation, not audio decoding quality.

### Update `FakeAudioRenderer`

Change `FakeAudioRenderer` to store rendered `FakePcmFrame` values instead of raw encoded payload bytes.

Required API shape:

```csharp
public int RenderedCount { get; }
public IReadOnlyList<FakePcmFrame> RenderedFrames { get; }
public void Render(FakePcmFrame frame);
public int Drain(AudioFrameQueue queue, FakeAacDecoder decoder);
```

`Drain` must call `queue.TryDequeue` until empty, decode each queued payload, and render the decoded frame.

Validation:

- `Render(null)` throws `ArgumentNullException`.
- `Drain(null, decoder)` throws `ArgumentNullException`.
- `Drain(queue, null)` throws `ArgumentNullException`.

`RenderedFrames` returns a snapshot. The returned list cannot mutate renderer history, and `FakePcmFrame.PcmBytes` clone behavior protects the bytes.

### Why not keep raw-payload rendering

Keeping the old `Drain(AudioFrameQueue)` overload would let tests bypass the decoder seam. Phase 1-G should make the new queue-to-decoder-to-render path explicit.

No compatibility shim is added because `FakeAudioRenderer` is still an internal test seam with only repository-local callers.

---

## Testing Requirements

### Fake decoder unit tests

Add tests proving:

1. `Decode` maps frame number, capture timestamp, frame duration, and encoded bytes into `FakePcmFrame`.
2. `Decode` rejects unsupported codecs through the existing payload validator.
3. `Decode` rejects length mismatches through the existing payload validator.
4. `FakePcmFrame.PcmBytes` is isolated from caller mutation.

### Fake renderer unit tests

Update renderer tests to prove:

1. `Drain(queue, decoder)` moves all queued encoded payloads through `FakeAacDecoder` into rendered `FakePcmFrame` history.
2. FIFO order is preserved across multiple frames.
3. `Drain` returns the number of frames drained in that call.
4. Multiple drain calls append to render history.
5. Draining an empty queue returns `0` and increments `AudioFrameQueue.UnderflowCount` once through the final failed dequeue.
6. `Drain(null, decoder)`, `Drain(queue, null)`, and `Render(null)` throw `ArgumentNullException`.
7. Render history is immutable from the caller perspective.

### TCP loopback integration test

Extend the Phase 1-F TCP loopback test so queued fake `AUDIO` payloads are drained through `FakeAacDecoder` before rendering.

The test must prove:

1. Three fake payloads reach `AudioFrameQueue`.
2. `FakeAudioRenderer.Drain(queue, decoder)` returns `3`.
3. `AudioFrameQueue.Count == 0` after drain.
4. Rendered `FakePcmFrame` values preserve frame numbers `1`, `2`, `3`.
5. Rendered fake PCM bytes match the three deterministic encoded byte arrays from Phase 1-E.
6. `PING -> PONG` still works after decode/render drain.
7. `STOP_STREAM` still closes the session path.

Use bounded waits for TCP callbacks. Do not add sleeps or background render loops.

### Regression checks

Local Linux checks remain:

```bash
python3 tools/check_docs_consistency.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
```

Windows CI must run:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Android CI must remain green:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

---

## Acceptance Criteria

Phase 1-G is complete when:

- Windows production code includes `FakePcmFrame` and `FakeAacDecoder` under `OpenAudioLink.Receiver`.
- `FakeAacDecoder.Decode` validates and parses existing AAC `AUDIO` payloads without changing protocol wire format.
- `FakeAacDecoder.Decode` returns fake PCM frames whose metadata matches the encoded payload header.
- Fake PCM bytes are deterministic and equal to the encoded byte section of the `AUDIO` payload.
- `FakeAudioRenderer` renders `FakePcmFrame` values, not raw encoded payloads.
- TCP loopback tests prove three fake payloads travel from receiver sink to `AudioFrameQueue`, through `FakeAacDecoder`, and into `FakeAudioRenderer` in order.
- No real AAC decode, WASAPI, NAudio, Media Foundation, renderer thread, timer, jitter buffer, Android change, protocol change, UI, config, or dependency is added.
- All CI workflows are green.
