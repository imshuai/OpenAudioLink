# Phase 1-F Windows Fake Playback Render Design

**Status:** Draft for implementation

**Date:** 2026-07-13

**Scope:** Windows receiver queue consumption and fake render tests only.

---

## Goal

Phase 1-F adds the first receiver-side playback boundary after `AudioFrameQueue`.

After this phase, Windows tests can prove this full fake path:

```text
TcpReceiver audio sink -> AudioFrameQueue -> FakeAudioRenderer
```

The renderer is deliberately fake. It drains already-accepted `AUDIO` payloads from the queue and records exactly what would be handed to a future decoder/playback layer.

This moves the main path one step past queueing without adding AAC decode, WASAPI, Media Foundation, NAudio, device selection, timing, or playback threads.

---

## Non-Goals

Phase 1-F must not add:

- AAC decode.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- Renderer threads, timers, clocks, jitter buffers, backpressure, or async loops.
- Android sender changes.
- Protocol wire-format changes.
- Discovery, UI, tray, installer, or configuration changes.
- New external dependencies.

The renderer is a synchronous test seam only.

---

## Current Baseline

Phase 1-D added `AudioFrameQueue`, a bounded FIFO for accepted receiver-side audio payloads.

Phase 1-E proved the Windows TCP loopback path can enqueue three deterministic fake `AUDIO` payloads in order.

The remaining gap is that no Windows component consumes queued frames. Tests currently stop at `AudioFrameQueue.TryDequeue` assertions, so the future playback boundary is not represented in production code.

---

## Design

### Add a fake renderer seam

Add a small Windows receiver component:

```csharp
namespace OpenAudioLink.Receiver
{
    public sealed class FakeAudioRenderer
    {
        public int RenderedCount { get; }
        public IReadOnlyList<byte[]> RenderedFrames { get; }
        public int Drain(AudioFrameQueue queue);
    }
}
```

`Drain` synchronously calls `queue.TryDequeue(out byte[] payload)` until it returns `false`.

For each dequeued payload, `FakeAudioRenderer` stores a clone. The clone prevents later mutations to the queue payload or caller-owned arrays from changing recorded render history.

Return value:

```text
number of frames drained during this call
```

`RenderedCount` returns the total number of frames recorded across all drain calls.

`RenderedFrames` returns a read-only snapshot of recorded frame clones. Mutating a returned frame must not mutate renderer state.

### Why a synchronous drain method

A synchronous `Drain` is enough to prove the next boundary:

1. Frames leave `AudioFrameQueue` in FIFO order.
2. The renderer receives each payload exactly once.
3. Draining an empty queue is safe and visible through `AudioFrameQueue.UnderflowCount`.

Threads, timing, callbacks, and playback device abstractions would be speculative here. They can be added when a real decoder or playback loop exists.

### Error handling

`Drain(null)` throws `ArgumentNullException`.

No other error model is added. Invalid audio payload validation remains the responsibility of the existing receiver/session parsing path before enqueue.

### Integration boundary

`TcpReceiver` remains unchanged in production code.

Tests wire the existing sink to `AudioFrameQueue.Enqueue`, then call `FakeAudioRenderer.Drain(queue)` after the expected frames arrive. This proves the queue-to-render seam without changing network behavior.

---

## Testing Requirements

### Fake renderer unit tests

Add tests proving:

1. `Drain` moves all queued frames into the renderer and empties the queue.
2. FIFO order is preserved across multiple frames.
3. `Drain` returns the number of frames drained in that call.
4. Multiple drain calls append to render history.
5. Draining an empty queue returns `0` and increments `AudioFrameQueue.UnderflowCount` once through the final failed dequeue.
6. `Drain(null)` throws `ArgumentNullException`.
7. Rendered frame history is immutable from the caller perspective: mutating the source payload or a returned rendered frame does not change renderer state.

### TCP loopback integration test

Extend the Phase 1-E TCP loopback test so after three fake `AUDIO` payloads are queued:

1. `FakeAudioRenderer.Drain(queue)` returns `3`.
2. `AudioFrameQueue.Count == 0` after drain.
3. `FakeAudioRenderer.RenderedFrames` contains the three expected payloads in FIFO order.
4. `PING -> PONG` still works after drain.
5. `STOP_STREAM` still closes the session path.

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

Phase 1-F is complete when:

- Windows production code includes `FakeAudioRenderer` under `OpenAudioLink.Receiver`.
- `FakeAudioRenderer.Drain(AudioFrameQueue queue)` synchronously drains all currently queued frames.
- Drained frames are recorded in FIFO order.
- Render history is protected by cloning at enqueue-to-render and snapshot-return boundaries.
- Empty drain returns `0` and increments queue underflow exactly once.
- `Drain(null)` throws `ArgumentNullException`.
- TCP loopback tests prove three fake payloads travel from receiver sink to `AudioFrameQueue` and then to `FakeAudioRenderer` in order.
- No real decode, WASAPI, NAudio, Media Foundation, renderer thread, timer, jitter buffer, Android change, protocol change, UI, config, or dependency is added.
- All CI workflows are green.
