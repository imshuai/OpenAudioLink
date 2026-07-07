# docs/04-Android.md

# Android Sender Implementation

Version: 1.0

Target Platform:

Android 10 (API 29) and later

Language:

Kotlin

Minimum SDK:

29

Target SDK:

Latest Stable

---

# Overview

The Android application is responsible for capturing the device's playback audio and streaming it to an OpenAudioLink receiver.

The application performs five primary functions:

- Receiver Discovery
- Audio Capture
- Audio Encoding
- Network Transport
- Session Management

Unlike traditional media player applications, OpenAudioLink does not decode or render media.

Its only responsibility is to transport playback audio with minimal latency.

---

# High-Level Architecture

```text
UI

↓

Foreground Service

↓

Session Manager

↓

Discovery

↓

Transport

↓

Protocol

↓

AAC Encoder

↓

Audio Capture
```

Each layer communicates only with its neighboring layer.

---

# Design Principles

The Android implementation follows these principles.

- Single responsibility
- Foreground execution
- Event-driven architecture
- Coroutine-based concurrency
- Minimal battery consumption
- Automatic recovery
- Zero UI dependency during streaming

Streaming continues even if the Activity is destroyed.

---

# Project Structure

```
sender-android/

app/

src/

main/

java/

com.openaudiolink/

    ui/

    service/

    capture/

    codec/

    protocol/

    transport/

    discovery/

    repository/

    configuration/

    logging/

    common/

    model/

    util/
```

Each package represents one subsystem.

Cross-package dependencies should remain minimal.

---

# Application Components

The application consists of the following major components.

| Component | Responsibility |
|-----------|---------------|
| MainActivity | User Interface |
| StreamingService | Foreground Service |
| SessionManager | Session lifecycle |
| DiscoveryManager | Receiver discovery |
| AudioCaptureEngine | Playback capture |
| AudioEncoder | AAC encoding |
| TransportClient | TCP communication |
| ProtocolEngine | Packet serialization |
| ConfigurationManager | Persistent settings |
| Logger | Logging |

Each component exposes a stable public interface.

---

# Application Lifecycle

```text
App Launch

↓

Initialize

↓

Load Configuration

↓

Initialize Discovery

↓

Display Receiver List

↓

User Connects

↓

Start Foreground Service

↓

Start Capture

↓

Start Streaming
```

Closing the Activity does not stop streaming.

Stopping the Foreground Service ends the session.

---

# Foreground Service

Streaming is implemented inside a Foreground Service.

Reasons:

- Android background restrictions
- Long-running operation
- Stable process priority
- Persistent notification

The service owns:

- Audio Capture
- Encoder
- Transport
- Protocol
- Session State

The Activity owns only the user interface.

---

# Service Lifecycle

```text
Created

↓

Initialized

↓

Running

↓

Streaming

↓

Stopping

↓

Destroyed
```

The service may exist without an active stream.

This allows discovery to continue while waiting for user interaction.

---

# MainActivity

Responsibilities:

- Display discovered receivers
- Show connection state
- Display latency
- Display log summary
- Manage permissions
- Open settings

The Activity must never:

- Capture audio
- Encode AAC
- Manage sockets
- Parse protocol packets

---

# StreamingService

Responsibilities:

- Start and stop sessions
- Coordinate subsystems
- Handle reconnection
- Maintain foreground notification
- Publish runtime state

The service acts as the application's orchestration layer.

---

# Session Manager

The Session Manager coordinates every streaming session.

Responsibilities:

- Connect
- Disconnect
- Retry
- Handshake
- Heartbeat
- Recovery

Only one Session Manager instance exists.

---

# Dependency Graph

```text
MainActivity

↓

StreamingService

↓

SessionManager

↓

DiscoveryManager

↓

TransportClient

↓

ProtocolEngine

↓

AudioEncoder

↓

AudioCaptureEngine
```

Dependencies always point downward.

Reverse dependencies are prohibited.

---

# Runtime State

The application maintains a single runtime state.

```text
Idle

↓

Discovering

↓

Connecting

↓

Connected

↓

Streaming

↓

Reconnecting

↓

Disconnected
```

All UI elements observe this state.

No component should maintain conflicting state machines.

---

# Data Flow

```text
Playback Audio

↓

AudioPlaybackCapture

↓

PCM Buffer

↓

AAC Encoder

↓

Protocol Packet

↓

TCP Socket

↓

Receiver
```

Each stage processes only one data format.

---

# Thread Model

Recommended coroutine dispatchers.

| Component | Dispatcher |
|-----------|------------|
| UI | Main |
| Discovery | IO |
| TCP | IO |
| Protocol | Default |
| AAC Encoder | Default |
| Audio Capture | Default |
| Logging | IO |

Blocking operations should never execute on the Main dispatcher.

---

# Dependency Injection

