# Phase 1-I Windows App Runtime Lifecycle Design

**Status:** Draft for implementation

**Date:** 2026-07-13

**Scope:** WinForms application lifecycle wiring for the existing fake Windows receiver runtime.

---

## Goal

Phase 1-I makes the Windows application start the existing fake receiver runtime when the main window is created and stop it when the main window is disposed.

After this phase, launching the WinForms app starts the tested fake receiver path:

```text
MainForm -> ReceiverRuntime -> TcpReceiver audio sink -> AudioFrameQueue -> FakeAacDecoder -> FakePcmFrame -> FakeAudioRenderer
```

The UI also shows the actual bound TCP listening port so a developer can connect a sender or loopback test without reading logs.

---

## Non-Goals

Phase 1-I must not add:

- Real AAC decode.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- Android sender changes.
- Protocol wire-format changes.
- Discovery, mDNS, UDP, pairing, or receiver identity.
- Configuration files, fixed listening ports, settings UI, or persisted receiver names.
- Tray behavior, installer work, crash reporting, runtime logs, or diagnostics bundles.
- UI timers, live counters, background UI polling, or cross-thread UI updates.
- New external dependencies.

---

## Current Baseline

Phase 1-H added `ReceiverRuntime`, which owns and composes the fake receiver runtime. It exposes `Port`, `Queue`, and `Renderer`, and it disposes the owned `TcpReceiver`.

The Windows app still starts only `MainForm`. `MainForm` currently shows a skeleton label and does not own a runtime.

---

## Design

### MainForm owns one runtime

`MainForm` constructs one `ReceiverRuntime` during form construction:

```csharp
private readonly ReceiverRuntime runtime;

public MainForm()
{
    runtime = ReceiverRuntime.StartLoopback();
    ListeningPort = runtime.Port;
    Text = "OpenAudioLink Receiver";
}
```

`StartLoopback()` intentionally uses port `0`; the operating system selects a free local port. This keeps Phase 1-I reliable in CI and on developer machines before configuration and discovery exist.

### Public status surface

Add one read-only property:

```csharp
public int ListeningPort { get; }
```

This is the smallest testable UI-facing state. Tests and developers can use it to connect to the running app instance. The UI label displays:

```text
Listening on TCP port <ListeningPort>
```

No renderer counts, queue depths, sender identity, or session state are shown in this phase.

### Disposal

Override `Dispose(bool disposing)` and dispose the owned runtime when `disposing` is `true`:

```csharp
protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        runtime.Dispose();
    }

    base.Dispose(disposing);
}
```

No restart behavior is added. Closing and reopening the form creates a new runtime.

### Threading

`MainForm` still runs on the WinForms UI thread. Network and session work remain inside `TcpReceiver` background workers created by the existing receiver implementation.

Phase 1-I does not update UI controls from receiver callbacks, so no `Control.Invoke`, timers, synchronization context, or polling loop is needed.

---

## Testing Requirements

Add WinForms tests proving:

1. Constructing `MainForm` starts a receiver and exposes a non-zero `ListeningPort`.
2. The form label contains `Listening on TCP port <ListeningPort>`.
3. A loopback TCP client can connect to `ListeningPort`, send `HELLO`, and receive `WELCOME`.
4. Disposing `MainForm` stops the listener, so a new loopback connection to the old port fails.

Run WinForms tests on an STA thread inside the test helper. Do not add test-only production constructors or test-only production hooks.

Use bounded waits for connection failure checks. Do not add sleeps.

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

Phase 1-I is complete when:

- `MainForm` starts one `ReceiverRuntime` during construction.
- `MainForm.ListeningPort` exposes the actual bound TCP port.
- The UI label displays the listening port.
- `MainForm.Dispose(bool)` disposes the owned runtime.
- WinForms tests prove app startup can complete a loopback `HELLO -> WELCOME` exchange through the form-owned runtime.
- WinForms tests prove disposing the form closes the listener.
- Existing protocol, receiver, runtime, Android, and docs checks stay green.
- No real decode, playback, discovery, configuration, tray behavior, timers, live UI counters, protocol changes, Android changes, dependencies, or large UI refactors are added.
- All CI workflows are green.
