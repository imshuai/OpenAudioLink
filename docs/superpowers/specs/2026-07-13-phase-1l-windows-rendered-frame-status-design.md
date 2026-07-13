# Phase 1-L Windows Rendered Frame Status Design

**Status:** Draft for implementation

**Date:** 2026-07-13

**Scope:** Windows receiver UI status for fake rendered frame count only.

---

## Goal

Phase 1-L makes the Windows receiver show that the fake stream was actually received and rendered.

After this phase, the Windows app still listens on the protocol default TCP port and additionally displays:

```text
Rendered frames: 0
```

When the Android sender runs the existing fake stream, the Windows label updates to:

```text
Rendered frames: 3
```

This gives the manual Android-to-Windows validation path visible confirmation on both sides.

---

## Non-Goals

Phase 1-L must not add:

- Real audio playback, WASAPI, Media Foundation, NAudio, device selection, or volume controls.
- Discovery, mDNS, pairing, reconnect policy, or session history.
- A logging subsystem, diagnostics panel, settings UI, tray behavior, or installer work.
- Android sender changes.
- Protocol wire-format changes.
- New external dependencies.

---

## Current Baseline

The Windows app already starts `ReceiverRuntime` from `MainForm` on `IPAddress.Any:ProtocolConstants.DefaultPort`.

`ReceiverRuntime` already owns:

- `AudioFrameQueue`
- `FakeAacDecoder`
- `FakeAudioRenderer`
- `TcpReceiver`

For each `AUDIO` packet, the receiver callback enqueues the payload and immediately drains the fake queue into `FakeAudioRenderer`. `FakeAudioRenderer.RenderedCount` already exposes the rendered fake PCM frame count, but `MainForm` does not display it.

---

## Design

### Runtime notification

Add an optional `Action<int>` callback to `ReceiverRuntime.Start(...)`:

```csharp
public static ReceiverRuntime Start(IPAddress address, int port, int queueCapacity = 8, Action<int> renderedCountChanged = null)
```

After the existing queue drain, invoke the callback with `renderer.RenderedCount`.

`StartLoopback()` keeps its current public call shape and passes no callback.

### MainForm UI

`MainForm` adds a second label below the listener port label:

```text
Rendered frames: 0
```

`MainForm` starts `ReceiverRuntime` with a callback that updates this label to:

```text
Rendered frames: <count>
```

The callback may run from the TCP receiver thread. If the label has a WinForms handle and requires marshaling, use `BeginInvoke`; otherwise update directly. This keeps the normal UI path safe while letting existing no-message-loop unit tests observe the label text.

### Error behavior

If updating the label races with form disposal, ignore the update. This is only best-effort UI status and must not break receiver disposal.

---

## Testing Requirements

Update WinForms tests to prove:

1. New `MainForm` instances show `Rendered frames: 0`.
2. A loopback TCP fake stream through `MainForm` updates visible text to `Rendered frames: 3`.
3. Existing default-port startup and dispose behavior remain covered.

The fake stream test must send:

- `HELLO`
- `START_STREAM`
- three `AUDIO` packets
- `PING` and expect matching `PONG`
- `STOP_STREAM`

Using `PING/PONG` as the synchronization point avoids sleeps: the receiver cannot reply to `PING` until it has processed the previous `AUDIO` packets.

---

## Regression Checks

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

Phase 1-L is complete when:

- `MainForm` displays both the listener port and `Rendered frames: 0` on startup.
- A fake stream containing three `AUDIO` packets updates the visible WinForms text to `Rendered frames: 3`.
- `ReceiverRuntime.StartLoopback()` remains source-compatible.
- Existing fake decode/render behavior remains unchanged.
- Existing protocol, docs, Windows, and Android checks stay green.
- No real playback, discovery, Android changes, protocol changes, or new dependency is added.
- All CI workflows on the phase branch are green.
