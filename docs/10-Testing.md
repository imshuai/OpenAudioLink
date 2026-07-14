# docs/10-Testing.md

# Testing Strategy

Version: 1.0

Status: Draft

---

# Overview

This document defines the testing strategy for OpenAudioLink.

OpenAudioLink has two reference applications:

- Android Sender
- Windows Receiver

They communicate through the OpenAudioLink Protocol and share the same quality goals:

- Low latency
- Stable long-duration playback
- Predictable failure handling
- Windows 7 compatibility
- Android 10 compatibility
- Local-network operation without cloud dependencies

Testing must verify both individual subsystems and the full sender-to-receiver workflow.

---

# Testing Goals

The test strategy is designed to prove that:

1. The protocol is wire-compatible across implementations.
2. The Android sender can capture, encode and transmit playback audio.
3. The Windows receiver can discover, accept, decode and play a stream.
4. Audio remains stable under normal LAN jitter and temporary failures.
5. Configuration, deployment and upgrade behavior are safe.
6. Regressions are caught at the smallest practical layer.

---

# Non-Goals

Version 1 tests do not attempt to validate:

- DLNA compatibility
- AirPlay compatibility
- Chromecast compatibility
- Bluetooth behavior
- WAN streaming quality
- Multi-room synchronization
- DRM-protected application audio capture

These areas are outside Version 1 scope.

---

# Testing Principles

OpenAudioLink follows these testing principles.

## Test Small Boundaries First

Pure logic must be tested without Android devices, Windows audio devices or network services.

Examples:

- Packet parsing
- Packet serialization
- Configuration validation
- Receiver cache merging
- State transitions

---

## Test Protocol With Bytes

Protocol tests must assert exact binary output.

The following values must be verified directly:

- Magic number
- Header size
- Byte order
- Packet type
- Sequence number
- Timestamp
- Payload length
- Audio payload layout

String or object-level tests are not enough for wire compatibility.

---

## Prefer Fakes Over Real Devices In Unit Tests

Unit tests should use fake implementations for:

- Audio capture
- Audio renderer
- TCP sockets
- Discovery publisher
- Clock
- Configuration storage

Real devices are reserved for integration, compatibility and release validation.

---

## Manual Tests Must Be Explicit

Some behavior cannot be fully automated in Version 1.

Examples:

- Android MediaProjection permission dialog
- Foreground service notification behavior
- Windows firewall prompt
- Audio device hot-plug
- Audible playback quality

Manual tests must have clear steps and expected results.

---

## Every Bug Gets A Regression Test

When a bug is fixed, the fix should include the smallest test that would have failed before the fix.

Preferred order:

1. Unit test
2. Component test
3. Integration test
4. Manual regression checklist entry

Manual-only regressions should be avoided unless automation is impractical.

---

# Test Levels

OpenAudioLink uses five test levels.

```text
Static Checks

↓

Unit Tests

↓

Component Tests

↓

Integration Tests

↓

End-to-End Validation
```

---

# Static Checks

Static checks verify that source, configuration and documentation remain consistent.

Recommended checks:

- Markdown links are valid.
- Documented default ports match configuration defaults.
- Android minimum version remains Android 10 / API 29.
- Windows target remains Windows 7 SP1 or later.
- Protocol constants match implementation constants.
- Public examples use valid JSON.
- Release notes include test status.

Static checks should run in pull requests.

---

# Unit Tests

Unit tests run without real network, real audio devices or platform permission dialogs.

Unit tests should be deterministic and fast.

Recommended maximum duration:

```text
< 5 seconds per module
```

Recommended coverage target for core logic:

```text
>= 80%
```

Coverage is a guardrail, not a replacement for meaningful assertions.

---

# Component Tests

Component tests verify one subsystem with fake neighbors.

Examples:

| Component | Fake Neighbor |
|----------|---------------|
| Protocol Engine | Fake transport |
| Transport Client | Fake TCP server |
| Audio Decoder | Sample AAC frames |
| Audio Renderer | Fake PCM sink |
| Discovery Manager | Fake mDNS/broadcast providers |
| Configuration Manager | Temporary file system |

