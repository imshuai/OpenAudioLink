# docs/01-Introduction.md

# Introduction

## Overview

OpenAudioLink (OAL) is an open, lightweight, low-latency wireless audio transport protocol and reference implementation.

Unlike traditional media streaming technologies such as DLNA, OpenAudioLink is designed specifically for **real-time playback audio transport**.

The project allows an Android device to capture its currently playing audio using the official Android AudioPlaybackCapture API and stream it over a local network to a Windows receiver.

The receiver immediately decodes and plays the incoming audio using the selected Windows playback device.

The protocol has been designed with the following priorities:

- Low latency
- Simple implementation
- Windows 7 compatibility
- Open specification
- AI-friendly architecture
- Long-term extensibility

---

# Motivation

Many existing technologies partially solve the problem of wireless audio playback.

However, each of them introduces limitations that make them unsuitable for an open, lightweight and cross-platform implementation.

OpenAudioLink was created to provide a protocol specifically designed for:

- Android
- Windows
- Local network
- Real-time audio
- Open implementation

The project intentionally avoids dependencies on cloud services or vendor-specific ecosystems.

---

# Project Objectives

The primary objectives are listed below.

## Objective 1

Allow Android devices to transmit playback audio to a Windows computer with minimal latency.

---

## Objective 2

Require no Bluetooth hardware.

Communication must occur entirely over a local IP network.

---

## Objective 3

Avoid proprietary protocols.

Every packet, message and state transition must be publicly documented.

---

## Objective 4

Keep deployment simple.

The Windows receiver should be distributed as a standalone application.

The Android sender should require only standard Android permissions.

No root access.

No custom ROM.

No modified firmware.

---

## Objective 5

Separate protocol from implementation.

Applications communicate using OpenAudioLink Protocol.

Neither side depends on platform-specific implementation details.

This allows future implementations on:

- Linux
- macOS
- Raspberry Pi
- Embedded Linux
- NAS devices
- OpenWrt
- Other operating systems

---

# Non-Goals

OpenAudioLink intentionally does not attempt to replace existing media protocols.

The following features are explicitly outside the scope of Version 1.

## Media Library

The project does not index or organize music libraries.

---

## Media Sharing

The project does not expose files to other devices.

---

## DLNA Renderer

The project does not implement UPnP AVTransport.

---

## Chromecast Receiver

The project is not intended to emulate Google Cast.

---

## AirPlay Receiver

The project is inspired by the AirPlay user experience but does not implement Apple's proprietary protocol.

---

## Screen Mirroring

Only audio is transported.

Video transport is outside the scope of the project.

---

# Design Philosophy

OpenAudioLink follows several core principles.

## Simplicity

Every subsystem should be understandable in isolation.

The codebase should avoid unnecessary abstraction.

---

## Predictability

Every protocol message must have deterministic behavior.

Hidden state should be avoided whenever possible.

---

## Extensibility

Adding new codecs should not require changes to unrelated modules.

Adding new platforms should not require protocol redesign.

---

## Platform Independence

The protocol specification should remain independent from Android or Windows APIs.

Implementations may differ internally while remaining wire-compatible.

---

## Testability

Every subsystem should be testable independently.

Examples:

- Packet Parser
- Encoder
- Decoder
- Discovery
- Configuration
- Audio Pipeline

Each component should have standalone unit tests.

---

# High-Level Workflow

The complete workflow is shown below.

```text
Start Receiver

↓

Receiver advertises itself

↓

Android discovers receiver

↓

User selects receiver

↓

Connection established

↓

Permission granted

↓

Audio capture begins

↓

Audio encoding

↓

Network transmission

↓

Packet reception

↓

Audio decoding

↓

Playback

↓

Connection monitoring

↓

Graceful shutdown
```

---

# System Components

OpenAudioLink consists of six major components.

| Component | Description |
|-----------|-------------|
| Sender | Android application |
| Receiver | Windows application |
| Protocol | Packet format |
| Discovery | Device discovery |
| Audio Engine | Encoding and decoding |
| Configuration | Runtime settings |

Each component is documented independently.

---

# Versioning Strategy

OpenAudioLink uses semantic versioning.

```
MAJOR.MINOR.PATCH
```

Rules:

MAJOR

Breaking protocol changes.

MINOR

Backward-compatible features.

PATCH

Bug fixes.

The protocol version is negotiated during connection establishment.

This allows future protocol evolution without breaking compatibility.

---

# Future Vision

OpenAudioLink is intended to become a reusable protocol for wireless audio transport.

The reference implementation targets Android and Windows.

Future implementations may include additional platforms while remaining compatible with the published protocol specification.

The protocol should eventually become stable enough that independent implementations can interoperate without sharing application code.

---

**Next Document**

```
docs/02-Architecture.md
```

This document defines the complete software architecture, module boundaries, dependency rules, threading model, audio pipeline and network architecture used throughout the project.