Version 1 recommends constructor injection.

Example.

```text
StreamingService

↓

SessionManager

↓

TransportClient

↓

ProtocolEngine
```

Interfaces should be injected instead of concrete implementations.

This greatly improves testing.

---

# Package Responsibilities

ui/

Only presentation.

service/

Foreground service.

capture/

AudioPlaybackCapture implementation.

codec/

MediaCodec wrapper.

protocol/

Packet encoding.

transport/

TCP client.

configuration/

Persistent configuration.

logging/

Structured logging.

common/

Shared utilities.

No package should own responsibilities belonging to another package.

---

# Audio Capture Engine

The Audio Capture Engine is responsible for obtaining playback audio from the Android operating system.

It is the first stage of the audio pipeline.

No encoding or networking logic belongs to this component.

---

# Capture Technology

Version 1 uses the official Android APIs.

Primary APIs:

- MediaProjection
- AudioPlaybackCaptureConfiguration
- AudioRecord

No private APIs are required.

No root privileges are required.

---

# Audio Pipeline

```text
Application Audio

↓

Android Audio Mixer

↓

AudioPlaybackCapture

↓

PCM Buffer

↓

AAC Encoder
```

Captured audio is always PCM.

Compression occurs only after capture.

---

# MediaProjection

Android requires explicit user authorization before playback capture may begin.

Typical workflow:

```text
Request Permission

↓

System Dialog

↓

User Grants Permission

↓

MediaProjection

↓

Capture Session
```

Permission cannot be granted silently.

---

# Permission Lifecycle

Permission is valid only while the associated MediaProjection remains active.

If the projection terminates:

```text
Projection Stopped

↓

Stop Capture

↓

Stop Encoder

↓

Notify SessionManager

↓

Disconnect Receiver
```

Streaming must never continue after permission has been revoked.

---

# Capture Configuration

Recommended defaults.

| Property | Value |
|----------|------|
| Sample Rate | 48000 Hz |
| Channels | Stereo |
| Encoding | PCM 16-bit |
| Source | AudioPlaybackCapture |

These values should match the protocol defaults whenever possible.

---

# AudioRecord Configuration

Recommended initialization.

| Parameter | Value |
|-----------|------|
| AudioFormat | PCM_16BIT |
| Channel Mask | CHANNEL_IN_STEREO |
| Sample Rate | 48000 |
| Transfer Mode | Streaming |

Buffer size:

```
2 × Minimum Buffer Size
```

The actual value may be increased on devices requiring larger internal buffers.

---

# Capture Filters

AudioPlaybackCaptureConfiguration should capture media playback only.

Recommended usages:

Included:

- Media
- Games

Excluded:

- Alarm
- Notification
- Accessibility
- Voice Communication

This minimizes unwanted system sounds.

---

# Capture Lifecycle

```text
Create Configuration

↓

Create AudioRecord

↓

Start Recording

↓

Read PCM

↓

Deliver Frames

↓

Stop Recording

↓

Release Resources
```

Every AudioRecord instance should be released after use.

---

# Capture Thread

Audio capture runs independently of the UI.

Recommended coroutine:

```
Dispatchers.Default
```

Pseudo workflow:

```text
while (capturing)

↓

AudioRecord.read()

↓

PCM Queue

↓

Encoder
```

Blocking reads are acceptable.

Busy polling should be avoided.

---

# PCM Frame Size

Recommended frame duration:

```
20 ms
```

For:

```
48000 Hz

Stereo

16-bit
```

PCM size:

```
3840 Bytes
```

Calculation:

```
48000

×

2 Channels

×

2 Bytes

×

20 ms

=

3840 Bytes
```

The encoder receives one PCM frame at a time.

---

# PCM Queue

Capture and encoding are separated by a queue.

```text
AudioRecord

↓

PCM Queue

↓

AAC Encoder
```

Benefits:

- Decouples capture timing
- Smooths temporary encoder delays
- Simplifies threading

Recommended capacity:

```
5 Frames
```

---

# Queue Overflow

Overflow indicates the encoder cannot keep pace.

Handling:

```text
PCM Queue Full

↓

Discard Oldest Frame

↓

Insert New Frame
```

Maintaining low latency is preferred over preserving every frame.

---

# Queue Underflow

Underflow indicates the capture source has temporarily stopped producing data.

Handling:

```text
Queue Empty

↓

Wait

↓

Resume Capture
```

Artificial silence should not be generated at this stage.

Silence insertion occurs only on the receiver.

---

# Audio Session Changes

Android may recreate audio sessions.

Examples:

- Headphones connected
- Bluetooth device connected
- Output device changed

Capture engine behavior:

```text
Audio Route Changed

↓

Restart AudioRecord

↓

Continue Capture
```

Session restart should be transparent to the user.

---

# Audio Focus

