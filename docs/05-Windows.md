# docs/05-Windows.md

# Windows Receiver Implementation

Version: 1.0

Target Platform

Windows 7 SP1 (x64)

Windows 8.1

Windows 10

Windows 11

Framework

.NET Framework 4.8

Language

C#

UI Framework

WinForms

---

# Design Objectives

The Windows Receiver is responsible for:

- Receiver discovery
- Protocol handling
- Audio decoding
- Audio playback
- Configuration management
- Runtime monitoring

The Receiver never captures audio.

The Receiver never performs audio encoding.

---

# Design Philosophy

The Windows implementation prioritizes:

- Windows 7 compatibility
- Stable playback
- Low latency
- Low CPU usage
- Long-term reliability

The Receiver should remain operational for weeks without requiring a restart.

---

# Why WinForms

Version 1 intentionally uses WinForms.

Reasons.

- Native .NET support
- Mature ecosystem
- Small runtime footprint
- Excellent Windows 7 compatibility
- Fast startup
- Easy tray integration

WPF, WinUI and MAUI are intentionally excluded from Version 1.

---

# High-level Architecture

```text
UI

↓

ReceiverService

↓

SessionManager

↓

Discovery

↓

Transport

↓

Protocol

↓

AAC Decoder

↓

Audio Output
```

Every layer owns a single responsibility.

---

# Runtime Components

| Component | Responsibility |
|-----------|---------------|
| MainForm | User Interface |
| TrayManager | System Tray |
| ReceiverService | Application Lifecycle |
| SessionManager | Session Control |
| DiscoveryService | mDNS |
| TcpServer | TCP Listener |
| ProtocolEngine | Packet Parsing |
| AudioDecoder | AAC Decoder |
| AudioOutput | Speaker Playback |
| Configuration | Settings |
| Logger | Logging |

---

# Receiver Lifecycle

```text
Application Start

↓

Load Configuration

↓

Initialize Audio

↓

Start Discovery

↓

Start TCP Listener

↓

Wait For Connection
```

The Receiver remains idle until a sender connects.

---

# ReceiverService

ReceiverService is the application's orchestration layer.

Responsibilities.

- Startup
- Shutdown
- Initialization
- Dependency wiring
- Error recovery

Only one ReceiverService exists.

---

# SessionManager

SessionManager owns:

- Current session
- Connection state
- Heartbeat
- Reconnection handling
- Statistics

Exactly one active session is permitted.

---

# Main Window

The main window provides configuration and diagnostics.

Displayed information.

- Receiver Name
- Listening Port
- Connected Sender
- Current Latency
- Bitrate
- Playback Device
- Session Duration

Controls.

- Start
- Stop
- Disconnect
- Refresh Audio Devices

---

# Tray Icon

The application minimizes to the system tray.

Tray menu.

```text
Open

Disconnect

Settings

Logs

Exit
```

Closing the main window minimizes the application instead of terminating it.

---

# Startup Flow

```text
Launch

↓

Load Settings

↓

Initialize Logger

↓

Initialize Audio

↓

Start Discovery

↓

Open TCP Listener

↓

Ready
```

Initialization failures should be reported to the user before the application exits.

---

# Package Structure

```text
receiver-windows/

OpenAudioLink/

Core/

Audio/

Protocol/

Transport/

Discovery/

Configuration/

Logging/

UI/

Models/

Utilities/
```

Each namespace corresponds to one subsystem.

---

# Namespace Layout

```text
OpenAudioLink.Core

OpenAudioLink.Audio

OpenAudioLink.Protocol

OpenAudioLink.Transport

OpenAudioLink.Discovery

OpenAudioLink.Configuration

OpenAudioLink.Logging

OpenAudioLink.UI
```

Dependencies always point toward lower layers.

---

# Dependency Graph

```text
MainForm

↓

ReceiverService

↓

SessionManager

↓

Transport

↓

Protocol

↓

Audio
```

Reverse dependencies are prohibited.

---

# Thread Model

Recommended execution model.

| Component | Thread |
|-----------|--------|
| UI | UI Thread |
| TCP Listener | Background |
| Packet Parser | Background |
| Decoder | Background |
| Playback | Audio Thread |
| Discovery | Background |
| Logging | Background |

No network or decoding work should execute on the UI thread.

---

# Runtime State

Recommended application states.

```text
Starting

↓

Ready

↓

Connected

↓

Streaming

↓

Stopping

↓

Stopped
```

The UI should observe these states reactively.

---

# Configuration

Recommended configuration file.

```text
config.json
```

Stored beside the executable.

Example.

```json
{
  "receiverName": "Office-PC",
  "listenPort": 39888,
  "bufferMs": 100,
  "autoStart": true,
  "startMinimized": true,
  "logLevel": "Information"
}
```

