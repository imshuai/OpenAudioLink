# docs/02-Architecture.md

# Software Architecture

## Overview

OpenAudioLink is designed around a strict layered architecture.

Every subsystem owns a single responsibility.

Layers communicate only with adjacent layers through well-defined interfaces.

No implementation details leak across layer boundaries.

The architecture intentionally avoids tightly coupled modules.

---

# Design Goals

The architecture is designed to satisfy the following requirements.

- Windows 7 compatibility
- Android 10 compatibility
- Low latency
- Simple implementation
- Easy testing
- AI-friendly development
- Cross-platform evolution

---

# Layered Architecture

```
+------------------------------------------------------+
|                    User Interface                    |
+------------------------------------------------------+

+------------------------------------------------------+
|                Application Services                  |
+------------------------------------------------------+

+------------------------------------------------------+
|              Discovery / Configuration               |
+------------------------------------------------------+

+------------------------------------------------------+
|                  Transport Layer                     |
+------------------------------------------------------+

+------------------------------------------------------+
|                   Protocol Layer                     |
+------------------------------------------------------+

+------------------------------------------------------+
|                  Codec Layer                         |
+------------------------------------------------------+

+------------------------------------------------------+
|                 Audio Device Layer                   |
+------------------------------------------------------+

+------------------------------------------------------+
|                 Operating System                     |
+------------------------------------------------------+
```

Each layer has exactly one responsibility.

A layer may only communicate with the layer immediately below it.

---

# Layer Responsibilities

## User Interface

Responsibilities:

- Display status
- Display latency
- Select output device
- Modify configuration
- View logs

The UI must never:

- Parse packets
- Decode AAC
- Manage sockets

---

## Application Services

Coordinates the system.

Responsibilities:

- Start services
- Stop services
- Lifecycle management
- Dependency injection
- Module initialization

Business logic belongs here.

---

## Discovery Layer

Responsible for locating receivers.

Version 1 uses:

- mDNS

Possible future additions:

- SSDP
- Static discovery
- Manual IP

The remaining application should not know how discovery is implemented.

---

## Configuration Layer

Responsible for:

- Loading configuration
- Saving configuration
- Runtime settings
- Default values

Configuration is shared by all modules.

No module should access configuration files directly.

---

## Transport Layer

Responsibilities:

- Socket creation
- TCP connection
- Connection monitoring
- Reconnection
- Keep Alive

The transport layer has no knowledge of audio.

It transports bytes only.

---

## Protocol Layer

Responsible for:

- Packet framing
- Packet validation
- Sequence numbers
- Protocol version
- Message serialization
- Message parsing

The protocol layer does not understand AAC.

It only understands protocol messages.

---

## Codec Layer

Responsibilities:

- AAC encoding (Android)
- AAC decoding (Windows)

Future:

- Opus
- PCM
- FLAC

Changing codecs should not affect transport.

---

## Audio Layer

Responsible for:

- Output device enumeration
- PCM playback
- Buffering
- Volume control

The audio engine receives decoded PCM only.

It never receives network packets.

---

# Android Architecture

```
Android

┌────────────────────┐
│      Activity      │
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│  ForegroundService │
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│ AudioCaptureEngine │
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│   AAC Encoder      │
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│ Protocol Encoder   │
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│    TCP Client      │
└────────────────────┘
```

Each component has exactly one responsibility.

---

# Windows Architecture

```
Windows

┌────────────────────┐
│      WinForms      │
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│ Receiver Service   │
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│ Protocol Decoder   │
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│ AAC Decoder        │
└─────────┬──────────┘
          │
          ▼
┌────────────────────┐
│ Audio Output       │
└────────────────────┘
```

Networking and playback remain completely independent.

---

# Core Components

The receiver consists of the following major components.

| Component | Responsibility |
|------------|---------------|
| ReceiverService | Connection lifecycle |
| DiscoveryService | mDNS advertisement |
| ProtocolEngine | Packet serialization |
| AudioDecoder | AAC decoding |
| AudioOutput | Playback |
| ConfigManager | Configuration |
| Logger | Logging |
| UI | Visualization |

Each component exposes a public interface.

Internal implementation remains private.

---

# Dependency Rules

Dependencies are intentionally one-way.

```
UI

↓

Application

↓

Receiver

↓

Protocol

↓

Codec

↓

Audio
```

Forbidden dependencies:

Audio

❌ UI

Codec

❌ Configuration

Protocol

❌ WinForms

Receiver

❌ NAudio

