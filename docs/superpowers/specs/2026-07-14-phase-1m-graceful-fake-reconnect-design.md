# Phase 1-M Graceful Fake Reconnect Design

**Status:** Draft for implementation

**Date:** 2026-07-14

**Scope:** Deterministic graceful stop and a second manual fake-stream connection.

---

## Goal

Phase 1-M proves the Phase 1 roadmap requirement:

```text
Stop/reconnect works.
```

For this phase, reconnect means a user starts a second fake stream after the first fake stream completes normally. Automatic recovery from network loss or receiver restart remains Phase 1.1 work.

After this phase:

1. Android sends the existing `STOP_STREAM` packet.
2. Android waits for the Windows receiver to close the TCP stream before reporting `Success` and re-enabling the connect button.
3. A second button press can establish a new session with the same running receiver.
4. The Windows fake rendered-frame count advances from `3` to `6` after two manual runs.

---

## Non-Goals

Phase 1-M must not add:

- Automatic retries, retry delays, backoff, receiver-restart recovery, or network-change handling.
- A new `STOP_ACK` packet or any other protocol wire-format change.
- Discovery, saved receiver state, foreground service, capture, encoding, decoding, or real playback.
- New Android or Windows UI controls.
- New external dependencies.

---

## Current Baseline

The Android `HandshakeClient` already sends:

```text
HELLO -> START_STREAM -> 3 AUDIO -> PING -> STOP_STREAM
```

It currently returns `true` immediately after flushing `STOP_STREAM`.

The Windows `TcpReceiver` already:

- Moves the session to `Stopped` after `STOP_STREAM`.
- Leaves the session loop.
- Clears its active-session flag.
- Disposes the client socket and returns to listening.

The protocol requires the receiver to close TCP after `STOP_STREAM`. Returning Android success before observing that close creates a race: the UI can re-enable its button while Windows still considers the first session active, so an immediate second connection may receive `Receiver Busy`.

---

## Review-Discovered Default Port Drift

Phase 1-M review found that both implementations currently define `DefaultPort = 37373`, while the frozen project documentation consistently defines the Version 1 TCP port as `39888`:

- `docs/03-Protocol.md` defines `receiver.port` and the default network port as `39888`.
- `docs/08-Configuration.md` defines `network.tcpPort` as `39888`.
- `docs/10-Testing.md` uses TCP `39888` in the platform and configuration matrices.
- Deployment and discovery documentation also use `39888`.

Git history shows `37373` entered through the Phase 1-A implementation plan even though `docs/03-Protocol.md` already specified `39888`. Phase 1-J and Phase 1-K then treated that implementation value as authoritative.

Phase 1-M must correct both platform constants to `39888`, repair the stale Phase 1-A/1-J/1-K documentation, and add a static consistency check that fails when either implementation drifts from the canonical port.

This is an implementation/configuration correction. It does not change any protocol packet bytes.

---

## Alternatives

### A. Wait for receiver TCP close

After sending `STOP_STREAM`, Android reads once and requires end-of-stream.

Advantages:

- Uses the completion signal already required by `docs/03-Protocol.md`.
- Removes the reconnect race without timing constants.
- Needs no wire-format or Windows production change.

This is the selected approach.

### B. Add `STOP_ACK`

This would make completion explicit at the packet layer, but it changes the frozen Version 1 protocol and both implementations for behavior TCP FIN already provides.

Rejected.

### C. Delay or retry the second connection

A fixed delay is nondeterministic, and automatic retry policy belongs to Phase 1.1.

Rejected.

---

## Design

### Android stop completion

`HandshakeClient.run(...)` keeps the existing packet order. After writing and flushing `STOP_STREAM`, it performs one `InputStream.read()`.

The run succeeds only when that read returns `-1`, meaning the receiver closed its sending side cleanly. Any byte after `STOP_STREAM`, an `IOException`, a socket timeout, or a parse error makes the run fail.

`TcpHandshakeClient` already configures a 15-second socket read timeout, so a receiver that never closes cannot leave the UI blocked indefinitely.