Component tests should verify behavior, not implementation details.

---

# Integration Tests

Integration tests combine multiple real subsystems.

Examples:

- TCP loopback sender and receiver
- Real packet parser with real session state machine
- Real configuration files with application startup
- Real discovery advertisement on local test network
- Real decoder with recorded AAC samples

Integration tests may be slower than unit tests but should still be repeatable.

---

# End-to-End Validation

End-to-end validation uses the Android Sender and Windows Receiver together.

The minimal happy path is:

```text
Install Windows Receiver

↓

Start Receiver

↓

Advertise Discovery Service

↓

Install Android Sender

↓

Grant MediaProjection Permission

↓

Discover Receiver

↓

Connect

↓

Send Audio

↓

Hear Playback On Windows

↓

Stop Stream Cleanly
```

End-to-end validation must be performed before public releases.

---

# Development Fake End-to-End Smoke Test

This is a development fake smoke test to run before real capture, codec and playback are available. It does not replace release end-to-end validation.

Prerequisites:

- The Windows and Android devices are on the same reachable network.
- TCP port 39888 is reachable.
- The current Windows and Android builds are installed.

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Start the Windows application. | `Listening on TCP port 39888` and `Rendered frames: 0` are displayed. |
| 2 | On Android, enter the Windows IP address or host and tap `Connect Fake Stream`. | The status changes from `Connecting` to `Success`. |
| 3 | Check the Windows application. | `Rendered frames: 3` is displayed. |
| 4 | Without restarting either application, tap `Connect Fake Stream` again. | The status changes from `Connecting` to `Success`, and the previous session does not cause a busy failure. |
| 5 | Check the Windows application. | `Rendered frames: 6` is displayed. |

This smoke test proves only fake packet flow, graceful `STOP_STREAM` close and manual reconnect. It does not replace release end-to-end validation for discovery, MediaProjection, real AAC, audible playback or latency.

---

# Platform Test Matrix

Version 1 compatibility matrix.

| Area | Required |
|------|----------|
| Android Minimum | Android 10 / API 29 |
| Android Current | Latest project-supported Android release |
| Windows Minimum | Windows 7 SP1 x64 |
| Windows Current | Windows 10 / Windows 11 |
| Network | Same LAN |
| Transport | TCP 39888 |
| Discovery | mDNS, UDP broadcast, manual IP |
| Audio Format | AAC-LC, 48 kHz, stereo, 16-bit PCM output |

---

# Android Device Matrix

Recommended device coverage.

| Device Family | Required |
|---------------|----------|
| Pixel | Yes |
| Samsung | Yes |
| Xiaomi | Yes |
| OnePlus | Yes |
| Motorola | Recommended |

Recommended Android versions:

- Android 10
- Android 12
- Android 13
- Android 14
- Latest project-supported Android release

Android 10 is mandatory because AudioPlaybackCapture starts at API 29.

---

# Windows Matrix

Recommended receiver coverage.

| Windows Version | Required |
|-----------------|----------|
| Windows 7 SP1 x64 | Release gate |
| Windows 8.1 | Compatibility gate |
| Windows 10 | Release gate |
| Windows 11 | Release gate |

Windows 7 validation must include:

- Application startup
- Audio output
- Firewall rule behavior
- mDNS advertisement
- TCP streaming
- Installer and uninstaller

---

# Network Matrix

Recommended network coverage.

| Scenario | Expected Result |
|----------|-----------------|
| Android and Windows on same Wi-Fi | Discovery succeeds |
| Android on Wi-Fi, Windows on Ethernet | Discovery succeeds if multicast is routed |
| mDNS blocked | UDP broadcast or manual IP works |
| Windows firewall enabled | Required ports are allowed or diagnostics explain failure |
| Receiver IP changes | Sender refreshes discovery/cache |
| Wi-Fi reconnect | Discovery and streaming recover |
| WireGuard/manual IP | Direct unicast connection works |

---

# Protocol Tests