---

# Logging

Recommended log file.

```text
logs/

2026-07-06.log
```

Daily rolling log files simplify troubleshooting.

Structured logging is preferred.

---

# Exception Handling

All background threads should catch unexpected exceptions.

Pattern.

```text
try

↓

Log

↓

Recover

↓

Continue
```

Unexpected exceptions should not terminate the application whenever recovery is possible.

---

# Component Responsibilities

MainForm

UI only.

ReceiverService

Application lifecycle.

SessionManager

Session ownership.

TcpServer

Socket management.

ProtocolEngine

Packet parsing.

AudioDecoder

AAC decoding.

AudioOutput

PCM playback.

DiscoveryService

mDNS.

Logger

Diagnostics.

Each component should expose a clear public interface.

---

# Resource Ownership

Ownership model.

```text
ReceiverService

↓

SessionManager

↓

TcpServer

↓

ProtocolEngine

↓

AudioDecoder

↓

AudioOutput
```

Resources should always be released in reverse order during shutdown.

---

# Shutdown Sequence

```text
Stop Discovery

↓

Close TCP Listener

↓

Disconnect Session

↓

Flush Decoder

↓

Drain Playback

↓

Release Audio Device

↓

Save Configuration

↓

Exit
```

Graceful shutdown prevents audio artifacts and resource leaks.

---

# Application Host

Version 1 uses a lightweight host architecture compatible with .NET Framework 4.8.

Benefits:

- Dependency Injection
- Unified Logging
- Configuration Management
- Hosted Services
- Graceful Shutdown
- Improved Testability

Application startup sequence.

```text
Program

↓

HostBuilder

↓

Configuration

↓

Dependency Injection

↓

Hosted Services

↓

MainForm
```

The UI is hosted inside the same application host.

---

# Hosted Services

Recommended hosted services.

```text
ReceiverHostedService

DiscoveryHostedService

TcpHostedService
```

Each hosted service owns a single responsibility.

---

# Dependency Injection

Recommended registrations.

Singleton:

```text
SessionManager

ReceiverService

ConfigurationManager

Logger

DiscoveryManager
```

Transient:

```text
PacketSerializer

PacketParser
```

Scoped services are generally unnecessary in a desktop application.

---

# ReceiverService

ReceiverService coordinates the application's runtime.

Responsibilities.

- Start listening
- Stop listening
- Accept sessions
- Monitor runtime health
- Notify UI

ReceiverService does not perform audio decoding.

---

# ConnectionManager

Responsibilities.

- Accept TCP connections
- Validate protocol version
- Create sessions
- Close idle connections
- Track connected sender

Only one active sender is permitted in Version 1.

Additional senders receive a BUSY response.

---

# TCP Listener

Recommended implementation.

```text
TcpListener

↓

AcceptTcpClientAsync()

↓

Session Creation

↓

SessionManager
```

The listener should remain active throughout the application's lifetime.

---

# Connection Lifecycle

```text
Incoming Connection

↓

Accept

↓

Read HELLO

↓

Validate

↓

Send WELCOME

↓

Streaming
```

Connections that fail validation should be closed immediately.

---

# Session Timeout

If no heartbeat is received within the configured timeout.

```text
Heartbeat Timeout

↓

Disconnect

↓

Release Resources

↓

Ready
```

Recommended timeout.

```
15 Seconds
```

---

# Concurrent Sessions

Version 1 supports:

```
One Active Session
```

Future versions may support multiple independent playback zones.

---

# Packet Processing

Incoming bytes follow this path.

```text
Socket

↓

Receive Buffer

↓

Header Parser

↓

Packet Validator

↓

Packet Dispatcher

↓

SessionManager
```

Packets are validated before processing.

---

# Packet Dispatcher

Responsibilities.

- HELLO
- START_STREAM
- AUDIO
- PING
- STOP_STREAM
- DISCONNECT

Each packet type has an independent handler.

---

# Receive Buffer

Recommended size.

```
64 KB
```

The buffer should be reused to minimize memory allocations.

---

# Receive Queue

Packets are placed into a processing queue.

```text
Socket

↓

Receive Queue

↓

Protocol Engine
```

Recommended capacity.

```
128 Packets
```

Overflow should terminate the session because it indicates the receiver cannot process incoming data fast enough.

---

# Protocol Validation

Every packet should pass the following checks.

```text
Magic Number

↓

Version

↓

Length

↓

Flags

↓

CRC (Optional Future)

↓

Dispatch
```

Malformed packets must never reach higher layers.

---

# Heartbeat Manager

Responsibilities.

- Send PONG
- Monitor heartbeat timeout
- Update last activity timestamp