The application does not request exclusive audio focus.

Reasons:

- It is not a media player.
- It should not interrupt other applications.
- It only observes playback.

---

# MediaProjection Callback

The implementation should register a callback.

Events:

```text
Projection Started

Projection Stopped

Projection Revoked
```

If Projection Stopped is received:

- Stop capture.
- Notify SessionManager.
- Release resources immediately.

---

# Resource Ownership

AudioCaptureEngine owns:

- MediaProjection
- AudioPlaybackCaptureConfiguration
- AudioRecord
- Capture Coroutine

Ownership is never shared.

The SessionManager starts and stops the engine through its public interface only.

---

# Error Handling

Recoverable errors.

Examples:

- Temporary AudioRecord read failure
- Route change
- Buffer starvation

Behavior:

```
Retry

↓

Continue
```

Fatal errors.

Examples:

- MediaProjection revoked
- AudioRecord initialization failure
- Unsupported device

Behavior:

```
Stop Capture

↓

Notify SessionManager

↓

Terminate Stream
```

---

# Performance Targets

Recommended performance goals.

| Metric | Target |
|--------|-------:|
| Capture Latency | < 10 ms |
| AudioRecord Read | < 5 ms |
| Queue Delay | < 20 ms |
| CPU Usage | < 3% |

These values assume a modern Android device running Android 10 or later.

---

# Testing

The Audio Capture Engine should be testable independently.

Recommended tests:

- Permission Granted
- Permission Denied
- AudioRecord Creation
- Capture Start
- Capture Stop
- Route Change
- Projection Revoked
- Long-duration Capture
- Buffer Overflow
- Buffer Underflow

The engine should operate correctly without requiring network connectivity.

---

# Audio Encoder

The Audio Encoder converts captured PCM audio into compressed AAC frames suitable for network transmission.

Encoding is isolated from audio capture and networking.

The encoder receives PCM frames and produces encoded AAC frames.

---

# Encoding Pipeline

```text
PCM Queue

↓

MediaCodec

↓

AAC Queue

↓

Protocol Engine

↓

Transport
```

The encoder is unaware of networking, protocol framing and session management.

---

# Codec Technology

Version 1 uses Android's hardware-accelerated MediaCodec API.

Preferred MIME type:

```
audio/mp4a-latm
```

The implementation should always prefer hardware encoders when available.

Software encoding should be used only as a fallback.

---

# Encoder Configuration

Recommended defaults.

| Property | Value |
|----------|------|
| Codec | AAC-LC |
| Sample Rate | 48000 Hz |
| Channels | 2 |
| Bitrate | 192 kbps |
| Profile | AAC-LC |
| Frame Duration | 20 ms |

Bitrate should be configurable by the user.

---

# Supported Bitrates

Recommended presets.

| Quality | Bitrate |
|----------|---------:|
| Low | 96 kbps |
| Medium | 128 kbps |
| High | 192 kbps |
| Very High | 256 kbps |
| Lossless Future | PCM |

Version 1 defaults to:

```
192 kbps
```

---

# Encoder Lifecycle

```text
Create MediaCodec

↓

Configure

↓

Start

↓

Encode Frames

↓

Flush

↓

Stop

↓

Release
```

MediaCodec instances should never be reused across streaming sessions.

---

# Encoder Thread

The encoder runs independently.

Recommended dispatcher:

```
Dispatchers.Default
```

Workflow:

```text
PCM Queue

↓

Input Buffer

↓

MediaCodec

↓

Output Buffer

↓

AAC Queue
```

Encoding should never occur on the UI thread.

---

# MediaCodec Input

Input consists exclusively of PCM frames.

Each frame contains:

```
20 ms

Stereo

48000 Hz

16-bit PCM
```

Input timestamps should originate from the Audio Capture Engine.

---

# MediaCodec Output

Output consists of AAC access units.

Each access unit is immediately wrapped into an AUDIO packet.

No additional buffering occurs inside the encoder.

---

# Encoder Queue

AAC output is stored in a queue before protocol serialization.

```text
MediaCodec

↓

AAC Queue

↓

Protocol Engine
```

Recommended queue capacity:

```
10 Frames
```

---

# Queue Overflow

Overflow indicates that network transmission is slower than encoding.

Handling:

```text
AAC Queue Full

↓

Discard Oldest AAC Frame

↓

Continue
```

Latency always takes precedence over completeness.

---

# Queue Underflow

Underflow occurs when no encoded data is available.

Behavior:

```text
Wait

↓

Next Frame
```

The encoder does not generate silence.

---

# Timestamp Preservation

The original capture timestamp must be preserved.

Pipeline:

```text
Capture

↓

PCM Timestamp

↓

AAC Frame

↓

Protocol Packet
```

MediaCodec output timestamps should be replaced with the original capture timestamp if necessary.

---

# Encoder Errors

