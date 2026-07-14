# Phase 1-J Windows Default LAN Listener Design

**Status:** Draft for implementation

**Date:** 2026-07-13

**Scope:** Windows app default listening address and port only.

---

## Goal

Phase 1-J makes the Windows receiver app listen on the protocol default TCP port from the normal WinForms startup path.

After this phase, launching the Windows app starts the existing fake receiver runtime on:

```text
IPAddress.Any : ProtocolConstants.DefaultPort
```

This aligns the Windows receiver with the Android sender's existing `TcpHandshakeClient` default port (`39888`) and moves the project one step closer to Android-to-Windows LAN validation.

---

## Non-Goals

Phase 1-J must not add:

- Configuration files or configurable ports.
- Discovery, mDNS, UDP, pairing, or receiver identity.
- Android UI or Android sender changes.
- Real AAC decode.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- Firewall rule management.
- Tray behavior, installer work, logs, diagnostics, or settings UI.
- Protocol wire-format changes.
- New external dependencies.

---

## Current Baseline

Phase 1-I wires `MainForm` to start and dispose `ReceiverRuntime`, but it uses `ReceiverRuntime.StartLoopback()`.

That means the app currently binds only loopback on an OS-selected dynamic port. This is good for isolated tests, but Android's production TCP sender defaults to `ProtocolConstants.DefaultPort = 39888`, so the normal app path is not yet LAN-addressable on the protocol port.

Both platforms already define the same default port:

- Windows: `receiver-windows/src/OpenAudioLink/Protocol/ProtocolConstants.cs` → `DefaultPort = 39888`.
- Android: `sender-android/app/src/main/java/com/openaudiolink/protocol/ProtocolConstants.kt` → `DefaultPort = 39888`.

---

## Design

### MainForm default runtime start

Change `MainForm` to start the runtime with the protocol default listener:

```csharp
runtime = ReceiverRuntime.Start(IPAddress.Any, ProtocolConstants.DefaultPort);
ListeningPort = runtime.Port;
```

`ListeningPort` should remain the actual bound port, which should equal `ProtocolConstants.DefaultPort` for the normal app path.

`ReceiverRuntime.StartLoopback()` remains unchanged for tests that need an isolated dynamic port.

### UI status

Keep the existing status text shape:

```text
Listening on TCP port 39888
```

No host/address list is shown in this phase. Showing every LAN address would add UI and network-interface complexity before discovery exists.

### Error behavior

If port `39888` is already in use, construction of `MainForm` may fail with the existing `SocketException` from `TcpListener.Start()`.

Phase 1-J does not add a fallback dynamic port. A silent fallback would make Android default connection fail while the UI still appears healthy.

---

## Testing Requirements

Update WinForms tests to prove:

1. `MainForm.ListeningPort == ProtocolConstants.DefaultPort`.
2. The label contains `Listening on TCP port 39888`.
3. A loopback TCP client can connect to the default port and complete `HELLO -> WELCOME`.
4. Disposing `MainForm` closes the listener on the default port.

Keep runtime tests that use `ReceiverRuntime.StartLoopback()` unchanged.

Use bounded waits. Do not add sleeps.

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

Phase 1-J is complete when:

- `MainForm` starts `ReceiverRuntime` on `IPAddress.Any` and `ProtocolConstants.DefaultPort`.
- `MainForm.ListeningPort` equals `ProtocolConstants.DefaultPort` in the normal app path.
- The UI label displays the default port.
- Existing `ReceiverRuntime.StartLoopback()` behavior remains available for isolated tests.
- WinForms tests prove `HELLO -> WELCOME` works through the default port.
- WinForms tests prove disposing the form closes the default-port listener.
- Existing protocol, receiver, Android, and docs checks stay green.
- No configuration, discovery, Android UI, real decode, playback, firewall management, protocol change, or new dependency is added.
- All CI workflows on the phase branch are green.