Recommended heartbeat interval.

```
5 Seconds
```

Recommended timeout.

```
15 Seconds
```

---

# Audio Pipeline

After protocol validation.

```text
AUDIO Packet

↓

AAC Queue

↓

Decoder

↓

PCM Queue

↓

Audio Output
```

Each stage communicates through bounded queues.

---

# Queue Capacities

Recommended defaults.

| Queue | Capacity |
|--------|---------:|
| Receive Queue | 128 Packets |
| AAC Queue | 20 Frames |
| PCM Queue | 100 ms Audio |

Bounded queues prevent uncontrolled memory growth.

---

# Flow Control

If the decoder falls behind.

```text
AAC Queue Full

↓

Discard Oldest Frame

↓

Continue Playback
```

Maintaining real-time playback is preferred over preserving every frame.

---

# Backpressure

The receiver should never block the TCP receive thread.

Instead.

```text
Receive

↓

Queue

↓

Worker

↓

Decode
```

This keeps network processing responsive.

---

# Session Statistics

Recommended metrics.

| Metric | Description |
|--------|-------------|
| Connected Time | Session duration |
| Received Frames | Total AUDIO packets |
| Dropped Frames | Queue overflow count |
| Average Latency | Playback latency |
| Peak Latency | Maximum latency |
| Bytes Received | Total traffic |

Statistics should be exposed to both the UI and logging system.

---

# Error Categories

Recoverable.

- Temporary network interruption
- Packet timeout
- Decoder buffer starvation

Fatal.

- Protocol mismatch
- Invalid packet format
- Decoder initialization failure
- Audio device unavailable

Fatal errors terminate the current session but should not terminate the application.

---

# Runtime Monitoring

ReceiverService should periodically verify.

- Listener state
- Decoder state
- Playback state
- Queue lengths
- Session health

Monitoring interval.

```
1 Second
```

This information is useful for diagnostics and future telemetry.

---

# UI Notifications

Important runtime events should be published to the UI.

Examples.

```text
Receiver Ready

Sender Connected

Streaming Started

Streaming Stopped

Audio Device Changed

Reconnect Requested

Session Closed
```

UI updates should be marshaled onto the UI thread.

---

# Performance Targets

Recommended goals.

| Metric | Target |
|--------|-------:|
| TCP Accept | < 5 ms |
| Packet Validation | < 1 ms |
| Queue Delay | < 5 ms |
| Session Startup | < 100 ms |
| CPU Usage (Idle) | < 1% |

These targets assume a typical desktop system running Windows 7 or later.

---

# Audio Subsystem

The Audio Subsystem is responsible for transforming received AAC frames into audible sound.

Responsibilities.

- AAC decoding
- PCM buffering
- Audio playback
- Device management
- Latency control
- Stream synchronization

Networking and protocol logic do not belong to this subsystem.

---

# Audio Pipeline

```text
TCP

↓

Protocol

↓

AAC Queue

↓

AAC Decoder

↓

PCM Queue

↓

Audio Renderer

↓

Windows Audio Engine

↓

Speaker
```

Every stage operates independently.

Communication occurs only through bounded queues.

---

# Audio Architecture

Recommended implementation.

```text
AudioManager

├── AudioDecoder

├── AudioBuffer

├── AudioRenderer

├── DeviceManager

└── ClockManager
```

AudioManager coordinates all audio-related components.

---

# AudioManager

Responsibilities.

- Initialize audio subsystem
- Select output device
- Start playback
- Stop playback
- Restart playback
- Handle device changes

Only one AudioManager instance exists.

---

# AAC Decoder

Version 1 uses Windows Media Foundation.

Reasons.

- Built into Windows
- Hardware acceleration when available
- Excellent compatibility
- Low CPU usage
- Mature implementation

Third-party decoders are unnecessary.

---

# Decoder Lifecycle

```text
Create Decoder

↓

Configure

↓

Decode AAC

↓

Produce PCM

↓

Flush

↓

Release
```

The decoder exists only while a streaming session is active.

---

# Decoder Input

Input format.

```
AAC-LC

48 kHz

Stereo

192 kbps
```

Frames arrive directly from the protocol layer.

---

# Decoder Output

Output format.

```
PCM

16-bit

Stereo

48000 Hz
```

Decoded PCM is immediately forwarded to the playback queue.

---

# Decoder Thread

Recommended execution.

```text
AAC Queue

↓

Decoder Thread

↓

PCM Queue
```

The decoder should block while waiting for new frames.

Busy waiting is prohibited.

---

# Buffered Playback

PCM data is stored inside a playback buffer before reaching the audio device.

Pipeline.

```text
Decoder

↓

BufferedWaveProvider

↓

Renderer
```