Violating these rules creates unnecessary coupling.

---

# Interface-Based Design

Every subsystem communicates through interfaces.

Example:

```
IAudioOutput

IAudioDecoder

ITransport

IProtocolEngine

IDiscoveryService

IConfiguration

ILogger
```

Concrete implementations may change.

Interfaces remain stable.

Future implementations become straightforward.

Example:

```
WaveOutAudioOutput

↓

WasapiAudioOutput

↓

ASIOAudioOutput
```

No other module needs modification.

---

# Thread Model

OpenAudioLink avoids large thread pools.

Version 1 uses dedicated threads.

Windows:

Main UI Thread

↓

Receiver Thread

↓

Protocol Thread

↓

Audio Thread

Android:

Main Thread

↓

Capture Thread

↓

Encoder Thread

↓

Transport Thread

Each thread owns a specific task.

No thread performs unrelated work.

---

# Error Handling Philosophy

Errors propagate upward.

```
Audio

↓

Decoder

↓

Protocol

↓

Receiver

↓

UI
```

Lower layers never display dialogs.

Lower layers never terminate the application.

Only the application layer decides how errors are presented.

---

# Execution Flow

This section describes the runtime execution path from application startup to audio playback.

---

## Windows Startup Sequence

```text
Application Start

↓

Load Configuration

↓

Initialize Logger

↓

Initialize Audio Engine

↓

Start Receiver Service

↓

Start Discovery Service

↓

Begin Listening

↓

Wait For Incoming Connections
```

Each initialization step must complete successfully before the next begins.

If initialization fails, previously initialized modules should be shut down gracefully.

---

## Android Startup Sequence

```text
Application Start

↓

Load Configuration

↓

Initialize Logger

↓

Start Foreground Service

↓

Request Audio Capture Permission

↓

Discover Receivers

↓

User Selects Receiver

↓

Connect

↓

Start Audio Capture

↓

Start Encoding

↓

Start Streaming
```

Audio capture never starts automatically.

Explicit user authorization is always required.

---

# Runtime Pipeline

The following diagram illustrates the runtime processing pipeline.

```mermaid
flowchart LR

Capture

Encoder

Packetizer

Transport

Receiver

Parser

Decoder

Output

Capture --> Encoder
Encoder --> Packetizer
Packetizer --> Transport
Transport --> Receiver
Receiver --> Parser
Parser --> Decoder
Decoder --> Output
```

Every stage receives data from exactly one upstream stage.

No stage communicates with stages outside its immediate neighbors.

---

# Module Lifecycle

Every major subsystem follows the same lifecycle.

```
Created

↓

Initialized

↓

Running

↓

Stopping

↓

Stopped

↓

Disposed
```

Once a module enters the Stopped state it cannot return to Running.

A new instance must be created.

---

# Receiver Lifecycle

```text
Receiver Created

↓

Socket Created

↓

Listening

↓

Client Connected

↓

Handshake

↓

Streaming

↓

Disconnected

↓

Listening
```

The receiver always returns to the Listening state after a client disconnects.

The receiver process itself never exits because a client disconnects.

---

# Sender Lifecycle

```text
Idle

↓

Receiver Selected

↓

Connecting

↓

Connected

↓

Capturing

↓

Encoding

↓

Streaming

↓

Disconnected

↓

Idle
```

If the connection is interrupted:

- Capture stops.
- Encoding stops.
- Network resources are released.

Automatic reconnection may optionally restart the workflow.

---

# Connection Model

Version 1 supports one active sender per receiver.

```
Android

↓

Windows Receiver
```

Multiple simultaneous senders are intentionally not supported.

Reasons:

- Simpler synchronization
- Lower implementation complexity
- Reduced resource usage
- Easier user experience

Future versions may introduce receiver groups and multi-client support.

---

# Service Model

Windows consists of several long-running services.

```
Receiver Service

Discovery Service

Audio Service

Configuration Service

Logging Service
```

Each service owns its internal state.

Services communicate only through public interfaces.

---

# Message Flow

Connection establishment follows this sequence.

```text
Android

HELLO
────────────────────────►

                WELCOME

◄────────────────────────

START_STREAM

────────────────────────►

STREAM_READY

◄────────────────────────

Audio Frames

══════════════════════►
```

Control messages are transmitted separately from audio frames.

---

# Audio Flow

PCM never crosses process boundaries.

Android:

```
Playback Audio

↓

PCM

↓

AAC

↓

Protocol Packet
```

Windows:

```
Protocol Packet

↓

AAC

↓

PCM

↓

Speaker
```

Only compressed audio travels across the network.

---

# Buffering Strategy

Version 1 uses a simple FIFO model.

```
Network

↓

Receive Queue

↓

Decoder Queue

↓

Playback Queue

↓

Speaker
```

Each queue has a fixed capacity.

Overflow:

Oldest packets are discarded.

Underflow:

Playback inserts silence until data becomes available.

Dynamic buffering is planned for Version 2.

---

# Clock Strategy

Version 1 treats the receiver clock as authoritative for playback scheduling.

Android timestamps are used only for:

- Diagnostics
- Latency calculation
- Packet ordering

Clock synchronization is intentionally omitted in Version 1.

Future protocol revisions may introduce optional clock synchronization.

---

# State Machines

Each subsystem owns an independent state machine.

Examples include:

- Receiver State
- Sender State
- Discovery State
- Audio Output State
- Connection State

State transitions are fully documented in:

```
specs/connection-state.md
```

Application code should never invent undocumented states.

---

# Event-Driven Architecture

Modules communicate using events instead of direct method chains whenever possible.

Example:

```text
ConnectionEstablished

↓

ReceiverService

↓

AudioEngine

↓

UI

↓

Logger
```

Advantages:

- Loose coupling
- Easier testing
- Better extensibility

---

# Background Processing

The receiver performs the following operations asynchronously:

- Accept connections
- Receive packets
- Decode audio
- Write audio
- Publish discovery service
- Rotate log files

The UI thread remains responsive regardless of network activity.

---

# Failure Isolation

Subsystem failures should remain isolated.

Example:

Audio output failure:

```
Audio Device Removed

↓

Audio Engine

↓

Application Service

↓

Select New Device

↓

Continue Streaming
```

The receiver should not terminate simply because the playback device changed.

Likewise:

Network interruption should not crash the audio subsystem.

Configuration failures should not terminate packet reception.

Each subsystem is responsible only for its own recovery.

---

# Dependency Inversion

High-level modules depend only on interfaces.

Example:

```
ReceiverService

↓

IAudioOutput
```

instead of

```
ReceiverService

↓

WaveOutAudioOutput
```

This allows replacing implementations without modifying business logic.

Examples include:

- WASAPI
- DirectSound
- ASIO
- Dummy Output (for testing)

All can implement the same interface.

# Directory Architecture

The following directory structure is considered the canonical layout of the repository.

```
OpenAudioLink
│
├── README.md
├── LICENSE
├── CHANGELOG.md
├── CONTRIBUTING.md
│
├── docs/
│
├── specs/
│
├── sender-android/
│
├── receiver-win/
│
├── shared/
│
├── examples/
│
├── tools/
│
└── .codex/
```

Every source file belongs to exactly one module.

Circular dependencies are prohibited.

---

# Receiver Architecture

The Windows receiver is composed of several independent assemblies.

```
receiver-win/

src/

    OpenAudioLink.UI/

    OpenAudioLink.App/

    OpenAudioLink.Transport/

    OpenAudioLink.Protocol/

    OpenAudioLink.Audio/

    OpenAudioLink.Codec/

    OpenAudioLink.Discovery/

    OpenAudioLink.Configuration/

    OpenAudioLink.Logging/

tests/

installer/
```

Each assembly is responsible for a single domain.

Assembly references should always point downward.

```
UI

↓

App

↓

Transport

↓

Protocol

↓

Codec

↓

Audio
```

Reverse references are forbidden.

---

# Android Architecture

```
sender-android/

app/

capture/

codec/

transport/

protocol/

discovery/

configuration/

logging/

common/
```

The Android project follows the same dependency rules as Windows.

The goal is to keep both implementations conceptually identical despite using different programming languages.

---

# Shared Module

The Shared module contains platform-neutral definitions.

```
shared/

protocol/

configuration/

constants/

utilities/

models/
```

Examples:

ProtocolVersion

PacketHeader

MessageType

ConfigurationModel

ErrorCode

These definitions should remain identical across every platform.

---

# Object Ownership

Every runtime object has a single owner.

Example:

```
ReceiverService

owns

↓

TcpListener
```

AudioOutput

owns

↓

WaveOutEvent

ConfigurationManager

owns

↓

Configuration File

Ownership should never be ambiguous.

Objects must not be released by modules that do not own them.

---

# Memory Management

OpenAudioLink intentionally minimizes heap allocations during streaming.

Preferred strategy:

