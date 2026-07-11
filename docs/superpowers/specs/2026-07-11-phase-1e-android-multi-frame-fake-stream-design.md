# Phase 1-E Android Multi-Frame Fake Stream Design

**Status:** Draft for implementation

**Date:** 2026-07-11

**Scope:** Android sender and existing Windows receiver tests only.

---

## Goal

Phase 1-E upgrades the current one-frame fake `AUDIO` sender path into a deterministic multi-frame fake stream.

After this phase, the existing test sender path can run:

```text
HELLO -> WELCOME
START_STREAM -> STREAM_READY
AUDIO frame 1
AUDIO frame 2
AUDIO frame 3
PING -> PONG
STOP_STREAM
```

The Windows receiver should accept all fake frames and enqueue them through the Phase 1-D `AudioFrameQueue` seam.

This keeps the Android-to-Windows main link moving toward continuous streaming without adding real Android capture, real AAC encoding, Windows decoding, or playback.

---

## Non-Goals

Phase 1-E must not add:

- Android `MediaProjection`.
- Android `AudioPlaybackCaptureConfiguration` or `AudioRecord`.
- Android `MediaCodec` AAC encoding.
- Real audio capture threads, encoder threads, coroutines, services, or foreground notifications.
- Windows AAC decode.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- mDNS discovery.
- UI, tray, installer, or configuration changes.
- Protocol wire-format changes.
- New external dependencies.

The fake stream is deterministic test data only.

---

## Current Baseline

Phase 1-B added fake `AUDIO` payload builders and Android sends exactly one fake `AUDIO` packet after `STREAM_READY`.

Phase 1-C added a Windows receiver sink seam:

```csharp
Action<byte[]> audioSink
```

Phase 1-D added `AudioFrameQueue`, a bounded receiver-side FIFO for accepted `AUDIO` payloads.

Current Android `HandshakeClient.run` sends one `AUDIO` packet with:

```text
packet sequence = 3
packet timestamp = 123456789
frame number = 1
capture timestamp = 123456789
frame duration = 20 ms
encoded bytes = 11 22 33 44
```

The remaining gap is that the sender path still proves only a single-frame transfer. It does not prove deterministic consecutive frame numbering or receiver queue ordering across multiple `AUDIO` packets.

---

## Design

### Keep `HandshakeClient.run` as the test sender entry point

Do not introduce session managers, transports, services, or Android framework dependencies.

`HandshakeClient.run(input, output)` remains a synchronous stream-to-stream protocol exercise:

1. Send `HELLO`.
2. Read `WELCOME`.
3. Send `START_STREAM`.
4. Read `STREAM_READY`.
5. Send deterministic fake `AUDIO` frames.
6. Send `PING`.
7. Read matching `PONG`.
8. Send `STOP_STREAM`.

### Fake frame model

Add the smallest Android-side representation needed to describe fake frames:

```kotlin
private data class FakeAudioFrame(
    val frameNumber: Long,
    val captureTimestamp: Long,
    val encoded: ByteArray,
)
```

This matches the existing Android protocol helper signatures. Do not add a public model package for fake frames.

The fake frame list is hard-coded inside `HandshakeClient` or a private companion helper. No configuration is added.

Required fake frames:

| Packet Sequence | Packet Timestamp | Frame Number | Capture Timestamp | Encoded Bytes |
|---:|---:|---:|---:|---|
| 3 | 123456003 | 1 | 123456003 | `11 22 33 44` |
| 4 | 123456023 | 2 | 123456023 | `21 22 23 24` |
| 5 | 123456043 | 3 | 123456043 | `31 32 33 34` |

All frames use:

```text
Codec = AAC-LC
Frame Duration = 20 ms
```

The `PING` packet moves to sequence `6`. `STOP_STREAM` moves to sequence `7`.

### Why exactly three frames

Three frames are enough to prove:

- More than one `AUDIO` packet is sent.
- Frame numbers increase.
- Timestamps increase by one 20 ms frame duration.
- Receiver queue ordering is FIFO across multiple accepted payloads.

More frames would slow tests without proving a new behavior.

### Android packet writing behavior

`HandshakeClient` should loop over the deterministic fake frame list and write one `PacketTypeAudio` packet per frame.

Required shape:

```kotlin
for (frame in fakeAudioFrames) {
    output.write(PacketWriter.writePacket(
        ProtocolConstants.PacketTypeAudio,
        sequence,
        packetTimestamp,
        HandshakePayloads.audio(
            ProtocolConstants.CodecAacLc,
            frameNumber,
            captureTimestamp,
            20,
            encoded
        )
    ))
    output.flush()
}
```

The implementation may compute sequence and timestamp from the frame list instead of storing them separately, as long as the exact values in this spec are produced.

### Error handling

No new error model is added.

The existing behavior remains:

- If `WELCOME` is not success, return `false`.
- If `STREAM_READY` is not success, return `false`.
- If `PONG` is missing or does not echo the `PING` payload, return `false`.
- `IOException` and `PacketParseException` return `false`.

Because fake frames are local deterministic data, there is no runtime fake-frame failure path.

### Windows receiver integration

No Windows production code should change.

Existing Windows receiver behavior should already accept multiple `AUDIO` packets while the session is in `Streaming` state.

Phase 1-E should add/extend tests so the Windows TCP loopback path proves all three fake payloads can reach `AudioFrameQueue` in order before `PING` and `STOP_STREAM`.

---

## Testing Requirements

### Android unit tests

Update `HandshakeClientTest.runWritesHandshakePacketsOnSuccess` to prove:

1. `HELLO` is first.
2. `START_STREAM` is second.
3. Exactly three `AUDIO` packets are written before `PING`.
4. The three `AUDIO` payloads match the required fake frames exactly.
5. `PING` uses sequence `6` and keeps the existing ping payload shape.
6. `STOP_STREAM` uses sequence `7`.
7. No extra packet remains after `STOP_STREAM`.

Keep existing failure tests for busy receiver, protocol rejection, stream-ready failure, timeout, and pong payload mismatch.

### Windows TCP loopback test

Update or add a Windows TCP loopback test that sends the same three fake `AUDIO` payloads to `TcpReceiver.StartLoopback` with a queue-backed sink.

The test must prove:

1. The receiver accepts all three frames.
2. `AudioFrameQueue.Count == 3` after all callbacks arrive.
3. Dequeue returns payloads in frame-number order: 1, 2, 3.
4. `PING -> PONG` still works after the three `AUDIO` packets.
5. `STOP_STREAM` still closes the session path.

Because `TcpReceiver` handles clients on thread-pool threads, use a bounded wait. Do not use unbounded sleeps.

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

Android CI must run:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

---

## Acceptance Criteria

Phase 1-E is complete when:

- Android `HandshakeClient.run` writes exactly three deterministic fake `AUDIO` packets after `STREAM_READY` succeeds.
- The three fake frames use frame numbers `1`, `2`, `3`.
- The three fake frames use capture timestamps `123456003`, `123456023`, `123456043`.
- The three fake frames use encoded bytes `11 22 33 44`, `21 22 23 24`, and `31 32 33 34`.
- `PING` and `STOP_STREAM` sequence numbers are adjusted to `6` and `7`.
- Android unit tests assert exact packet order and payload bytes.
- Windows TCP loopback tests prove the three fake payloads reach `AudioFrameQueue` in FIFO order.
- No protocol wire-format values change.
- No real capture, encoder, decoder, playback, discovery, UI, or configuration code is added.
- All CI workflows are green.