Short buffering absorbs network jitter.

---

# Recommended Buffer

Version 1 defaults.

```
100 ms
```

User configurable range.

```
40 ms

↓

250 ms
```

Smaller buffers reduce latency.

Larger buffers improve resilience.

---

# Queue Management

Recommended capacities.

| Queue | Size |
|--------|------|
| AAC Queue | 20 Frames |
| PCM Queue | 100 ms Audio |

Queues should remain bounded.

---

# Underflow

Occurs when playback consumes data faster than decoding produces it.

Handling.

```text
Buffer Empty

↓

Insert Silence

↓

Continue Playback
```

Maintaining a stable playback clock is more important than avoiding brief silence.

---

# Overflow

Occurs when decoding outpaces playback.

Handling.

```text
Buffer Full

↓

Discard Oldest PCM

↓

Continue
```

Low latency takes precedence over preserving every decoded frame.

---

# Audio Renderer

The renderer transfers PCM samples to the Windows audio subsystem.

Version 1 supports two renderer implementations.

```text
WasapiRenderer

WaveOutRenderer
```

The implementation is selected automatically at runtime.

---

# Renderer Selection

Recommended strategy.

```text
Windows Vista+

↓

Try WASAPI

↓

Success

↓

Use WASAPI

↓

Failure

↓

WaveOutEvent
```

This guarantees compatibility with all supported Windows versions.

---

# WASAPI

Preferred renderer.

Advantages.

- Low latency
- Exclusive/shared mode support
- Native Windows API
- Better synchronization

Version 1 uses **shared mode**.

Exclusive mode may be introduced later.

---

# WaveOutEvent

Fallback renderer.

Advantages.

- Very mature
- Stable
- Excellent compatibility
- Reliable on Windows 7

Used only if WASAPI initialization fails.

---

# Audio Device Manager

Responsibilities.

- Enumerate devices
- Detect default device
- Detect hot-plug events
- Switch playback device

Device enumeration occurs during startup and whenever Windows reports a device change.

---

# Device Enumeration

Recommended API.

```
MMDeviceEnumerator
```

Collected information.

- Device ID
- Friendly Name
- State
- Default Flag

The Device ID should be stored rather than the display name.

---

# Device Selection

Priority.

```text
Configured Device

↓

Current Default Device

↓

First Active Device
```

If no device is available, streaming pauses until one becomes available.

---

# Device Hot-Plug

Examples.

- USB DAC connected
- USB DAC removed
- Bluetooth headset connected
- HDMI monitor connected

Recommended sequence.

```text
Device Changed

↓

Pause Playback

↓

Recreate Renderer

↓

Resume Playback
```

The decoder should continue running during renderer recreation whenever possible.

---

# Sample Format

Internal PCM format.

```
48000 Hz

Stereo

16-bit

Little Endian
```

All playback components should use the same format to avoid unnecessary conversions.

---

# Volume Control

Version 1 intentionally does not modify system volume.

The receiver outputs audio at unity gain.

Users control volume through the Windows audio mixer or the playback device.

Future protocol versions may support synchronized remote volume.

---

# Audio Timing

Playback timing is determined by the renderer.

The decoder must not attempt to regulate playback speed.

The playback device clock is considered authoritative.

---

# Performance Targets

Recommended goals.

| Metric | Target |
|--------|-------:|
| AAC Decode | < 5 ms |
| PCM Queue Delay | < 20 ms |
| Renderer Latency | < 40 ms |
| Total Audio Pipeline | < 80 ms |
| CPU Usage | < 2% |

Values assume 48 kHz stereo AAC-LC on a typical desktop processor.

---

# Error Recovery

Recoverable.

- Temporary decoder starvation
- Output buffer underflow
- Renderer restart
- Device change

Fatal.

- Decoder creation failure
- Unsupported audio format
- No playback devices available

Recoverable errors should not terminate the application.

Fatal errors terminate only the active session.

---

# Discovery Service

The Discovery Service advertises the receiver on the local network and enables Android senders to discover it automatically.

Version 1 uses standard DNS Service Discovery (DNS-SD) over Multicast DNS (mDNS).

This ensures interoperability and avoids proprietary discovery mechanisms.

---

# Discovery Technology

Recommended implementation.

| Platform | Technology |
|----------|------------|
| Windows | Makaretu.Dns.Multicast |
| Android | NsdManager |
| Protocol | DNS-SD / mDNS |

No central server is required.

All discovery traffic remains within the local network.

---

# Advertised Service

Recommended service type.

```text
_openaudiolink._tcp.local
```

Default service port.

```text
39888
```

The service name should be configurable by the user.

Example.

```text
Office-PC
```

---

# TXT Records