Recoverable:

- Temporary MediaCodec timeout
- Output buffer unavailable

Behavior:

```
Retry
```

Fatal:

- Codec creation failure
- Unsupported profile
- Illegal MediaCodec state

Behavior:

```text
Release Codec

↓

Notify SessionManager

↓

Stop Streaming
```

---

# Transport Client

The Transport Client owns the TCP connection.

Responsibilities:

- Connect
- Send Packets
- Receive Packets
- Heartbeat
- Reconnect

It is completely unaware of audio encoding.

---

# Transport Lifecycle

```text
Disconnected

↓

Connecting

↓

Connected

↓

Streaming

↓

Disconnected
```

Only one TCP connection exists per session.

---

# TCP Configuration

Recommended socket options.

| Option | Value |
|--------|------|
| TCP_NODELAY | Enabled |
| SO_KEEPALIVE | Enabled |
| Receive Buffer | 64 KB |
| Send Buffer | 64 KB |

TCP_NODELAY minimizes latency.

---

# Send Queue

Outgoing packets are serialized through a dedicated queue.

```text
Protocol Packet

↓

Send Queue

↓

TCP Socket
```

This prevents blocking the encoder.

Recommended capacity:

```
128 Packets
```

---

# Connection Failure

Unexpected socket closure.

```text
Socket Closed

↓

Stop Encoder

↓

Stop Capture

↓

Reconnect

↓

Handshake

↓

Resume Streaming
```

Automatic reconnection should be enabled by default.

---

# Reconnection Policy

Suggested strategy.

```
Attempt 1

1 second

↓

Attempt 2

2 seconds

↓

Attempt 3

4 seconds

↓

Attempt 4

8 seconds

↓

Maximum

30 seconds
```

Exponential backoff prevents unnecessary network traffic.

---

# Heartbeat

Transport periodically sends:

```
PING
```

Expected response:

```
PONG
```

Failure sequence:

```text
Heartbeat Timeout

↓

Close Socket

↓

Reconnect
```

The heartbeat timer should pause when no session is active.

---

# Protocol Engine

Responsibilities:

- Packet Serialization
- Packet Parsing
- Header Validation
- Version Negotiation

The Protocol Engine is completely stateless.

All session state belongs to SessionManager.

---

# Serialization Pipeline

```text
AAC Frame

↓

Protocol Header

↓

Payload

↓

Byte Array

↓

Transport
```

Serialization should minimize memory allocation.

Reusable buffers are recommended.

---

# Packet Parsing

Incoming packets:

```text
Receive Bytes

↓

Header Validation

↓

Payload Parsing

↓

Dispatch
```

Malformed packets should never propagate beyond the Protocol Engine.

---

# Performance Goals

Recommended targets.

| Metric | Target |
|--------|-------:|
| AAC Encode | < 20 ms |
| Packet Serialize | < 1 ms |
| Send Queue Delay | < 5 ms |
| TCP Send | < 5 ms |
| Total Encode Pipeline | < 30 ms |

These values are intended for a typical mid-range Android device.

---

# User Interface Architecture

Version 1 uses a modern Android application architecture.

Recommended stack:

| Layer | Technology |
|---------|-----------|
| UI | Jetpack Compose |
| State | StateFlow |
| Lifecycle | ViewModel |
| Storage | DataStore |
| Database | Room |
| DI | Hilt |
| Async | Kotlin Coroutines |

The UI layer must remain completely independent from streaming logic.

---

# MVVM Architecture

Application structure.

```text
UI

↓

ViewModel

↓

Repository

↓

Service Layer

↓

Streaming Components
```

Data flows downward.

Events flow upward.

---

# MainActivity

MainActivity acts only as a host.

Responsibilities:

- Permission requests
- Navigation
- Compose root
- Service binding

Responsibilities explicitly excluded:

- Networking
- Audio capture
- Protocol handling
- Encoding

---

# Application Screens

Version 1 recommends four screens.

```text
Home

Settings

Logs

About
```

Additional screens may be added later.

---

# Home Screen

Purpose:

Control streaming.

Displayed information:

- Receiver list
- Connection status
- Current receiver
- Latency
- Bitrate
- Audio level
- Stream duration

Actions:

- Connect
- Disconnect
- Refresh discovery

---

# Home Screen Layout

```text
+--------------------------------+

OpenAudioLink

--------------------------------

Receivers

[ Office-PC ]

[ LivingRoom-PC ]

--------------------------------

Status

Connected

Latency

87 ms

Bitrate

192 kbps

--------------------------------

[ CONNECT ]

+--------------------------------+
```

The layout should remain simple and easy to operate with one hand.

---

# Settings Screen

Configuration options.

General:

- Receiver Name
- Auto Connect
- Auto Reconnect

Audio:

- Bitrate
- Frame Duration

Network:

