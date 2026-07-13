# Phase 1-H Windows Receiver Runtime Composition Design

**Status:** Draft for implementation

**Date:** 2026-07-13

**Scope:** Windows receiver runtime composition only.

---

## Goal

Phase 1-H adds the first production-side Windows receiver runtime composition.

After this phase, Windows production code has one small object that wires the existing fake receiver path:

```text
TcpReceiver audio sink -> AudioFrameQueue -> FakeAacDecoder -> FakePcmFrame -> FakeAudioRenderer
```

This turns the tested seams from Phase 1-D through Phase 1-G into one reusable runtime entry point without adding real AAC decode, WASAPI, UI lifecycle, or device playback.

---

## Non-Goals

Phase 1-H must not add:

- Real AAC decode.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- Android sender changes.
- Protocol wire-format changes.
- UI controls, tray behavior, installer work, settings, or device selection.
- New background render loops, timers, clocks, jitter buffers, or backpressure.
- New external dependencies.

`TcpReceiver` already uses thread-pool workers internally. Phase 1-H does not add another thread or scheduling abstraction.

---

## Current Baseline

Phase 1-G added:

- `AudioFrameQueue` as the accepted encoded payload FIFO.
- `FakeAacDecoder` to parse AAC `AUDIO` payloads into `FakePcmFrame` values.
- `FakeAudioRenderer` to record rendered fake PCM frames.

Tests manually compose these pieces. Production code has no object that owns the composed receiver runtime.

`MainForm` is still a UI skeleton and should remain unchanged in this phase.

---

## Design

### Add `ReceiverRuntime`

Add one small disposable receiver runtime:

```csharp
namespace OpenAudioLink.Receiver
{
    public sealed class ReceiverRuntime : IDisposable
    {
        public int Port { get; }
        public AudioFrameQueue Queue { get; }
        public FakeAudioRenderer Renderer { get; }
        public static ReceiverRuntime StartLoopback(int queueCapacity = 8);
        public static ReceiverRuntime Start(IPAddress address, int port, int queueCapacity = 8);
        public void Dispose();
    }
}
```

The runtime owns:

- `AudioFrameQueue`.
- `FakeAacDecoder`.
- `FakeAudioRenderer`.
- `TcpReceiver`.

`Start(address, port, queueCapacity)` creates these components and starts `TcpReceiver` with an audio sink that does:

```csharp
queue.Enqueue(payload);
renderer.Drain(queue, decoder);
```

This intentionally drains synchronously inside the existing receiver callback. It is the smallest runtime composition that proves the end-to-end fake path. If later real decode or playback needs different scheduling, that phase can replace this synchronous drain with a dedicated loop.

`StartLoopback(queueCapacity)` uses `IPAddress.Loopback` and port `0`, matching existing tests.

### Public state

`Port` exposes the bound TCP port for tests and future UI wiring.

`Queue` is exposed so tests can inspect queue count/drop/underflow counters. The runtime owns it; callers must not enqueue into it in production use.

`Renderer` is exposed so tests and future UI wiring can inspect rendered fake PCM frames.

### Disposal

`Dispose` delegates to the owned `TcpReceiver.Dispose`.

No explicit stopped state or restart behavior is added. Create a new runtime when needed.

### Error handling

- `Start(address: null, ...)` throws `ArgumentNullException`.
- `queueCapacity <= 0` is rejected by existing `AudioFrameQueue` validation.
- Invalid network packets are still handled by `TcpReceiver` / `ReceiverSession`.
- Invalid audio payloads are still rejected by `AudioPayloadValidator` through `FakeAacDecoder`.

---

## Testing Requirements

### Runtime unit/integration tests

Add tests proving:

1. `StartLoopback` exposes a non-zero `Port`, an empty `Queue`, and an empty `Renderer`.
2. Sending the existing Phase 1-A/1-E TCP handshake plus three fake `AUDIO` payloads through `ReceiverRuntime.StartLoopback()` renders three fake PCM frames.
3. Rendered frames preserve frame numbers `1`, `2`, `3`.
4. Rendered fake PCM bytes equal the deterministic encoded bytes from the payloads.
5. Queue count is `0` after runtime drain.
6. `PING -> PONG` still works after rendering.
7. `STOP_STREAM` still closes the session path.
8. `Start(null, ...)` throws `ArgumentNullException`.
9. `Start(..., queueCapacity: 0)` throws `ArgumentOutOfRangeException` through `AudioFrameQueue`.

Use bounded waits. Do not add sleeps or background render loops.

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

Phase 1-H is complete when:

- Windows production code includes `ReceiverRuntime` under `OpenAudioLink.Receiver`.
- `ReceiverRuntime` owns and composes `TcpReceiver`, `AudioFrameQueue`, `FakeAacDecoder`, and `FakeAudioRenderer`.
- TCP loopback runtime tests prove three fake `AUDIO` payloads render into three fake PCM frames in order.
- Runtime queue count is `0` after synchronous drain.
- Existing protocol, queue, decoder, renderer, and TCP receiver tests stay green.
- `MainForm` remains unchanged.
- No real AAC decode, WASAPI, NAudio, Media Foundation, Android change, protocol change, UI behavior, background render loop, timer, jitter buffer, config, or dependency is added.
- All CI workflows are green.