Recommended TXT records.

| Key | Example |
|-----|---------|
| version | 1.0 |
| codec | aac-lc |
| sampleRate | 48000 |
| channels | 2 |
| protocol | 1 |
| platform | windows |
| hostname | OFFICE-PC |
| capabilities | aac |

TXT records allow senders to determine compatibility before connecting.

---

# Discovery Lifecycle

```text
Application Start

↓

Publish Service

↓

Broadcast Presence

↓

Respond to Queries

↓

Application Exit

↓

Withdraw Service
```

The service should be unpublished before shutdown whenever possible.

---

# Discovery Manager

Responsibilities.

- Publish service
- Refresh advertisement
- Handle hostname conflicts
- Update TXT records
- Withdraw advertisement

DiscoveryManager is independent of the streaming session.

---

# Hostname Conflicts

If another receiver already uses the same service name.

Recommended behavior.

```text
Office-PC

↓

Conflict

↓

Office-PC (2)
```

The updated name should be advertised automatically.

The user may later rename the receiver.

---

# Receiver Identity

Every receiver should have a persistent unique identifier.

Recommended format.

```text
UUID
```

Example.

```text
3c8a91f0-f7ef-45c9-9cfd-0d9b3d8f0f84
```

The UUID should be generated once and stored in the configuration.

Changing the display name must not change the UUID.

---

# Dynamic TXT Updates

The following fields may be updated while the application is running.

- Receiver Name
- Codec Support
- Protocol Version
- Current Status

Status values.

```text
Ready

Busy

Offline
```

Senders may use these values to improve the user experience.

---

# Busy State

If a second sender discovers the receiver while another session is active.

Discovery continues to advertise the receiver.

TXT record.

```text
status=busy
```

The receiver may also return a BUSY response if a connection attempt occurs.

---

# Discovery Refresh

Recommended advertisement refresh interval.

```text
60 Seconds
```

The library should also respond immediately to incoming mDNS queries.

---

# Network Changes

Discovery should respond to network changes.

Examples.

- IP address changed
- Wi-Fi connected
- Ethernet connected
- VPN connected

Recommended sequence.

```text
Network Changed

↓

Stop Advertisement

↓

Refresh Interface

↓

Republish Service
```

---

# Multiple Network Interfaces

The receiver may have several active interfaces.

Examples.

- Ethernet
- Wi-Fi
- Hyper-V Virtual Switch
- VMware Adapter
- WireGuard
- Tailscale

Recommended behavior.

Publish only on interfaces capable of reaching the local network.

Virtual interfaces intended solely for tunneling should generally be ignored unless explicitly enabled.

---

# IPv4 and IPv6

Version 1 should support both IPv4 and IPv6 advertisements.

Preferred connection order.

```text
IPv4

↓

IPv6
```

Future versions may allow the user to configure this preference.

---

# Firewall Considerations

The installer should create Windows Firewall rules for.

- TCP 39888
- UDP 5353

The application should detect when discovery is blocked and provide a diagnostic warning.

---

# Configuration Management

Configuration is stored in:

```text
config.json
```

Suggested properties.

```json
{
  "receiverName": "Office-PC",
  "receiverId": "3c8a91f0-f7ef-45c9-9cfd-0d9b3d8f0f84",
  "listenPort": 39888,
  "publishDiscovery": true,
  "preferredAudioDevice": "",
  "bufferMs": 100,
  "startMinimized": true,
  "autoStart": true
}
```

Unknown configuration keys should be ignored to maintain forward compatibility.

---

# Runtime Configuration

Changes to the following settings should take effect without restarting the application.

- Receiver Name
- Buffer Size
- Log Level
- Preferred Audio Device

Changing the listening port requires restarting the TCP listener.

---

# Settings Validation

Configuration values should be validated during startup.

Examples.

```text
Buffer

40–250 ms
```

```text
Port

1024–65535
```

Invalid values should be replaced with safe defaults and logged.

---

# Diagnostics

The application should provide a diagnostics page.

Suggested information.

- Receiver Name
- Receiver UUID
- Current IP Addresses
- Discovery Status
- Listening Port
- Connected Sender
- Audio Device
- Protocol Version

This information simplifies troubleshooting.

---

# Runtime Health Checks

ReceiverService should periodically verify.

- Discovery active
- TCP listener active
- Audio renderer active
- Decoder active
- Session state valid

Health check interval.

```text
5 Seconds
```

Detected failures should be logged and reported to the UI.

---

# Production Recommendations

For reliable operation.

- Enable automatic startup.
- Minimize to tray.
- Enable automatic reconnection.
- Use wired Ethernet when possible.
- Avoid unnecessary virtual network adapters.
- Keep Windows audio drivers up to date.

