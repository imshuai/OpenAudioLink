# Phase 1-K Android Manual Fake Connect UI Design

**Status:** Draft for implementation

**Date:** 2026-07-13

**Scope:** Android sender manual host input and fake TCP stream trigger only.

---

## Goal

Phase 1-K gives the Android sender app a minimal manual connection screen.

After this phase, a user can launch the Android app, enter the Windows receiver host or IP, tap a button, and the app will run the existing fake TCP handshake/audio flow through `TcpHandshakeClient.connect(host)` on the protocol default port.

This moves the project from library-only Android sender behavior to a manually testable Android-to-Windows fake stream path.

---

## Non-Goals

Phase 1-K must not add:

- Receiver discovery, mDNS, UDP, pairing, QR codes, or cached receivers.
- MediaProjection, AudioPlaybackCapture, foreground service, AAC encoder, or real audio capture.
- Compose, ViewModel, coroutines, AndroidX, Robolectric, instrumentation tests, or new dependencies.
- Saved settings, `SharedPreferences`, host history, validation beyond trimming empty input, or richer error messages.
- Windows receiver changes.
- Protocol wire-format changes.

---

## Current Baseline

Android already has the network pieces needed for a fake stream:

- `sender-android/app/src/main/java/com/openaudiolink/network/TcpHandshakeClient.kt` connects to `ProtocolConstants.DefaultPort` and runs `HandshakeClient`.
- `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt` sends `HELLO`, `START_STREAM`, three fake `AUDIO` frames, `PING`, validates `PONG`, then sends `STOP_STREAM`.
- `sender-android/app/src/main/AndroidManifest.xml` already declares `android.permission.INTERNET`.

The app entry point is still a skeleton `TextView` that only displays:

```text
OpenAudioLink Sender
```

Windows Phase 1-J already starts the normal receiver app on `IPAddress.Any:39888`, matching Android's default port.

---

## Design

### UI

Replace the skeleton `TextView` with a small native Android View tree built in `MainActivity`:

- Title: `OpenAudioLink Sender`.
- `EditText` for receiver host/IP.
- `Button` labeled `Connect Fake Stream`.
- `TextView` status line.

Initial status:

```text
Idle
```

### Connection behavior

When the button is tapped:

1. Trim the host text.
2. If it is empty, show `Failed` and do not start a network thread.
3. Otherwise show `Connecting`, disable the button, and run `TcpHandshakeClient().connect(host)` on a background `Thread`.
4. On completion, update the UI on the main thread:
   - `Success` if `connect` returns `true`.
   - `Failed` if it returns `false` or throws.
5. Re-enable the button after completion.

The UI uses the existing default port implicitly through `TcpHandshakeClient.connect(host)`.

### Testable helper

Keep Android framework code thin by adding one tiny JVM-testable helper:

```kotlin
enum class ManualConnectStatus {
    Idle,
    Connecting,
    Success,
    Failed,
}

class ManualConnectController(
    private val connect: (String) -> Boolean,
) {
    fun connect(host: String): ManualConnectStatus {
        val trimmed = host.trim()
        if (trimmed.isEmpty()) return ManualConnectStatus.Failed
        return if (connect(trimmed)) ManualConnectStatus.Success else ManualConnectStatus.Failed
    }
}
```

`MainActivity` owns threading and UI updates. The helper only handles trim/empty/success/failure status mapping, so JVM unit tests can cover the new decision logic without Android UI test dependencies.

---

## Testing Requirements

Add Android JVM unit tests proving:

1. Empty or whitespace-only host returns `ManualConnectStatus.Failed` and does not call the connector.
2. Non-empty host is trimmed before calling the connector.
3. Connector `true` maps to `ManualConnectStatus.Success`.
4. Connector `false` maps to `ManualConnectStatus.Failed`.
5. Connector exceptions map to `ManualConnectStatus.Failed`.

No Robolectric or instrumentation tests are added in this phase.

Manual Android device/emulator validation after CI can be:

1. Start the Windows receiver app on the LAN.
2. Open Android sender.
3. Enter the Windows receiver IP.
4. Tap `Connect Fake Stream`.
5. Confirm Android status becomes `Success`.

---

## Regression Checks

Local Linux checks remain:

```bash
python3 tools/check_docs_consistency.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
```

Android CI must run:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Windows CI must remain green:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

---

## Acceptance Criteria

Phase 1-K is complete when:

- `MainActivity` shows manual host input, a connect button, and a status text.
- Tapping the button calls the existing `TcpHandshakeClient().connect(host)` on a background thread.
- The button is disabled while connecting and re-enabled after completion.
- UI status transitions cover `Idle`, `Connecting`, `Success`, and `Failed`.
- Empty host input fails locally without starting a network call.
- New Android JVM tests cover the manual connection decision logic.
- Existing protocol, docs, Windows, and Android checks stay green.
- No discovery, real audio capture, saved settings, new dependencies, Windows changes, or protocol changes are added.
- All CI workflows on the phase branch are green.