Protocol tests are the highest-priority automated tests.

The protocol is the compatibility contract between implementations.

---

# Header Tests

The common header must be validated exactly.

Required tests:

- Header size is 24 bytes.
- Integers are big endian.
- Magic number is accepted only when exact.
- Major version mismatch is rejected.
- Minor version mismatch is negotiated through capabilities.
- Unknown required flags are rejected.
- Unknown optional flags are ignored if safe.
- Payload length is bounded by maximum packet size.
- Payload length shorter than required payload header is rejected.
- Packet type dispatch never reads beyond the payload.

---

# Packet Validation Tests

Malformed packets must never crash the receiver.

Required invalid inputs:

- Empty packet
- Partial header
- Invalid magic
- Unsupported major version
- Unknown packet type
- Negative length equivalent after integer conversion
- Payload length larger than available bytes
- Payload length larger than 64 KB
- Valid header with invalid payload
- Extra trailing bytes in frame buffer

Expected behavior:

- Packet is rejected.
- Error is logged.
- Recoverable errors keep the connection alive.
- Protocol-incompatible errors close the session cleanly.

---

# Handshake Tests

Required handshake cases:

| Case | Expected Result |
|------|-----------------|
| Valid HELLO | WELCOME returned |
| Unsupported major version | ERROR or connection close |
| Supported major, newer minor | Capability negotiation |
| Receiver idle | Session accepted |
| Receiver busy | Busy result returned |
| Missing required capability | Stream rejected |
| Timeout before HELLO | Connection closed |

---

# Audio Packet Tests

AUDIO packet payload layout:

| Offset | Size | Field |
|--------:|-----:|------|
| 0 | 1 | Codec |
| 1 | 4 | Frame Number |
| 5 | 8 | Capture Timestamp |
| 13 | 2 | Frame Duration |
| 15 | 4 | Encoded Size |
| 19 | N | Encoded Data |

Required tests:

- `PayloadLength == 19 + EncodedSize`.
- Codec must be AAC-LC for Version 1 streams.
- Frame Number increments by one per encoded frame.
- Capture Timestamp uses a monotonic sender clock.
- AAC-LC represents exactly 1024 samples/channel at 48 kHz
  (21.333333... ms); the integer wire Frame Duration is nominally 21 ms.
- Encoded Data contains exactly one raw AAC-LC access unit and no ADTS or
  codec-configuration bytes.
- Encoded Size cannot exceed packet payload length.
- Corrupted encoded data is dropped without killing the session.
- Queue overflow drops old audio instead of increasing latency.
- Queue underflow is reported as playback starvation.

---

# Heartbeat Tests

Heartbeat behavior:

| Value | Target |
|-------|--------|
| PING interval | 5 seconds |
| PONG response | Immediate |
| PONG processing delay | < 5 ms |
| Timeout | 15 seconds |

Required cases:

- PING produces identical PONG payload.
- Delayed PONG is tolerated within timeout.
- Missing PONG triggers retry.
- Repeated missing PONG disconnects and allows reconnect.
- Heartbeat failures do not leak resources.

---

# State Machine Tests

Both sender and receiver state machines must reject invalid transitions.

Receiver states to test:

- Idle
- Connecting
- Handshaking
- Streaming
- Stopping
- Error

Sender states to test:

- Idle
- Discovering
- Connecting
- Waiting For Permission
- Streaming
- Reconnecting
- Stopped

Required cases:

- Start from Idle.
- Stop is idempotent.
- Disconnect during handshake recovers.
- Disconnect during streaming releases audio resources.
- ERROR packet moves session to Error or Stopped.

---

# Android Sender Tests

Android tests verify capture, encoding, transport and UI orchestration.

---

# Android Unit Tests

Recommended unit test areas:

- Protocol serialization
- Protocol parsing
- Session state transitions
- Receiver repository merge logic
- Configuration defaults
- Configuration validation
- ViewModel state mapping
- Error message mapping
- Reconnection policy

These tests should run on the JVM where possible.

---

# Android Component Tests

Recommended component tests:

| Component | Test Cases |
|----------|------------|
| Audio Capture Engine | permission granted, denied, revoked, start, stop |
| Audio Encoder | valid PCM input, timeout, restart, queue overflow |
| Transport Client | connect, send, disconnect, retry, heartbeat |
| Discovery Manager | mDNS result, broadcast result, manual IP, duplicate merge |
| Foreground Service | start, stop, notification action, service restart |
| Configuration Repository | DataStore read/write, migration, invalid value recovery |

---

# Android Instrumentation Tests

Instrumentation tests should cover Android framework boundaries.

Recommended cases:

- MediaProjection permission workflow
- Foreground service notification visibility
- AudioRecord creation with playback capture configuration
- MediaCodec AAC encoder availability
- Network monitoring callbacks
- Process recreation during inactive state

Some cases may require manual approval during test execution.

---

# Android Manual Tests

Required manual release tests:

- Fresh install starts without crash.
- Receiver list is initially empty.
- Receiver discovery populates list.
- MediaProjection permission dialog appears before capture.
- Permission denial returns user to Idle.
- Permission grant starts streaming.
- Foreground notification remains visible during streaming.
- Stop action terminates stream.
- App survives screen lock during playback.
- App recovers after Wi-Fi reconnect.

---

# Windows Receiver Tests

Windows tests verify the receiver service, protocol handling, audio playback and desktop integration.

---

# Windows Unit Tests

Recommended unit test areas:

- Packet parsing
- Packet serialization
- Version negotiation
- Session state transitions
- Queue overflow and underflow policy
- Configuration loading
- Configuration validation
- Configuration migration
- Receiver identity generation
- Log redaction rules

Target coverage for core non-UI logic:

```text
>= 80%
```

---

# Windows Component Tests

Recommended component tests:

| Component | Test Cases |
|----------|------------|
| TCP Listener | bind, accept, close, port unavailable |
| Session Manager | accept one sender, reject busy sender, timeout |
| Packet Dispatcher | valid dispatch, invalid packet, unknown packet |
| AAC Decoder | valid sample, corrupt frame, decoder restart |
| Audio Renderer | fake PCM sink, device unavailable, buffer underflow |
| Discovery Service | publish, unpublish, TXT update |
| Configuration Manager | atomic save, backup restore, hot reload |

---

# Windows UI Tests

WinForms UI tests should stay minimal.

Recommended automated checks:

- View model formatting
- Settings validation
- Tray menu command routing
- Status text mapping

Manual checks are acceptable for:

- Tray icon visibility
- Minimize-to-tray behavior
- Audio device selection dialog
- Firewall prompt behavior
- Installer shortcuts

---

# Audio Pipeline Tests

Audio tests verify low latency and stable playback.

---

# Audio Unit Tests

Required cases:

- AAC queue accepts valid frames.
- AAC queue enforces bounded capacity.
- Old frames are dropped on overflow.
- Underflow is reported without crashing.
- PCM ring buffer wraps correctly.
- Invalid AAC frame is skipped.
- Decoder failure affects only the current frame.
- Renderer stop drains or discards buffers cleanly.

---

# Audio Quality Tests

Recommended signals:

- 1 kHz sine wave
- Silence
- Alternating left/right channel tone
- Short impulse
- Continuous 48 kHz stereo AAC stream

Expected results:

- No channel swap.
- No repeated frames during normal playback.
- No unbounded latency growth.
- No audible clicks during normal start/stop.
- Corrupt frame does not terminate playback.

---

# Latency Tests

Version 1 target:

```text
End-to-end latency < 150 ms
```

Approximate budget:

| Stage | Target |
|-------|-------:|
| Audio Capture | < 10 ms |
| AAC Encoding | < 20 ms |
| Packetization | < 2 ms |
| TCP Transport | < 20 ms |
| Packet Parsing | < 2 ms |
| AAC Decoding | < 20 ms |
| Playback Buffer | 40–80 ms |

Latency measurement should use monotonic timestamps.