These recommendations improve stability and reduce latency.

---

# Related Documents

Further implementation details are defined in.

```text
docs/06-Audio.md
```

Detailed audio pipeline.

```text
docs/07-Discovery.md
```

Complete discovery protocol.

```text
docs/08-Configuration.md
```

Configuration schema.

```text
docs/09-Deployment.md
```

Installation and packaging.

---

# End of Part

# User Interface

The Windows Receiver provides a lightweight desktop interface intended for configuration, monitoring and diagnostics.

The application is designed to spend most of its lifetime minimized to the system tray.

---

# Design Goals

The UI should:

- Start quickly
- Consume minimal memory
- Never block streaming
- Provide clear status information
- Require little user interaction

The receiver should continue operating normally even if the main window is closed.

---

# Main Window Layout

Recommended layout.

```text
+------------------------------------------------------+

OpenAudioLink Receiver

--------------------------------------------------------

Receiver

Name:        Office-PC

Status:      Streaming

Sender:      Pixel 9 Pro

Protocol:    1.0

--------------------------------------------------------

Audio

Output Device:

[ Speakers (Realtek)             ▼ ]

Latency:

82 ms

Buffer:

100 ms

--------------------------------------------------------

Network

Listening Port:

39888

Discovery:

Enabled

IP Address:

192.168.1.100

--------------------------------------------------------

Buttons

[ Disconnect ]

[ Refresh Devices ]

[ Settings ]

+------------------------------------------------------+
```

The interface should emphasize operational status rather than configuration complexity.

---

# Status Indicators

Recommended status values.

| Status | Description |
|--------|-------------|
| Ready | Waiting for sender |
| Connecting | Handshake in progress |
| Streaming | Audio active |
| Busy | Another sender connected |
| Error | Recoverable fault |
| Offline | Receiver stopped |

Status updates should occur immediately after state changes.

---

# Tray Integration

The tray icon represents the current receiver state.

Suggested mappings.

| Icon | Meaning |
|------|---------|
| Gray | Stopped |
| Blue | Ready |
| Green | Streaming |
| Yellow | Warning |
| Red | Error |

Tooltip example.

```text
OpenAudioLink

Streaming

Latency: 83 ms
```

---

# Tray Menu

Recommended commands.

```text
Open

Disconnect Sender

Refresh Audio Devices

Restart Discovery

View Logs

Settings

About

Exit
```

Frequently used operations should be available without opening the main window.

---

# Settings Dialog

Suggested categories.

General

- Receiver Name
- Auto Start
- Start Minimized

Audio

- Output Device
- Playback Buffer
- Preferred Renderer

Network

- TCP Port
- Discovery Enabled

Logging

- Log Level
- Log Directory

Advanced

- Debug Mode
- Experimental Features

---

# Audio Device Selection

Available playback devices should be listed by friendly name.

Example.

```text
Speakers (Realtek)

USB DAC

Bluetooth Headset

HDMI Monitor
```

Internally, the application stores the Windows device identifier rather than the display name.

---

# Device Refresh

The UI should allow manual refresh.

Workflow.

```text
Refresh

↓

Enumerate Devices

↓

Compare Device List

↓

Update UI
```

Automatic refresh still occurs on system device notifications.

---

# Live Statistics

Recommended statistics.

| Metric | Description |
|--------|-------------|
| Session Duration | Elapsed time |
| Received Frames | AUDIO packets |
| Average Bitrate | Current bitrate |
| Network Throughput | KB/s |
| Playback Latency | ms |
| Buffer Fill | % |

Statistics should update approximately once per second.

---

# Log Viewer

The receiver should include a lightweight log viewer.

Capabilities.

- Filter by level
- Search
- Copy selected entries
- Export log

Logs are intended for diagnostics and should not impact streaming performance.

---

# Error Reporting

Recoverable issues should be presented as non-intrusive notifications.

Examples.

```text
Audio device changed.

Playback restarted.
```

Fatal session errors.

```text
Connection lost.

Waiting for sender...
```

The application should avoid modal dialogs during active streaming.

---

# Auto Start

The application should support automatic startup with Windows.

Recommended methods.

- Startup folder shortcut
- Registry Run entry

The feature should be configurable through the Settings dialog.

---

# Minimize Behavior

Recommended behavior.

```text
Click Close

↓

Hide Window

↓

Remain in Tray
```

Selecting Exit from the tray menu terminates the application.

---

# Update Checking

Version 1 may optionally support update notifications.

Behavior.

```text
Application Start

↓

Check Interval

↓

New Version Available

↓

Notify User
```

Automatic downloading and installation are outside the scope of Version 1.

---

# Crash Diagnostics

Unexpected failures should generate a crash report.