- Discovery Enabled
- Discovery Interval

Logging:

- Log Level

Advanced:

- Debug Mode

---

# Logs Screen

Displays recent runtime events.

Examples:

```text
18:02 Connected

18:02 Handshake Completed

18:02 Stream Started

18:12 Queue Underflow

18:14 Reconnected
```

Log viewing is diagnostic only.

Streaming must not depend on the log subsystem.

---

# About Screen

Displays:

- App Version
- Protocol Version
- License Information
- Open Source Notices
- Device Information

No operational functionality belongs here.

---

# ViewModel Layer

Every screen owns a ViewModel.

Examples:

```text
HomeViewModel

SettingsViewModel

LogsViewModel
```

ViewModels expose immutable UI state.

---

# StateFlow

UI state should be represented by StateFlow.

Example.

```text
ReceiverState

↓

StateFlow

↓

Compose UI
```

The UI never polls for updates.

All updates are reactive.

---

# UI State Model

Recommended HomeState.

```text
data class HomeState(

receivers,

selectedReceiver,

connectionState,

latency,

bitrate,

duration

)
```

State objects should be immutable.

---

# Repository Layer

Repositories isolate application state from infrastructure.

Examples.

```text
SettingsRepository

ReceiverRepository

LogRepository

SessionRepository
```

Repositories expose flows.

They do not expose mutable state.

---

# Service Binding

The UI communicates with StreamingService through a Binder interface.

Flow:

```text
Activity

↓

Service Binder

↓

SessionManager
```

Direct references to internal streaming components are prohibited.

---

# Foreground Notification

StreamingService must always display a foreground notification.

Notification states.

Disconnected:

```text
OpenAudioLink

Ready
```

Connected:

```text
OpenAudioLink

Connected to Office-PC
```

Streaming:

```text
OpenAudioLink

Streaming Audio
```

---

# Notification Actions

Recommended actions.

```text
Disconnect

Stop Service
```

Optional future actions.

```text
Mute

Pause

Switch Receiver
```

---

# Permission Management

Required permissions.

Android 10+

```text
FOREGROUND_SERVICE
```

Android 13+

```text
POST_NOTIFICATIONS
```

MediaProjection permission is requested at runtime.

---

# Permission Workflow

```text
Launch App

↓

Request Notification Permission

↓

User Accepts

↓

Request MediaProjection

↓

User Accepts

↓

Enable Streaming
```

Streaming cannot begin until all required permissions are granted.

---

# Discovery Manager

Responsibilities.

- Browse receivers
- Track receiver lifecycle
- Publish updates
- Remove expired receivers

Discovery is independent of connection state.

---

# Receiver Model

Recommended structure.

```text
Receiver

id

name

host

port

protocolVersion

lastSeen

capabilities
```

Receiver identifiers should remain stable.

---

# Receiver Expiration

Receivers are removed when:

```text
Current Time

-

Last Seen

>

30 Seconds
```

Expired receivers disappear from the UI automatically.

---

# DataStore

DataStore stores lightweight settings.

Examples.

```text
bitrate

autoReconnect

preferredReceiver

debugMode
```

DataStore replaces SharedPreferences.

---

# Room Database

Room stores structured historical data.

Examples.

```text
Logs

Connection History

Statistics
```

Large datasets should never be stored in DataStore.

---

# Logging Architecture

Recommended flow.

```text
Component

↓

Logger

↓

LogRepository

↓

Room

↓

UI
```

Components should never write directly to the database.

---

# Application State Synchronization

Global application state originates from SessionManager.

```text
SessionManager

↓

Repository

↓

StateFlow

↓

ViewModel

↓

Compose
```

This guarantees a single source of truth.

---

# Dependency Injection

Recommended Hilt modules.

```text
NetworkModule

DiscoveryModule

StorageModule

ProtocolModule

AudioModule
```

All services should be injected through interfaces.

---

# Package Expansion

Additional package structure.

```text
ui/

home/

settings/

logs/

about/

viewmodel/

repository/

database/

datastore/
```

Feature-based organization is preferred.

---

# UI Performance Requirements

Recommended targets.

| Metric | Target |
|----------|-------:|
| First Launch | < 2 s |
| Screen Switch | < 100 ms |
| Receiver Refresh | < 500 ms |
| State Update | < 16 ms |

The UI should remain responsive while streaming is active.

---

# Background Execution

Continuous audio streaming requires long-running execution.

The application is designed around a Foreground Service.

Streaming must remain active while:

- The screen is off
- The device is locked
- The UI Activity has been destroyed
- The user switches to another application

The Foreground Service is the only component responsible for maintaining an active streaming session.

---

# Android Version Compatibility

Supported Android versions.