```
Receive Buffer

↓

Reuse

↓

Receive Buffer

↓

Reuse

↓

Receive Buffer
```

Avoid:

```
Allocate

↓

Decode

↓

Free

↓

Allocate

↓

Decode

↓

Free
```

Continuous allocation leads to unnecessary garbage collection pauses.

---

# Buffer Ownership

Every buffer has exactly one owner.

Typical flow:

```
Network Buffer

↓

Protocol Buffer

↓

Decoder Buffer

↓

Playback Buffer
```

Buffers are transferred, not shared.

After ownership changes, the previous owner must no longer modify the buffer.

---

# Configuration Architecture

Configuration is centralized.

```
ConfigurationManager

↓

ConfigurationModel

↓

Consumers
```

Consumers receive immutable snapshots.

Configuration changes produce a new snapshot.

This avoids synchronization issues.

---

# Logging Architecture

Logging is asynchronous.

```
Application

↓

Log Queue

↓

Logger Thread

↓

Log File
```

Application threads should never block waiting for disk I/O.

Supported log levels:

```
Trace

Debug

Information

Warning

Error

Critical
```

Logging must never alter application behavior.

---

# Exception Strategy

Exceptions are categorized into three groups.

Recoverable

Examples:

- Packet checksum failure
- Temporary network interruption
- Device discovery timeout

The application continues running.

---

Recoverable With User Action

Examples:

- Audio device removed
- Permission denied
- Invalid configuration

The application requests user intervention.

---

Fatal

Examples:

- Configuration corruption
- Unsupported protocol version
- Missing runtime dependency

Application startup fails gracefully.

---

# Resource Management

Every disposable resource follows the same lifecycle.

```
Create

↓

Initialize

↓

Use

↓

Dispose
```

Examples:

Socket

File

Audio Device

Encoder

Decoder

Thread

Timer

Every resource must be released exactly once.

---

# Synchronization Rules

Version 1 minimizes shared mutable state.

Preferred communication:

```
Queue

↓

Consumer
```

instead of

```
Global Object

↓

Multiple Threads
```

This dramatically reduces locking complexity.

Where synchronization is required:

- lock (C#)
- synchronized primitives
- ConcurrentQueue
- BlockingCollection

Long-held locks are prohibited.

---

# Scalability

The architecture is intentionally designed for future growth.

Possible future receivers:

```
receiver-linux

receiver-macos

receiver-rpi

receiver-openwrt
```

Possible future senders:

```
sender-ios

sender-linux

sender-macos
```

The protocol should remain unchanged.

Only platform implementations differ.

---

# Extensibility

Future codecs should require only a new codec implementation.

```
ICodec

↓

AACCodec

OpusCodec

PCMCodec

FLACCodec
```

Transport remains unchanged.

Likewise, new transports can be introduced.

```
ITransport

↓

TCPTransport

UDPTransport

QUICTransport
```

No codec changes should be required.

---

# Testability

Every module should be executable independently.

Examples:

```
Protocol Parser Test

Audio Decoder Test

Transport Test

Discovery Test

Configuration Test

Playback Test
```

A developer should be able to debug one subsystem without launching the complete application.

This principle greatly improves maintainability and enables efficient AI-assisted code generation.

---

# Performance Architecture

Performance is a primary design objective.

The architecture prioritizes:

- Stable latency
- Predictable CPU usage
- Low memory allocation
- Fast startup
- Continuous playback

Absolute throughput is considered less important than deterministic behavior.

---

# Latency Budget

The target latency for Version 1 is less than 150 milliseconds.

The latency budget is divided approximately as follows.

| Stage | Target |
|--------|-------:|
| Audio Capture | < 10 ms |
| AAC Encoding | < 20 ms |
| Packetization | < 2 ms |
| TCP Transport (LAN) | < 20 ms |
| Packet Parsing | < 2 ms |
| AAC Decoding | < 20 ms |
| Playback Buffer | 40–80 ms |

These values are targets rather than strict guarantees.

Future protocol revisions may reduce the playback buffer through adaptive jitter management.

---

# CPU Budget

Normal operation should consume minimal CPU resources.

Approximate targets:

| Platform | CPU Target |
|-----------|-----------:|
| Android | < 5% |
| Windows | < 3% |

Testing should be performed using 48 kHz stereo audio.

CPU usage should remain stable during long playback sessions.

---

# Memory Budget

Memory usage should remain predictable.

Recommended targets:

| Component | Memory |
|-----------|--------:|
| Receiver | < 50 MB |
| Sender | < 80 MB |

Streaming should not continuously increase memory usage.

Long-running sessions must demonstrate stable memory consumption.

---

# Network Characteristics

Version 1 assumes a reliable local network.

Expected conditions:

- Wi-Fi 5
- Wi-Fi 6
- Gigabit Ethernet
- Typical home LAN

The protocol is not optimized for Internet transmission.

Future protocol revisions may introduce optional WAN support.

---

# Supported Audio Formats

Version 1 transport format:

| Property | Value |
|----------|-------|
| Codec | AAC-LC |
| Channels | 2 |
| Sample Rate | 48000 Hz |
| Bit Depth | 16-bit PCM (before encoding) |
| Bitrate | Configurable |

Future protocol versions may support:

- Opus
- PCM
- FLAC

Codec negotiation will be added through protocol version extensions.

---

# Failure Recovery Strategy

Subsystem failures should be isolated.

Examples:

Connection Lost

```
Streaming

↓

Socket Closed

↓

Stop Decoder

↓

Flush Playback Queue

↓

Return To Listening
```

Audio Device Removed

```
Playback Error

↓

Enumerate Devices

↓

Select Replacement

↓

Resume Playback
```

Discovery Failure

```
mDNS Failure

↓

Retry

↓

Continue Receiver
```

Discovery failure must never interrupt active audio playback.

---

# Shutdown Sequence

Graceful shutdown is required.

Receiver shutdown sequence:

```text
Stop Accepting Connections

↓

Notify Client

↓

Flush Protocol Queue

↓

Stop Decoder

↓

Drain Playback Queue

↓

Release Audio Device

↓

Dispose Network Resources

↓

Save Configuration

↓

Exit
```

Every stage should complete successfully before continuing.

Timeouts should prevent indefinite blocking.

---

# Security Considerations

Version 1 is intended for trusted local networks.

Security goals:

- No arbitrary code execution
- No file transfer
- No remote command execution
- No shell access
- No dynamic plugin loading

All incoming packets must be validated before processing.

Malformed packets must be discarded safely.

Future versions may introduce:

- Optional authentication
- Receiver pairing
- TLS transport
- Session encryption

These features are intentionally excluded from Version 1 to reduce complexity.

---

# Compatibility Strategy

Backward compatibility is an important design goal.

Rules:

- Existing packet types must never change meaning.
- New optional fields should be appended.
- Unsupported messages should be ignored when safe.
- Protocol negotiation determines available features.

Breaking protocol changes require a new major version.

---

# Coding Standards

All implementations should follow common architectural conventions.

General principles:

- One class per file
- One responsibility per class
- Constructor injection preferred
- Interface-first design
- Avoid static mutable state
- Avoid hidden global dependencies

Method guidelines:

- Keep methods short.
- Prefer composition over inheritance.
- Avoid deeply nested logic.
- Prefer immutable models.

Naming guidelines:

Interfaces

```
IAudioOutput

ITransport

ICodec
```

Classes

```
TcpTransport

AacDecoder

ReceiverService

DiscoveryService
```

Events

```
ConnectionEstablished

PacketReceived

PlaybackStarted

PlaybackStopped
```

---

# Architecture Constraints

The following constraints are mandatory.

The UI layer:

- MUST NOT decode audio.
- MUST NOT parse protocol packets.
- MUST NOT access sockets.

The Transport layer:

- MUST NOT know audio codecs.
- MUST NOT access configuration files.

The Codec layer:

- MUST NOT perform networking.
- MUST NOT update UI.

The Audio layer:

- MUST NOT parse packets.
- MUST NOT manage connections.

Violating these constraints introduces unnecessary coupling.

---

# Summary

The OpenAudioLink architecture emphasizes:

- Clear module boundaries
- Strict dependency direction
- Interface-oriented design
- Platform independence
- Predictable runtime behavior
- Long-term maintainability

By separating protocol, transport, codec and playback into independent layers, the project remains easy to understand, easy to test and easy to extend.

This architecture serves as the foundation for all future implementations, including additional operating systems, codecs and transport mechanisms.

---

# Related Documents

The following documents build upon this architecture.

```
docs/03-Protocol.md
```

Defines:

- Binary packet format
- Message definitions
- Connection handshake
- Session management
- Audio frame transport
- Protocol version negotiation

```
docs/04-Android.md
```

Defines the Android sender implementation.

```
docs/05-Windows.md
```

Defines the Windows receiver implementation.

The architecture described in this document is normative for all platform implementations.