Suggested contents.

- Exception
- Stack Trace
- Application Version
- OS Version
- Loaded Audio Device
- Protocol Version

Crash reports should never include captured audio data.

---

# Localization

The UI should support localization.

Version 1 languages.

- English
- Simplified Chinese

Additional languages may be added through resource files.

---

# Accessibility

Recommendations.

- Keyboard navigation
- High DPI support
- Screen reader compatibility
- High contrast themes

Accessibility should be considered during UI implementation rather than added later.

---

# Windows Compatibility

The receiver should operate consistently across all supported versions.

| Feature | Windows 7 | Windows 10 | Windows 11 |
|---------|-----------|------------|------------|
| Tray Icon | ✓ | ✓ | ✓ |
| WASAPI Shared | ✓ | ✓ | ✓ |
| WaveOutEvent | ✓ | ✓ | ✓ |
| Media Foundation | ✓* | ✓ | ✓ |
| High DPI | Limited | ✓ | ✓ |

\* Requires the Platform Update where applicable.

---

# Performance Requirements

Recommended UI targets.

| Metric | Target |
|--------|-------:|
| Startup | < 2 s |
| Memory Usage (Idle) | < 60 MB |
| CPU Usage (Idle) | < 1% |
| UI Refresh | 1 Hz |
| Device Refresh | < 1 s |

Streaming performance must never be affected by UI activity.

---

# Operational Principles

The receiver should continue streaming correctly when.

- The main window is hidden.
- The user locks Windows.
- The application is minimized.
- Another application becomes active.

The user interface is an observer of application state, not the owner of it.

---

# Project Structure

Recommended directory layout.

```text
receiver-windows/

OpenAudioLink.sln

src/

OpenAudioLink/

├── Program.cs
├── AppHost.cs
├── appsettings.json
│
├── Core/
│   ├── ReceiverHostedService.cs
│   ├── ReceiverService.cs
│   ├── SessionManager.cs
│   ├── SessionState.cs
│   └── RuntimeStatistics.cs
│
├── Audio/
│   ├── AudioManager.cs
│   ├── AudioDecoder.cs
│   ├── MediaFoundationDecoder.cs
│   ├── AudioRenderer.cs
│   ├── WasapiRenderer.cs
│   ├── WaveOutRenderer.cs
│   ├── AudioBuffer.cs
│   ├── JitterBuffer.cs
│   ├── DeviceManager.cs
│   ├── ClockManager.cs
│   └── AudioConfiguration.cs
│
├── Protocol/
│   ├── PacketParser.cs
│   ├── PacketSerializer.cs
│   ├── PacketDispatcher.cs
│   ├── PacketValidator.cs
│   ├── ProtocolConstants.cs
│   └── ProtocolVersion.cs
│
├── Transport/
│   ├── TcpServer.cs
│   ├── TcpSession.cs
│   ├── ConnectionManager.cs
│   ├── ReceiveLoop.cs
│   ├── SendLoop.cs
│   └── HeartbeatManager.cs
│
├── Discovery/
│   ├── DiscoveryManager.cs
│   ├── MdnsPublisher.cs
│   ├── ReceiverAdvertisement.cs
│   └── DiscoveryConfiguration.cs
│
├── Configuration/
│   ├── ConfigurationManager.cs
│   ├── ReceiverOptions.cs
│   └── AudioOptions.cs
│
├── Logging/
│   ├── Logger.cs
│   ├── LogEvent.cs
│   └── SerilogAdapter.cs
│
├── UI/
│   ├── MainForm.cs
│   ├── SettingsForm.cs
│   ├── AboutForm.cs
│   ├── TrayManager.cs
│   └── StatisticsViewModel.cs
│
├── Models/
│
├── Utilities/
│
└── Resources/
```

The structure is organized by subsystem to maximize maintainability and testability.

---

# Core Interfaces

All major subsystems should expose interfaces.

Recommended interfaces.

```text
IReceiverService

ISessionManager

IAudioManager

IAudioDecoder

IAudioRenderer

IDeviceManager

ITransport

IDiscoveryService

ILogger
```

Consumers depend on interfaces rather than concrete implementations.

---

# Dependency Injection

Suggested registrations.

Singleton.

```text
SessionManager

ReceiverService

AudioManager

ConfigurationManager

Logger

DiscoveryManager
```

Transient.

```text
PacketParser

PacketSerializer

PacketValidator
```

Hosted services.

```text
ReceiverHostedService

DiscoveryHostedService

TcpHostedService
```

The dependency graph should remain acyclic.

---

# Application Host Configuration

Recommended startup sequence.

```text
Create Application Host

↓

Load Configuration

↓

Configure Logging

↓

Register Services

↓

Build Host

↓

Start Hosted Services

↓

Run WinForms UI
```