| Android | API | Supported |
|----------|----:|-----------|
| Android 10 | 29 | ✓ |
| Android 11 | 30 | ✓ |
| Android 12 | 31 | ✓ |
| Android 13 | 33 | ✓ |
| Android 14 | 34 | ✓ |
| Android 15 | 35 | ✓ |
| Android 16+ | Future | Planned |

Android 10 is the minimum supported version because AudioPlaybackCapture was introduced in API 29.

---

# Process Lifecycle

Recommended process lifecycle.

```text
Application

↓

Foreground Service

↓

Streaming Session

↓

Receiver Connected

↓

Streaming Active

↓

Receiver Disconnected

↓

Service Idle

↓

Service Stopped
```

The service should remain alive for a short configurable grace period after streaming ends to allow quick reconnection.

---

# Automatic Reconnection

Unexpected network interruptions should trigger automatic reconnection.

Recommended sequence.

```text
Connection Lost

↓

Stop Transport

↓

Stop Encoder

↓

Keep Capture Ready

↓

Reconnect

↓

Handshake

↓

Resume Streaming
```

If reconnection fails after the configured retry limit, capture should stop and the user should be notified.

---

# Network Monitoring

The application should register a `ConnectivityManager.NetworkCallback`.

Monitor:

- Wi-Fi availability
- Network loss
- Network capability changes
- IP address changes

Network callbacks should notify the `SessionManager`, which decides whether to reconnect.

---

# Wi-Fi to Mobile Data Transition

Version 1 is intended for local network streaming.

If Wi-Fi disconnects and the device switches to mobile data:

```text
Wi-Fi Lost

↓

Streaming Interrupted

↓

Discovery Suspended

↓

Attempt Reconnect

↓

Fail

↓

Notify User
```

The application should not automatically stream over mobile data unless explicitly supported in a future version.

---

# Wi-Fi Lock

To reduce the likelihood of Wi-Fi entering power-saving mode during long playback sessions, the application may acquire a high-performance Wi-Fi lock while actively streaming.

Lifecycle:

```text
Streaming Started

↓

Acquire WifiLock

↓

Streaming Active

↓

Streaming Stopped

↓

Release WifiLock
```

The lock must always be released.

---

# Wake Lock

A partial wake lock is generally unnecessary for Version 1 because audio playback and the foreground service already keep the application active on most devices.

If testing identifies device-specific issues, an optional `PARTIAL_WAKE_LOCK` may be introduced behind a configuration flag.

Wake locks should never remain held after streaming stops.

---

# Doze Mode

Foreground Services continue to run under Doze, but network scheduling may still be affected on some devices.

Recommendations:

- Avoid unnecessary background work.
- Maintain a lightweight heartbeat.
- Resume quickly after temporary delays.
- Do not attempt to bypass system power management.

---

# Battery Optimization

Some manufacturers aggressively terminate background applications.

Recommended user option.

```text
Settings

↓

Battery Optimization

↓

Allow OpenAudioLink
```

The application should explain why this improves reliability but must continue functioning without it whenever possible.

---

# Foreground Notification Behavior

The notification should always reflect the current state.

Examples.

Idle:

```text
OpenAudioLink

Ready
```

Connecting:

```text
Connecting to Receiver...
```

Streaming:

```text
Streaming

Latency: 82 ms
```

Reconnecting:

```text
Reconnecting...
```

---

# Crash Recovery

Unexpected failures should not leave resources allocated.

Recovery sequence.

```text
Unhandled Exception

↓

Log Error

↓

Release Capture

↓

Release Encoder

↓

Close Socket

↓

Stop Foreground Service
```

The application should start cleanly on the next launch.

---

# Exception Boundaries

Every major subsystem should isolate failures.

Examples.

```text
Capture

↓

try/catch

↓

Report

↓

SessionManager
```

```text
Encoder

↓

try/catch

↓

Report

↓

SessionManager
```

```text
Transport

↓

try/catch

↓

Reconnect
```

Subsystem failures should not propagate across component boundaries.

---

# ANR Prevention

The following operations must never execute on the main thread.

- Socket operations
- Audio capture
- Audio encoding
- Database access
- mDNS discovery
- File I/O

The UI thread should perform presentation only.

---

# Memory Management

Long-running sessions require stable memory usage.

Recommendations:

- Reuse byte buffers.
- Avoid unnecessary object allocation.
- Prefer object pools for protocol buffers.
- Close MediaCodec buffers immediately after use.
- Release AudioRecord resources promptly.

Memory growth over time should be considered a defect.

---

# Logging Strategy

Logging levels.

| Level | Purpose |
|--------|---------|
| Error | Fatal failures |
| Warning | Recoverable issues |
| Information | Session lifecycle |
| Debug | Development diagnostics |

Debug logging should be disabled in release builds by default.

---

# Metrics Collection

Recommended runtime metrics.