No `MainActivity` change is needed. Its existing flow re-enables the connect button only after `ManualConnectController.connect(...)` returns. Waiting for EOF therefore makes the existing button lifecycle deterministic.

### Windows session reuse

No Windows session-lifecycle production change is expected. Add a loopback integration test around the existing `TcpReceiver` lifecycle:

1. Connect the first client and complete `HELLO` and `START_STREAM`.
2. Send `STOP_STREAM`.
3. Read and require TCP EOF.
4. Connect a second client to the same receiver instance.
5. Complete a second `HELLO` and `START_STREAM`.
6. Verify the second accepted session uses the next session ID.
7. Send `STOP_STREAM` and require EOF again.

Waiting for first-session EOF is the synchronization point. The test must not use sleeps or poll private state.

### Development fake end-to-end smoke test

Add an explicit development-only smoke procedure to `docs/10-Testing.md`:

1. Start the Windows receiver and confirm `Rendered frames: 0`.
2. Enter the Windows host in Android and press `Connect Fake Stream`.
3. Confirm Android reaches `Success` and Windows reaches `Rendered frames: 3`.
4. Press `Connect Fake Stream` again without restarting either app.
5. Confirm Android reaches `Success` and Windows reaches `Rendered frames: 6`.

This procedure validates the current fake transport path. It does not replace the release end-to-end test for discovery, capture, real AAC, audible playback, or latency.

### Canonical default port alignment

Set both implementation constants to the documented Version 1 TCP port:

```text
ProtocolConstants.DefaultPort = 39888
```

Extend `tools/check_docs_consistency.py` to parse the Android and Windows `DefaultPort` declarations and require `39888`.

Correct the stale numeric examples in:

- `docs/superpowers/plans/2026-07-08-phase-1a-network-handshake.md`
- `docs/superpowers/specs/2026-07-13-phase-1j-windows-default-lan-listener-design.md`
- `docs/superpowers/specs/2026-07-13-phase-1k-android-manual-fake-connect-ui-design.md`

---

## Error Behavior

- EOF after `STOP_STREAM`: report `Success`.
- Unexpected trailing data after `STOP_STREAM`: report `Failed`.
- Receiver does not close before the socket timeout: report `Failed`.
- Receiver closes with an I/O error: report `Failed`.
- A normal second connection is treated as a new session, not a continuation of the first session.

---

## Testing Requirements

### Android JVM tests

Update `HandshakeClientTest` to prove:

1. The existing successful fake stream still writes all packets in order and succeeds when response input reaches EOF after `PONG`.
2. A byte remaining after `PONG` causes the run to fail after `STOP_STREAM`, proving success requires receiver close.
3. An input that throws `IOException` instead of returning EOF after the successful `PONG` causes the run to fail, covering receiver close timeout or reset.
4. Existing busy, protocol rejection, initial-response timeout, stream rejection, and invalid PONG behavior remains unchanged.

### Windows integration tests

Update `TcpReceiverTests` to prove:

1. The receiver closes the first TCP session after `STOP_STREAM`.
2. The same receiver accepts a second handshake after that EOF.
3. Session IDs advance from `1` to `2`.
4. The second session also closes after `STOP_STREAM`.

### Repository checks

First prove the new static port check fails against the old implementation constants. After correcting them, the same check must pass.

Run:

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

Phase 1-M is complete when:

- Android reports fake-stream success only after observing receiver TCP EOF following `STOP_STREAM`.
- A receiver that sends trailing data or does not close does not produce Android `Success`.
- One running `TcpReceiver` accepts a second session after the first session stops.
- Sequential accepted sessions have distinct incrementing session IDs.
- `docs/10-Testing.md` contains exact two-run fake E2E steps with expected `3` then `6` rendered frames.
- Android and Windows both define `ProtocolConstants.DefaultPort = 39888`.
- The Phase 1-A, Phase 1-J, and Phase 1-K documents no longer name `37373` as the OpenAudioLink default port.
- Docs CI parses both implementation constants and rejects future default-port drift.
- Existing protocol packets and fake audio behavior remain unchanged.
- No sleep, retry policy, protocol change, UI change, or dependency is added.
- Docs, Android, and Windows CI workflows on the phase branch are green.
