# Phase 1-D Receiver AAC Queue Design

**Status:** Draft for implementation

**Date:** 2026-07-10

**Scope:** Windows receiver only.

---

## Goal

Phase 1-D adds the smallest receiver-side bounded queue for accepted `AUDIO` payloads.

After this phase, the Windows receiver can run this path:

```text
HELLO -> WELCOME
START_STREAM -> STREAM_READY
AUDIO(valid AAC payload) -> validated -> bounded AAC queue
PING -> PONG
STOP_STREAM -> close
```

This prepares the future AAC decoder/playback phase without adding decoder, renderer, jitter buffer, or audio-device code.

---

## Non-Goals

Phase 1-D must not add:

- AAC decoding.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- Playback threads, jitter buffers, playback clocks, or renderer loops.
- Android capture or encoder work.
- mDNS discovery.
- UI, tray, installer, or configuration changes.
- New external dependencies.

The queue is intentionally in-process and minimal. A future decoder phase can consume from it.

---

## Current Baseline

Phase 1-C already implemented:

- `ReceiverSession` validation for streaming `AUDIO` packets.
- A minimal `Action<byte[]> audioSink` seam.
- Defensive payload clones before sink delivery.
- `TcpReceiver.Start(..., Action<byte[]> audioSink = null)` and `StartLoopback(...)` sink wiring.
- TCP loopback coverage proving a valid fake `AUDIO` packet reaches the sink.

The remaining gap is that the sink seam has no bounded buffering component. A slow future decoder would otherwise need to be called directly from the network/session path or invent its own buffering later.

---

## Design

### Queue Shape

Add one small Windows receiver class:

```csharp
OpenAudioLink.Receiver.AudioFrameQueue
```

It stores accepted `AUDIO` payload bytes, not decoded PCM.

Required public shape:

```csharp
public sealed class AudioFrameQueue
{
    public AudioFrameQueue(int capacity);

    public int Capacity { get; }
    public int Count { get; }
    public ulong DroppedFrames { get; }
    public ulong UnderflowCount { get; }

    public void Enqueue(byte[] payload);
    public bool TryDequeue(out byte[] payload);
}
```

No interface is introduced in Phase 1-D. The class can be passed directly to the existing sink seam with:

```csharp
new ReceiverSession(sessionId, queue.Enqueue)
```

or:

```csharp
TcpReceiver.StartLoopback(queue.Enqueue)
```

### Capacity

The queue is bounded by constructor argument.

Rules:

- `capacity <= 0` throws `ArgumentOutOfRangeException`.
- `Capacity` never changes after construction.
- `Count` never exceeds `Capacity`.

No configuration file is added in this phase. Tests can use small capacities such as `2`; future application wiring can choose a production default.

### Enqueue Behavior

`Enqueue` owns the stored payload bytes.

Rules:

1. `payload == null` throws `ArgumentNullException`.
2. The queue clones `payload` before storing it.
3. If the queue is not full, append the clone.
4. If the queue is full, drop the oldest queued frame, increment `DroppedFrames`, then append the clone.

Drop-oldest is required because real-time audio prefers lower latency over perfect frame preservation.

`AudioFrameQueue` does not parse protocol fields and does not decode AAC. `ReceiverSession` remains the validation boundary; invalid `AUDIO` packets must not reach the queue through normal receiver flow.

### Dequeue Behavior

`TryDequeue` returns frames in FIFO order.

Rules:

- If a frame is available, remove it, assign it to `payload`, and return `true`.
- If the queue is empty, assign `null`, increment `UnderflowCount`, and return `false`.

Underflow is observable for future diagnostics, but Phase 1-D does not add logging or UI.

### Thread Safety

The queue must be safe for one network/session producer and one future decoder consumer.

Use the .NET standard library only:

```csharp
Queue<byte[]>
lock
```

Do not add lock-free data structures or background workers in this phase.

### ReceiverSession Integration

`ReceiverSession` should keep the existing `Action<byte[]> audioSink` seam from Phase 1-C.

Valid streaming `AUDIO` handling remains:

1. Validate the payload with `AudioPayloadValidator.ValidateAacPayload(payload)`.
2. Clone accepted payload for `LastAudioPayload`.
3. Deliver a separate clone to `audioSink`.
4. Increment `AudioFramesReceived`.
5. Return `null`.

Phase 1-D tests should pass `queue.Enqueue` as the sink and prove accepted frames are queued.

No `ReceiverSession` state names, packet types, or wire format values change.

### TcpReceiver Integration

`TcpReceiver` should keep its existing optional sink parameter.

Phase 1-D does not need a new public overload. Tests and future application code can pass `queue.Enqueue` into the existing API:

```csharp
AudioFrameQueue queue = new AudioFrameQueue(10);
using (TcpReceiver receiver = TcpReceiver.StartLoopback(queue.Enqueue))
{
    ...
}
```

`AudioFrameQueue` itself does not need `IDisposable` because it owns only managed memory.

---

## Testing Requirements

### AudioFrameQueue Unit Tests

Add tests proving:

1. Constructor rejects `capacity <= 0`.
2. `Enqueue` rejects `null`.
3. Enqueue then dequeue returns the same bytes.
4. Post-enqueue mutation of the caller's array does not mutate the queued frame.
5. Frames dequeue in FIFO order.
6. `Count` never exceeds `Capacity`.
7. Full queue drops the oldest frame and increments `DroppedFrames`.
8. Empty dequeue returns `false`, outputs `null`, and increments `UnderflowCount`.

### ReceiverSession Unit Tests

Add a focused test proving:

- Valid streaming `AUDIO` delivered through `queue.Enqueue` is available from `queue.TryDequeue`.

Existing Phase 1-C tests already cover invalid and out-of-order `AUDIO` not reaching the sink; keep them passing.

### TcpReceiver Loopback Test

Add or extend a loopback test to pass an enqueue sink into `TcpReceiver.StartLoopback`.

The sink may wrap `queue.Enqueue` to signal a `ManualResetEventSlim` after enqueueing:

```csharp
payload =>
{
    queue.Enqueue(payload);
    audioReceived.Set();
}
```

The test must prove:

- Android-style TCP fake `AUDIO` reaches the queue.
- The queued payload equals the original fake `AUDIO` payload.
- The normal `PING -> PONG` and `STOP_STREAM` flow still works.

Because `TcpReceiver` handles clients on thread-pool threads, use a bounded wait around queue observation. Do not use unbounded sleeps.

### Regression Checks

Local Linux checks remain:

```bash
python3 tools/check_docs_consistency.py
git diff --check HEAD
```

Protocol golden packets should remain unchanged:

```bash
python3 tools/protocol/generate_golden_packets.py --check
```

Windows CI must run:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Android CI should remain green even though Phase 1-D does not modify Android code.

---

## Acceptance Criteria

Phase 1-D is complete when:

- `AudioFrameQueue` exists in the Windows receiver code.
- The queue is bounded and rejects invalid capacity.
- Enqueue clones payload bytes before storing them.
- FIFO dequeue works.
- Full queue drops the oldest frame and records `DroppedFrames`.
- Empty dequeue records `UnderflowCount` and returns `false`.
- A valid streaming `AUDIO` packet can flow from `ReceiverSession` into `AudioFrameQueue` through the existing sink seam.
- A valid TCP loopback `AUDIO` packet can flow from `TcpReceiver` into `AudioFrameQueue` through the existing sink seam.
- No wire format, Android code, decoder, playback, discovery, UI, or configuration behavior changes.
- All CI workflows are green.