| Metric | Description |
|--------|-------------|
| Session Duration | Total streaming time |
| Average Latency | Mean transport latency |
| Peak Latency | Highest observed latency |
| Reconnect Count | Number of reconnections |
| Encoder Restart Count | MediaCodec restarts |
| Capture Restart Count | AudioRecord restarts |

Metrics assist troubleshooting and performance tuning.

---

# Release Build Configuration

Recommended build variants.

```text
debug

release
```

Debug:

- Verbose logging
- Developer tools
- Debug overlay

Release:

- Optimized
- Minimal logging
- ProGuard / R8 enabled
- Signed APK

---

# ProGuard / R8

Reflection-based libraries such as Hilt and Room require appropriate keep rules.

Protocol classes should not rely on reflection where possible.

Generated serialization code is preferred over runtime reflection.

---

# Testing Matrix

Recommended device testing.

| Test | Required |
|------|----------|
| Pixel | ✓ |
| Samsung | ✓ |
| Xiaomi | ✓ |
| OnePlus | ✓ |
| Motorola | ✓ |

Recommended Android versions.

- Android 10
- Android 12
- Android 13
- Android 14
- Latest Stable

---

# Long-duration Testing

Recommended endurance tests.

| Duration | Goal |
|----------|------|
| 1 Hour | Basic stability |
| 4 Hours | Memory verification |
| 8 Hours | Overnight streaming |
| 24 Hours | Production validation |

Memory usage, CPU usage and latency should remain stable throughout the test.

---

# Production Readiness Checklist

Before release.

- [ ] No ANRs
- [ ] No memory leaks
- [ ] Stable MediaProjection
- [ ] Stable AudioRecord
- [ ] Stable MediaCodec
- [ ] Automatic reconnection verified
- [ ] Discovery verified
- [ ] Foreground Service verified
- [ ] Notification verified
- [ ] Release build signed
- [ ] Long-duration test passed

Only after all checklist items pass should a release candidate be produced.

---

# Project Directory

Recommended project layout.

```text
sender-android/

app/

src/main/java/com/openaudiolink/

├── app/
│   ├── OpenAudioLinkApp.kt
│   └── AppInitializer.kt
│
├── ui/
│   ├── home/
│   ├── settings/
│   ├── logs/
│   ├── about/
│   ├── components/
│   └── theme/
│
├── service/
│   ├── StreamingService.kt
│   └── ServiceBinder.kt
│
├── session/
│   ├── SessionManager.kt
│   ├── SessionState.kt
│   └── SessionEvents.kt
│
├── capture/
│   ├── AudioCaptureEngine.kt
│   ├── ProjectionManager.kt
│   └── CaptureConfiguration.kt
│
├── codec/
│   ├── AudioEncoder.kt
│   ├── MediaCodecEncoder.kt
│   └── EncoderConfiguration.kt
│
├── protocol/
│   ├── PacketSerializer.kt
│   ├── PacketParser.kt
│   ├── PacketFactory.kt
│   ├── PacketValidator.kt
│   └── ProtocolConstants.kt
│
├── transport/
│   ├── TcpClient.kt
│   ├── HeartbeatManager.kt
│   └── ConnectionManager.kt
│
├── discovery/
│   ├── MdnsBrowser.kt
│   ├── ReceiverInfo.kt
│   └── DiscoveryManager.kt
│
├── repository/
│   ├── SettingsRepository.kt
│   ├── ReceiverRepository.kt
│   ├── SessionRepository.kt
│   └── LogRepository.kt
│
├── datastore/
│   ├── SettingsStore.kt
│   └── PreferenceKeys.kt
│
├── database/
│   ├── AppDatabase.kt
│   ├── LogEntity.kt
│   ├── LogDao.kt
│   └── Converters.kt
│
├── di/
│   ├── NetworkModule.kt
│   ├── AudioModule.kt
│   ├── DiscoveryModule.kt
│   ├── RepositoryModule.kt
│   └── StorageModule.kt
│
├── logging/
│   ├── Logger.kt
│   ├── LogLevel.kt
│   └── AndroidLogger.kt
│
├── model/
│
├── util/
│
└── common/
```

The structure separates responsibilities and minimizes coupling.

---

# Core Interfaces

Subsystems should communicate through interfaces.

Recommended interfaces.

```text
IAudioCapture

IAudioEncoder

ITransport

IProtocol

IDiscovery

ILogger

ISettingsRepository

ISessionManager
```

Concrete implementations remain replaceable.

---

# Dependency Direction

```text
UI

↓

Interfaces

↓

Implementations

↓

Android APIs
```

Android framework classes should remain at the outermost layer.

Business logic should not depend directly on Android APIs where abstraction is practical.

---

# Session Ownership

Only one component owns session state.

```text
SessionManager
```

Every other component reports events.

Example.

```text
Transport

↓

SessionManager

↓

UI
```

No component should modify session state directly.