Physical speaker-to-microphone latency tests are useful but should not be the only release gate because they depend on hardware.

---

# Endurance Tests

Recommended endurance matrix.

| Duration | Purpose | Gate |
|----------|---------|------|
| 10 minutes | Developer smoke test | Local |
| 1 hour | Basic stability | Pull request when practical |
| 4 hours | Memory verification | Nightly |
| 8 hours | Overnight streaming | Pre-release |
| 24 hours | Production validation | Public release |

During endurance tests, monitor:

- Memory growth
- CPU usage
- Queue depth
- Underflow count
- Overflow count
- Reconnect count
- Average latency
- Peak latency

Memory, CPU and latency should remain bounded.

---

# Discovery Tests

Discovery tests verify receiver visibility and connection readiness.

---

# mDNS Tests

Required cases:

- Receiver publishes `_oal._tcp.local`.
- TXT records include required fields.
- Unknown TXT keys are ignored.
- Android discovers receiver.
- Receiver unpublishes during shutdown.
- Busy receiver status is reflected.
- Duplicate service names are handled.

---

# UDP Broadcast Tests

Required cases:

- Sender sends discovery request to UDP 39887.
- Receiver responds with TCP port 39888.
- Invalid broadcast packet is ignored.
- Multiple receivers respond without collision.
- Duplicate mDNS and broadcast results are merged.

---

# Manual IP Tests

Required cases:

- Valid IP and port connects.
- Invalid IP fails with clear error.
- Cached receiver reconnects.
- WireGuard/private-network address works when reachable.

---

# Network Change Tests

Required cases:

- Wi-Fi disconnect marks receiver offline.
- Wi-Fi reconnect refreshes receiver list.
- DHCP address change updates advertised address.
- VPN enable/disable refreshes discovery.
- Sleep/wake does not leave stale active sessions.

---

# Configuration Tests

Configuration tests verify safe defaults and recovery.

Required cases:

- Missing configuration creates defaults.
- Valid configuration loads.
- Invalid JSON does not crash startup.
- Invalid fields are replaced with defaults.
- Unknown fields are preserved or ignored safely.
- Old versions migrate forward.
- Atomic save does not corrupt existing config.
- Backup restore works.
- Hot reload applies supported fields.
- Restart-required fields are reported clearly.

Canonical defaults for Version 1:

| Field | Value |
|-------|-------|
| TCP Port | 39888 |
| UDP Port | 39887 |
| Codec | AAC-LC |
| Sample Rate | 48000 |
| Channels | 2 |
| Bit Depth | 16 |
| Buffer | 100 ms |

---

# Deployment Tests

Deployment tests verify installation, upgrade and removal.

---

# Windows Installer Tests

Required cases:

- Fresh install succeeds on supported Windows versions.
- .NET Framework 4.8 prerequisite is detected.
- Desktop/start menu shortcuts are created when selected.
- Firewall rules are created or failure is reported.
- Receiver starts after installation.
- Upgrade preserves configuration.
- Uninstall removes application files.
- User data removal follows selected uninstall option.
- Installer log is written.

---

# Android Package Tests

Required cases:

- APK installs on Android 10+.
- Release APK is signed.
- App starts after install.
- Required permissions are requested at runtime.
- No root access is required.
- Upgrade preserves settings.
- Uninstall removes app-private data.

---

# CI Strategy

Continuous integration should stay small and reliable.

---

# Pull Request Gate

Recommended pull request checks:

```text
Checkout

↓

Restore Dependencies

↓

Build

↓

Unit Tests

↓

Static Analysis

↓

Package Smoke Test
```

Pull requests should not require real audio devices.

---

# Android CI

Recommended Android checks:

```text
Gradle Build

↓

Unit Tests

↓

Lint

↓

Static Analysis

↓

Assemble Debug
```

Release APK generation belongs in release workflows.

---

# Windows CI

Recommended Windows checks:

```text
Restore NuGet Packages

↓

Build Solution

↓

Unit Tests

↓

Code Analysis

↓

Installer Smoke Build
```

