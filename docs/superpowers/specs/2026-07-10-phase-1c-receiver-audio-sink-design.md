# Phase 1-C Receiver Audio Sink Design

**Status:** Draft for implementation

**Date:** 2026-07-10

**Scope:** Windows receiver only.

---

## Goal

Phase 1-C connects the already validated fake `AUDIO` packet path to a minimal receiver-side audio sink seam.

After this phase, the Windows receiver can:

```text
HELLO -> WELCOME
START_STREAM -> STREAM_READY
AUDIO(valid AAC payload) -> validated -> sink callback
PING -> PONG
STOP_STREAM -> close
```

This prepares the receiver for a future real AAC decoder/playback pipeline without adding decoder or playback code in Phase 1-C.

---

## Non-Goals

Phase 1-C must not add:

- AAC decoding.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- Android capture or encoder work.
- mDNS discovery.
- UI, tray, installer, or configuration changes.
- Background queues, worker threads, jitter buffers, or playback clocks.

The sink seam is intentionally synchronous and minimal.

---

## Current Baseline

Phase 1-B already implemented:

- Cross-platform fake `AUDIO` payload builders.
- `AudioPayloadValidator.ValidateAacPayload`.
- `ReceiverSession.AudioFramesReceived`.
- `ReceiverSession.LastAudioPayload`.
- TCP loopback coverage where `AUDIO` is sent before `PING`.
- Android `HandshakeClient` fake `AUDIO` send path.

The remaining gap is that accepted `AUDIO` frames are only recorded inside `ReceiverSession`; they are not handed to a receiver-side consumer.

---

## Design

### Sink Shape

Use the .NET standard delegate type:

```csharp
Action<byte[]> audioSink
```

No new production interface is introduced in Phase 1-C. A delegate is enough for tests now and can point at a future decoder enqueue method in the decoder/playback phase.

### ReceiverSession Construction

`ReceiverSession` gains an overload that accepts an optional sink:

```csharp
public ReceiverSession(ulong sessionId)
    : this(sessionId, null)
{
}

public ReceiverSession(ulong sessionId, Action<byte[]> audioSink)
{
    this.sessionId = sessionId;
    this.audioSink = audioSink ?? (_ => { });
    State = ReceiverSessionState.WaitingForHello;
}
```

The existing constructor remains source-compatible.

### Valid AUDIO Handling

When `ReceiverSession` is in `Streaming` state and receives `PacketTypeAudio`:

1. Parse header and payload as today.
2. Validate payload with `AudioPayloadValidator.ValidateAacPayload(payload)`.
3. Clone the accepted payload for `LastAudioPayload`.
4. Pass a separate clone to `audioSink`.
5. Increment `AudioFramesReceived`.
6. Return `null` because `AUDIO` has no response packet.

Required implementation shape:

```csharp
AudioPayloadValidator.ValidateAacPayload(payload);
byte[] acceptedPayload = (byte[])payload.Clone();
audioSink((byte[])acceptedPayload.Clone());
LastAudioPayload = acceptedPayload;
AudioFramesReceived++;
return null;
```

The separate sink clone prevents a sink implementation from mutating `LastAudioPayload`.

### Invalid AUDIO Handling

If validation fails:

- `PacketParseException` is thrown.
- `audioSink` is not called.
- `AudioFramesReceived` is not incremented.
- `LastAudioPayload` is not updated.

If `AUDIO` arrives before streaming starts, existing session-order validation still throws and the sink is not called.

### Sink Exception Policy

Phase 1-C does not introduce a new error model for sink failures.

The sink is expected to be fast and non-throwing. If it throws, the exception propagates to the caller. Real decoder/playback failure handling belongs to the future real audio pipeline phase.

---

## TcpReceiver Integration

`TcpReceiver` accepts the same optional sink and passes it to each accepted `ReceiverSession`.

Required public shape:

```csharp
public static TcpReceiver Start(IPAddress address, int port, Action<byte[]> audioSink = null)

public static TcpReceiver StartLoopback(Action<byte[]> audioSink = null)
```

The private constructor stores the sink:

```csharp
private readonly Action<byte[]> audioSink;

private TcpReceiver(TcpListener listener, Action<byte[]> audioSink)
{
    this.listener = listener;
    this.audioSink = audioSink ?? (_ => { });
    Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    ThreadPool.QueueUserWorkItem(_ => AcceptLoop());
}
```

`Handle` constructs sessions with:

```csharp
ReceiverSession session = new ReceiverSession((ulong)Interlocked.Increment(ref nextSessionId), audioSink);
```

Existing callers that do not pass a sink keep the Phase 1-B behavior.

---

## Testing Requirements

### ReceiverSession Unit Tests

Add tests proving:

1. Valid streaming `AUDIO` calls the sink exactly once with the valid payload.
2. The sink receives a clone; mutating the sink payload does not mutate `LastAudioPayload`.
3. Invalid streaming `AUDIO` does not call the sink and does not increment `AudioFramesReceived`.
4. `AUDIO` before `START_STREAM` does not call the sink.

Existing Phase 1-B tests must continue to pass.

### TcpReceiver Loopback Test

Extend the TCP loopback test to pass a sink callback into `TcpReceiver.StartLoopback`.

The test must prove the valid fake `AUDIO` packet sent over TCP reaches the sink exactly once.

Because `TcpReceiver` handles clients on thread-pool threads, the test should use `ManualResetEventSlim` or an equivalent bounded wait. It must not sleep unboundedly.

### Regression Checks

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

Android CI is not expected to change, but must remain green.

---

## Acceptance Criteria

Phase 1-C is complete when:

- `ReceiverSession` can be constructed with or without an audio sink.
- Valid streaming `AUDIO` is validated before sink invocation.
- Valid streaming `AUDIO` reaches the sink exactly once.
- Sink mutation cannot change `LastAudioPayload`.
- Invalid or out-of-order `AUDIO` never reaches the sink.
- `TcpReceiver` passes valid TCP `AUDIO` payloads to the configured sink.
- No real audio decode/playback/capture/discovery/UI code is added.
- All CI workflows are green.