The application should terminate gracefully when the host shuts down.

---

# Configuration Schema

Recommended `appsettings.json`.

```json
{
  "Receiver": {
    "Name": "Office-PC",
    "Port": 39888,
    "AutoStart": true,
    "StartMinimized": true
  },
  "Audio": {
    "PreferredDevice": "",
    "BufferMs": 100,
    "Renderer": "Auto"
  },
  "Discovery": {
    "Enabled": true
  },
  "Logging": {
    "Level": "Information"
  }
}
```

Environment-specific overrides may be added in future versions.

---

# NuGet Packages

Recommended dependencies.

| Purpose | Package |
|----------|---------|
| Audio | NAudio |
| mDNS | Makaretu.Dns.Multicast |
| Hosting | Lightweight application host or Microsoft.Extensions.Hosting version compatible with .NET Framework 4.8 |
| DI | Microsoft.Extensions.DependencyInjection |
| Configuration | Microsoft.Extensions.Configuration.Json |
| Logging | Serilog |
| Logging Sink | Serilog.Sinks.File |
| JSON | System.Text.Json |

The project intentionally avoids unnecessary third-party dependencies.

---

# Class Relationships

High-level class interaction.

```text
MainForm
    │
    ▼
ReceiverService
    │
    ▼
SessionManager
 ┌──┴──────────────┐
 ▼                 ▼
Transport      AudioManager
 │                 │
 ▼                 ▼
Protocol      Decoder
                   │
                   ▼
             AudioRenderer
```

Each subsystem communicates through well-defined interfaces.

---

# Resource Lifetime

Recommended ownership.

```text
Application Host
        │
        ▼
ReceiverService
        │
        ▼
SessionManager
        │
        ├───────┐
        ▼       ▼
Transport AudioManager
```

When the session ends, resources are released in reverse dependency order.

---

# Unit Testing

Recommended test coverage.

Protocol.

- Packet parsing
- Packet serialization
- Version negotiation

Transport.

- Connection lifecycle
- Heartbeat timeout
- Queue handling

Audio.

- Decoder initialization
- Buffer behavior
- Device switching

Configuration.

- JSON loading
- Validation
- Default values

Target coverage.

```text
≥ 80%
```

---

# Integration Testing

Suggested scenarios.

- Android discovery
- Session establishment
- Long-duration streaming
- Network interruption
- Audio device hot-plug
- Multiple receiver discovery
- Graceful shutdown

These tests should be automated where practical.

---

# Continuous Integration

Suggested workflow.

```text
Restore

↓

Build

↓

Unit Tests

↓

Code Analysis

↓

Package

↓

Publish Artifact
```

Release builds should only be produced after successful validation.

---

# Installer

Recommended installer.

```text
Inno Setup
```

Installation tasks.

- Copy application files
- Install runtime if required
- Create Start Menu shortcut
- Create Desktop shortcut (optional)
- Register auto-start (optional)
- Create Windows Firewall rules
- Launch application (optional)

Uninstallation should remove all installed components except user configuration files, unless the user requests complete removal.

---

# Runtime Diagnostics

The application should expose diagnostic information suitable for troubleshooting.

Suggested data.

- Receiver version
- Protocol version
- Listening port
- Active audio device
- Current latency
- Queue depth
- Decoder status
- Discovery status

Diagnostics should be accessible from the UI and log files.

---

# Coding Guidelines

General recommendations.

- One public class per file.
- Prefer immutable models.
- Use async I/O for networking.
- Keep UI logic separate from services.
- Avoid static mutable state.
- Dispose unmanaged resources promptly.
- Validate all external input.

Consistency is preferred over cleverness.

---

# Windows Receiver Summary

The Windows Receiver consists of five primary subsystems.

```text
Discovery

↓

Transport

↓

Protocol

↓

Audio

↓

User Interface
```

The `ReceiverService` coordinates subsystem interaction while `SessionManager` owns runtime state.

The implementation emphasizes:

- Low latency
- Stable playback
- Predictable memory usage
- Windows 7 compatibility
- Long-running reliability
- Clear separation of concerns
- Testability
- Maintainability

This architecture provides a solid foundation for future enhancements such as Opus support, multi-room playback, receiver grouping, remote volume control and encrypted transport.

---

# Related Documents

This document complements the following specifications.

```text
docs/02-Architecture.md
```

Overall system architecture.

```text
docs/03-Protocol.md
```

Wire protocol specification.

```text
docs/04-Android.md
```

Android sender implementation.

```text
docs/06-Audio.md
```

Detailed audio subsystem architecture.

Together, these documents define the complete Version 1 implementation.

---

# End of Document