Windows 7 runtime validation may require a dedicated VM outside normal hosted CI.

---

# Nightly Gate

Recommended nightly checks:

- Android instrumentation subset
- Windows integration tests
- TCP loopback streaming
- Discovery integration tests
- 4-hour endurance test
- Package build

Nightly failures should block releases but not necessarily block all pull requests.

---

# Release Gate

A public release requires:

- All pull request checks pass.
- All nightly checks pass.
- Protocol compatibility tests pass.
- Windows installer validation passes.
- Android APK validation passes.
- End-to-end audio playback succeeds.
- Release checklist is completed.
- Known manual test failures are documented.

---

# Manual Release Checklist

Before publishing Version 1:

## Receiver

- [ ] Fresh install succeeds.
- [ ] Receiver starts.
- [ ] TCP listener is active.
- [ ] mDNS advertisement is visible.
- [ ] Firewall configuration is valid.
- [ ] Audio device is selectable.
- [ ] Test sound plays.
- [ ] Tray icon works.

---

## Sender

- [ ] APK installs on Android 10+.
- [ ] Receiver is discovered.
- [ ] MediaProjection permission flow works.
- [ ] Streaming starts.
- [ ] Foreground notification is visible.
- [ ] Stop action works.
- [ ] Reconnect works after receiver restart.

---

## Audio

- [ ] Playback is audible.
- [ ] Latency is below 150 ms in normal LAN conditions.
- [ ] No sustained underruns.
- [ ] No sustained queue growth.
- [ ] Device hot-plug recovers or reports a clear error.
- [ ] 24-hour playback validation passes before public release.

---

## Upgrade

- [ ] Existing configuration is preserved.
- [ ] Configuration migration succeeds.
- [ ] Old logs do not block startup.
- [ ] Uninstall behavior matches user choice.

---

# Test Data

Checked-in AAC fixture data lives under `testdata/audio/`:

```text
testdata/audio/
├── README.md
├── aac-lc-48k-stereo-1024.adts
├── aac-lc-48k-stereo-1024.raw
├── aac-lc-48k-stereo.asc
└── fixture-manifest.json
```

Binary golden files are regenerated only for an intentional protocol-version
change or a reviewed pre-release wire-contract correction. Regeneration
updates the generator, manifest, platform exact-byte tests, and affected
protocol documentation in the same change.

Run:

```bash
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
```

The fixture proves structure and provenance; native Media Foundation decode is
proved in the next phase.

---

# Diagnostics Required For Testing

The implementation should expose enough diagnostics to make failures reproducible.

Recommended runtime metrics:

| Metric | Purpose |
|--------|---------|
| Current State | State machine validation |
| Connected Sender | Session ownership |
| Queue Depth | Buffer health |
| Dropped Frames | Overflow detection |
| Underflow Count | Playback starvation |
| Average Latency | Performance tracking |
| Peak Latency | Spike detection |
| Reconnect Count | Network stability |
| Decoder Restart Count | Codec reliability |
| Last Error | Manual debugging |

Logs should include timestamps and component names.

Logs must not include private network secrets or future pairing keys.

---

# Regression Policy

Every regression entry should record:

- Bug summary
- Root cause
- Reproduction steps
- Fixed version
- Regression test name
- Manual verification steps if automation is not practical

A bug is not considered closed until the regression path is documented.

---

# Release Quality Bar

Version 1 may be released when:

1. Protocol tests pass.
2. Android sender tests pass on required devices.
3. Windows receiver tests pass on required Windows versions.
4. End-to-end streaming works on a normal LAN.
5. End-to-end latency is below 150 ms in normal LAN conditions.
6. 24-hour receiver playback validation passes.
7. Installer and APK validation pass.
8. Known limitations are documented.

---

# Related Documents

- `docs/02-Architecture.md`
- `docs/03-Protocol.md`
- `docs/04-Android.md`
- `docs/05-Windows.md`
- `docs/06-Audio.md`
- `docs/07-Discovery.md`
- `docs/08-Configuration.md`
- `docs/09-Deployment.md`

---

# End of Document