---

# Coroutine Scope

Recommended coroutine scopes.

| Component | Scope |
|----------|-------|
| Application | SupervisorJob |
| StreamingService | SupervisorJob |
| Capture | Child Scope |
| Encoder | Child Scope |
| Transport | Child Scope |
| Discovery | Child Scope |

Child failures should not automatically terminate unrelated components.

---

# Event Flow

Recommended event propagation.

```text
Capture Started

↓

SessionManager

↓

Repository

↓

StateFlow

↓

Compose
```

One-directional data flow simplifies debugging.

---

# Hilt Modules

Recommended modules.

```text
AudioModule

TransportModule

ProtocolModule

DiscoveryModule

RepositoryModule

DatabaseModule

DataStoreModule

LoggingModule
```

Each module should expose interfaces instead of concrete classes whenever possible.

---

# Room Schema

Recommended entities.

```text
LogEntity

ConnectionHistoryEntity

ReceiverHistoryEntity
```

Suggested DAO interfaces.

```text
LogDao

HistoryDao

ReceiverDao
```

Database versioning should follow Room migration best practices.

---

# DataStore Schema

Suggested preference keys.

```text
preferred_receiver

auto_connect

auto_reconnect

bitrate

frame_duration

log_level

theme

discovery_enabled
```

Configuration should remain lightweight.

Historical information belongs in Room.

---

# Gradle Modules

Version 1 may begin with a single application module.

Future modularization.

```text
app

core

protocol

transport

audio

discovery

common
```

Separating protocol into its own module simplifies future desktop and server implementations.

---

# Third-party Libraries

Recommended dependencies.

| Purpose | Library |
|---------|----------|
| UI | Jetpack Compose |
| DI | Hilt |
| Database | Room |
| Preferences | DataStore |
| Discovery | JmDNS (Android-compatible fork) or NsdManager wrapper |
| Logging | Timber (optional) |
| Serialization | kotlinx.serialization |
| Coroutines | kotlinx.coroutines |

The project should minimize external dependencies wherever the Android SDK provides sufficient functionality.

---

# Build Configuration

Recommended Gradle configuration.

```text
compileSdk = Latest Stable

minSdk = 29

targetSdk = Latest Stable

Java = 17

Kotlin = Latest Stable
```

Release builds should enable code shrinking and resource optimization.

---

# Git Branch Strategy

Recommended branches.

```text
main

develop

feature/*

release/*

hotfix/*
```

The `main` branch should always remain releasable.

---

# Commit Convention

Recommended format.

```text
feat:

fix:

refactor:

docs:

test:

perf:

build:

ci:

chore:
```

Example.

```text
feat(protocol): implement HELLO packet

fix(capture): restart AudioRecord after route change

docs: update protocol specification
```

Consistent commit messages improve project history and automation.

---

# Continuous Integration

Recommended pipeline.

```text
Checkout

↓

Gradle Build

↓

Unit Tests

↓

Lint

↓

Static Analysis

↓

Assemble Debug

↓

Assemble Release
```

Release artifacts should be generated only after all quality checks pass.

---

# Automated Testing

Recommended categories.

Unit Tests.

- Packet serialization
- Packet parsing
- Session state
- Configuration
- Repository logic

Instrumentation Tests.

- MediaProjection
- AudioRecord
- MediaCodec
- Foreground Service
- Discovery

Manual Tests.

- Receiver discovery
- Streaming
- Reconnection
- Long-duration playback

---

# Documentation Requirements

Every public class should include:

- Purpose
- Responsibilities
- Thread ownership
- Lifecycle
- Public API description

Complex algorithms should include explanatory diagrams where appropriate.

---

# Coding Standards

General recommendations.

- One public class per file.
- Prefer immutable data classes.
- Keep methods short.
- Avoid global mutable state.
- Prefer composition over inheritance.
- Fail fast on invalid input.
- Use structured logging.
- Avoid reflection unless required by a framework.

---

# Android Implementation Summary

The Android sender implementation consists of four major subsystems.

```text
Capture

↓

Encode

↓

Protocol

↓

Transport
```

These subsystems are orchestrated by the SessionManager and exposed to the user through a lightweight MVVM-based interface.

The implementation prioritizes:

- Low latency
- Stability
- Predictable resource usage
- Long-running reliability
- Maintainability
- Testability

The Android application serves as the audio source for the OpenAudioLink ecosystem and forms the reference implementation for future sender platforms.

---

# Related Documents

The Android implementation depends on the following specifications.

```text
docs/02-Architecture.md
```

Defines the overall system architecture.

```text
docs/03-Protocol.md
```

Defines the wire protocol used by the sender.

```text
docs/05-Windows.md
```

Defines the receiver implementation that interoperates with this sender.

Android-specific behavior described here must remain consistent with those documents.

---

# End of Document